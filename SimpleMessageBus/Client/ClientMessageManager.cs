using System;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using ProtoBuf;
using SimpleMessageBus.Abstractions;
using SimpleMessageBus.Buffers;
using SimpleMessageBus.Utils;

namespace SimpleMessageBus.Client
{
    public struct ClientMessageNode<T>
    {
        public T Content;
        public MessageType MessageType;
        public ushort MessageClassId;
        public int MessageId;
        public ushort TimerIndex;
    }

    public class ClientMessageManager
    {
        public int SessionId;

        private const int SerializationWorkersAmount = 3;
        private const int UnserializedMessagesBufferSize = 1100;
        private readonly SemaphoreSlim _unserializedMessagesBackpressure = new(UnserializedMessagesBufferSize);
        private readonly ClientMessageNode<IMessage>[] _unserializedMessages;
        private readonly object _unserializedMessagesLock = new();
        private int _unserializedMessagesIndex;

        private const int RawMessagesBufferSize = 10_000;
        private readonly SemaphoreSlim _rawMessagesBackpressure = new(RawMessagesBufferSize);
        private readonly ClientMessageNode<string>[] _rawMessagesBuffer;
        private readonly object _rawMessagesBufferLock = new();
        private int _rawMessagesBufferIndex;

        // for received messages
        private const int ReceivedWorkersAmount = 2;
        private const int ReceivedMessagesBufferSize = 1500;
        private readonly SemaphoreSlim _receivedMessagesBackpressure = new(ReceivedMessagesBufferSize);
        private readonly ClientMessageNode<byte[]>[] _receivedMessages;
        private readonly object _receivedMessagesLock = new();
        private int _receivedMessagesIndex;
        private readonly IMessagesIdsBinding _messageBinding;

        private readonly AutoResetEvent _receivedMessagesControl = new(false);
        private readonly AutoResetEvent _serializationWorkersControl = new(false);
        private readonly AutoResetEvent _rawMessagesControl = new(false);

        // Shared by workers
        private readonly Dictionary<int, MemoryStream> _workersStreams = new(SerializationWorkersAmount);
        private readonly Dictionary<int, ClientMessageNode<string>[]> _rawChunk = new(SerializationWorkersAmount);

        private readonly Dictionary<int, ClientMessageNode<IMessage>[]>
            _messagesChunk = new(SerializationWorkersAmount);

        private readonly Dictionary<int, int> _rawResultBufferIndex = new(SerializationWorkersAmount);
        private readonly Dictionary<int, int> _messagesResultBufferIndex = new(SerializationWorkersAmount);

        public ReservationCircularBlockingBuffer<ClientMessageNode<IMessage>[]> ReadyToReceive { get; }
        public ReservationCircularBlockingBuffer<byte[]> ReadyToSendBuffer { get; } = new(1_000);

        private int _messagesRpsCounter;
        private int _receivedRpsCounter;

        public ClientMessageManager(IMessagesIdsBinding messagesIdsBinding)
        {
            _messageBinding = messagesIdsBinding;

            ReadyToReceive = new ReservationCircularBlockingBuffer<ClientMessageNode<IMessage>[]>(10_000_000);

            _unserializedMessages = new ClientMessageNode<IMessage>[UnserializedMessagesBufferSize];
            _rawMessagesBuffer = new ClientMessageNode<string>[RawMessagesBufferSize];
            _receivedMessages = new ClientMessageNode<byte[]>[ReceivedMessagesBufferSize];

            for (var i = 0; i < UnserializedMessagesBufferSize; i++)
                _unserializedMessages[i] = new ClientMessageNode<IMessage>();

            for (var i = 0; i < RawMessagesBufferSize; i++)
                _rawMessagesBuffer[i] = new ClientMessageNode<string>();

            for (var i = 0; i < SerializationWorkersAmount; i++)
            {
                _workersStreams[i] = new MemoryStream();
                _rawChunk[i] = Array.Empty<ClientMessageNode<string>>();
                _messagesChunk[i] = Array.Empty<ClientMessageNode<IMessage>>();
                _rawResultBufferIndex[i] = 0;
                _messagesResultBufferIndex[i] = 0;
            }

            for (var i = 0; i < ReceivedMessagesBufferSize; i++)
                _receivedMessages[i] = new ClientMessageNode<byte[]>();
        }

        public Task Start()
        {
            var tasks = new List<Task>();

            for (var i = 0; i < SerializationWorkersAmount; i++)
            {
                var k = i;
                tasks.Add(Task.Run(() => SerializationWorker(k)));
                // tasks.Add(Task.Run(() => CombinedWorker(k)));
            }

            for (var i = 0; i < ReceivedWorkersAmount; i++)
            {
                var k = i;
                tasks.Add(Task.Run(() => ReceivedMessagesWorker(k)));
            }

            tasks.Add(Task.Run(ShowWorkersHit));
            tasks.Add(Task.Run(async () =>
            {
                while (true)
                {
                    _receivedMessagesControl.Set();
                    _serializationWorkersControl.Set();
                    _rawMessagesControl.Set();

                    await Task.Delay(500);
                }
            }));
            tasks.Add(Task.Run(RawTcpMessagesWorker));

            return Task.WhenAny(tasks);
        }

        public void AddMessage(IMessage message, ushort messageClassId)
        {
            _unserializedMessagesBackpressure.Wait();

            lock (_unserializedMessagesLock)
            {
                _unserializedMessages[_unserializedMessagesIndex].Content = message;
                _unserializedMessages[_unserializedMessagesIndex++].MessageClassId = messageClassId;

                if (_unserializedMessagesIndex == 100)
                    _serializationWorkersControl.Set();
            }
        }

        public void AddRawMessage(MessageType messageType, string content, ushort messageClassId, int messageId,
            ushort timerIndex)
        {
            _rawMessagesBackpressure.Wait();

            lock (_rawMessagesBufferLock)
            {
                _rawMessagesBuffer[_rawMessagesBufferIndex].Content = content;
                _rawMessagesBuffer[_rawMessagesBufferIndex].MessageType = messageType;
                _rawMessagesBuffer[_rawMessagesBufferIndex].MessageClassId = messageClassId;
                _rawMessagesBuffer[_rawMessagesBufferIndex].MessageId = messageId;
                _rawMessagesBuffer[_rawMessagesBufferIndex].TimerIndex = timerIndex;
                _rawMessagesBufferIndex++;

                if (messageType == MessageType.Subscribe)
                    _rawMessagesControl.Set();
            }
        }

        public void AddReceivedMessage(byte[] message, ushort messageClass, int messageId, ushort timerIndex)
        {
            _receivedMessagesBackpressure.Wait();

            lock (_receivedMessagesLock)
            {
                _receivedMessages[_receivedMessagesIndex].Content = message;
                _receivedMessages[_receivedMessagesIndex].MessageClassId = messageClass;
                _receivedMessages[_receivedMessagesIndex].MessageId = messageId;
                _receivedMessages[_receivedMessagesIndex].TimerIndex = timerIndex;
                _receivedMessagesIndex++;

                if (_receivedMessagesIndex == 100)
                    _receivedMessagesControl.Set();
            }
        }

        private void SerializationWorker(int workerId)
        {
            try
            {
                while (true)
                {
                    _serializationWorkersControl.WaitOne();

                    int resultBufferIndex;
                    ClientMessageNode<IMessage>[] chunk;

                    lock (_unserializedMessagesLock)
                    {
                        if (_unserializedMessagesIndex == 0)
                            continue;

                        chunk = _unserializedMessages[.._unserializedMessagesIndex];
                        _unserializedMessagesIndex = 0;
                        resultBufferIndex = ReadyToSendBuffer.ReserveBufferIndex();
                    }

                    if (chunk.Length == 0)
                        continue;

                    var ms = _workersStreams[workerId];

                    foreach (var message in chunk)
                    {
                        ms.Write(MessageConfig.CreateTcpHeader(MessageType.Message,
                            message.MessageClassId,
                            message.MessageId, 0));
                        Serializer.Serialize(ms, message.Content);

                        ms.Write(MessageConfig.Delimiter);
                    }

                    Interlocked.Add(ref _messagesRpsCounter, chunk.Length);
                    ReadyToSendBuffer.Set(resultBufferIndex, ms.GetBuffer()[..(int)ms.Position]);

                    _unserializedMessagesBackpressure.Release(chunk.Length);

                    ms.Position = 0;
                }
            }
            catch (Exception e)
            {
                Console.WriteLine(e);
                throw;
            }
        }

        private Task CombinedWorker(int workerId)
        {
            try
            {
                while (true)
                {
                    _serializationWorkersControl.WaitOne();

                    lock (_rawMessagesBufferLock)
                    {
                        if (_rawMessagesBufferIndex > 0)
                        {
                            _rawChunk[workerId] = _rawMessagesBuffer[.._rawMessagesBufferIndex];
                            _rawMessagesBufferIndex = 0;
                            _rawResultBufferIndex[workerId] = ReadyToSendBuffer.ReserveBufferIndex();
                        }
                    }

                    lock (_unserializedMessagesLock)
                    {
                        if (_unserializedMessagesIndex > 0)
                        {
                            _messagesChunk[workerId] = _unserializedMessages[.._unserializedMessagesIndex];
                            _unserializedMessagesIndex = 0;
                            _messagesResultBufferIndex[workerId] = ReadyToSendBuffer.ReserveBufferIndex();
                        }
                    }

                    var ms = _workersStreams[workerId];

                    if (_rawChunk[workerId].Length > 0)
                    {
                        foreach (var message in _rawChunk[workerId])
                        {
                            ms.Write(MessageConfig.CreateTcpHeader(message.MessageType, message.MessageClassId,
                                message.MessageId, message.TimerIndex));

                            if (message.Content != null)
                                ms.Write(message.Content.ToBytes());

                            ms.Write(MessageConfig.Delimiter);
                        }

                        ReadyToSendBuffer.Set(_rawResultBufferIndex[workerId], ms.ToArray());
                        _rawMessagesBackpressure.Release(_rawChunk[workerId].Length);

                        ms.SetLength(0);
                    }

                    _rawChunk[workerId] = Array.Empty<ClientMessageNode<string>>();

                    if (_messagesChunk[workerId].Length <= 0)
                        continue;

                    foreach (var message in _messagesChunk[workerId])
                    {
                        ms.Write(MessageConfig.CreateTcpHeader(MessageType.Message, message.MessageClassId,
                            message.MessageId, 0));
                        Serializer.Serialize(ms, message.Content);
                        ms.Write(MessageConfig.Delimiter);
                    }

                    Interlocked.Add(ref _messagesRpsCounter, _messagesChunk[workerId].Length);

                    ReadyToSendBuffer.Set(_messagesResultBufferIndex[workerId], ms.ToArray());
                    _unserializedMessagesBackpressure.Release(_messagesChunk[workerId].Length);
                    _messagesChunk[workerId] = Array.Empty<ClientMessageNode<IMessage>>();

                    ms.SetLength(0);
                }
            }
            catch (Exception e)
            {
                Console.WriteLine(e);
                throw;
            }
        }

        private async Task ReceivedMessagesWorker(int workerId)
        {
            try
            {
                while (true)
                {
                    _receivedMessagesControl.WaitOne();

                    ClientMessageNode<byte[]>[] messages;
                    int readyIndex;

                    lock (_receivedMessagesLock)
                    {
                        if (_receivedMessagesIndex == 0)
                            continue;

                        messages = _receivedMessages[.._receivedMessagesIndex];
                        _receivedMessagesIndex = 0;
                        readyIndex = ReadyToReceive.ReserveBufferIndex();
                    }

                    if (messages.Length == 0)
                        continue;

                    var result = new ClientMessageNode<IMessage>[messages.Length];
                    var resultIndex = 0;

                    await using var ms = new MemoryStream();

                    foreach (var message in messages)
                    {
                        foreach (var (classType, messageClassId) in _messageBinding.GetMessagesIdsBinding())
                        {
                            if (messageClassId != message.MessageClassId)
                                continue;

                            await ms.WriteAsync(message.Content);
                            ms.Position = 0;

                            result[resultIndex].Content = (IMessage)Serializer.NonGeneric.Deserialize(classType, ms);
                            result[resultIndex].MessageClassId = messageClassId;
                            result[resultIndex].MessageId = message.MessageId;
                            result[resultIndex].TimerIndex = message.TimerIndex;
                            resultIndex++;

                            ms.SetLength(0);
                        }
                    }

                    Interlocked.Add(ref _receivedRpsCounter, messages.Length);
                    ReadyToReceive.Set(readyIndex, result);
                    _receivedMessagesBackpressure.Release(messages.Length);
                }
            }
            catch (Exception e)
            {
                Console.WriteLine(e);
                throw;
            }
        }

        private void RawTcpMessagesWorker()
        {
            try
            {
                while (true)
                {
                    ClientMessageNode<string>[] chunk;
                    int resultBufferIndex;

                    _rawMessagesControl.WaitOne();

                    lock (_rawMessagesBufferLock)
                    {
                        if (_rawMessagesBufferIndex == 0)
                            continue;

                        chunk = _rawMessagesBuffer[.._rawMessagesBufferIndex];
                        _rawMessagesBufferIndex = 0;

                        resultBufferIndex = ReadyToSendBuffer.ReserveBufferIndex();
                    }

                    if (chunk.Length == 0)
                        continue;

                    using var ms = new MemoryStream();

                    foreach (var message in chunk)
                    {
                        ms.Write(MessageConfig.CreateTcpHeader(message.MessageType, message.MessageClassId,
                            message.MessageId, message.TimerIndex));

                        if (message.Content != null)
                            ms.Write(message.Content.ToBytes());

                        ms.Write(MessageConfig.Delimiter);
                    }

                    ReadyToSendBuffer.Set(resultBufferIndex, ms.ToArray());
                    _rawMessagesBackpressure.Release(chunk.Length);
                }
            }
            catch (Exception e)
            {
                Console.WriteLine(e);
                throw;
            }
        }

        private async Task ShowWorkersHit()
        {
            try
            {
                while (true)
                {
                    await Task.Delay(1000);

                    Console.WriteLine(
                        $"[{SessionId}] Sent: {_messagesRpsCounter:#,##0.##}. Received: {_receivedRpsCounter:#,##0.##}");
                    Interlocked.Exchange(ref _messagesRpsCounter, 0);
                    Interlocked.Exchange(ref _receivedRpsCounter, 0);
                }
            }
            catch (Exception e)
            {
                Console.WriteLine(e);
                throw;
            }
        }
    }
}