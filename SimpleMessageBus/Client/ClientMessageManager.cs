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
    internal class MessageNode<T>
    {
        public T Content;
        public MessageType MessageType;
    }

    public class ClientMessageManager : IDisposable
    {
        private const int SerializationWorkersAmount = 3;

        private const int UnserializedMessagesBufferSize = 1200;
        private readonly SemaphoreSlim _unserializedMessagesBackpressure = new(UnserializedMessagesBufferSize);
        private readonly Memory<MessageNode<IMessage>> _unserializedMessages;
        private readonly object _unserializedMessagesLock = new();
        private int _unserializedMessagesIndex;

        private const int RawMessagesBufferSize = 20000;
        private readonly SemaphoreSlim _rawMessagesBackpressure = new(RawMessagesBufferSize);
        private readonly Memory<MessageNode<string>> _rawMessagesBuffer;
        private readonly object _rawMessagesBufferLock = new();
        private int _rawMessagesBufferIndex;

        public CircularArrayBuffer<byte> ReadyToSendBuffer { get; } = new();
        private readonly ShardedCounter _shardedCounter = new();
        private int _rawMessagesRpsCounter;

        public ClientMessageManager()
        {
            _unserializedMessages =
                new Memory<MessageNode<IMessage>>(new MessageNode<IMessage>[UnserializedMessagesBufferSize]);
            _rawMessagesBuffer = new Memory<MessageNode<string>>(new MessageNode<string>[RawMessagesBufferSize]);

            for (var i = 0; i < UnserializedMessagesBufferSize; i++)
                _unserializedMessages.Span[i] = new MessageNode<IMessage>();

            for (var i = 0; i < RawMessagesBufferSize; i++)
                _rawMessagesBuffer.Span[i] = new MessageNode<string>();
        }

        public Task Start()
        {
            var tasks = new List<Task>();

            for (var i = 0; i < SerializationWorkersAmount; i++)
            {
                var k = i;
                tasks.Add(Task.Run(() => SerializationWorker(k)));
            }

            tasks.Add(Task.Run(ShowWorkersHit));
            tasks.Add(Task.Run(RawTcpMessagesWorker));

            return Task.WhenAny(tasks);
        }

        public void AddMessage(IMessage message)
        {
            _unserializedMessagesBackpressure.Wait();

            lock (_unserializedMessagesLock)
            {
                _unserializedMessages.Span[_unserializedMessagesIndex].Content = message;
                _unserializedMessagesIndex++;
            }
        }

        public void AddRawMessage(MessageType messageType, string content)
        {
            _rawMessagesBackpressure.Wait();

            lock (_rawMessagesBufferLock)
            {
                _rawMessagesBuffer.Span[_rawMessagesBufferIndex].Content = content;
                _rawMessagesBuffer.Span[_rawMessagesBufferIndex].MessageType = messageType;
                _rawMessagesBufferIndex++;
            }
        }

        private void SerializationWorker(int worker)
        {
            try
            {
                while (true)
                {
                    Span<MessageNode<IMessage>> chunk;

                    SpinWait.SpinUntil(() => _unserializedMessagesIndex > 0);

                    int resultBufferIndex;

                    lock (_unserializedMessagesLock)
                    {
                        if (_unserializedMessagesIndex == 0)
                            continue;

                        chunk = new Span<MessageNode<IMessage>>(new MessageNode<IMessage>[_unserializedMessagesIndex]);

                        _unserializedMessages.Span[.._unserializedMessagesIndex].CopyTo(chunk);
                        _unserializedMessagesIndex = 0;

                        resultBufferIndex = ReadyToSendBuffer.ReserveBufferIndex();
                    }

                    if (chunk.Length == 0)
                        continue;

                    using var ms = new MemoryStream();

                    foreach (var message in chunk)
                    {
                        ms.Write(MessageConfig.CreateTcpHeader(MessageType.Message, 0));
                        Serializer.Serialize(ms, message.Content);
                        ms.Write(MessageConfig.Delimiter);

                        _shardedCounter.Increase(1);
                    }

                    ReadyToSendBuffer.Set(resultBufferIndex, ms.GetBuffer()[..(int) ms.Position]);
                    _unserializedMessagesBackpressure.Release(chunk.Length);
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
                    Span<MessageNode<string>> chunk;

                    SpinWait.SpinUntil(() => _rawMessagesBufferIndex > 0);

                    int resultBufferIndex;

                    lock (_rawMessagesBufferLock)
                    {
                        if (_rawMessagesBufferIndex == 0)
                            continue;

                        chunk = new Span<MessageNode<string>>(new MessageNode<string>[_rawMessagesBufferIndex]);

                        _rawMessagesBuffer.Span[.._rawMessagesBufferIndex].CopyTo(chunk);
                        _rawMessagesBufferIndex = 0;

                        resultBufferIndex = ReadyToSendBuffer.ReserveBufferIndex();
                    }

                    if (chunk.Length == 0)
                        continue;

                    using var ms = new MemoryStream();

                    foreach (var message in chunk)
                    {
                        ms.Write(MessageConfig.CreateTcpHeader(message.MessageType, 0));
                        ms.Write(message.Content.ToBytes());
                        ms.Write(MessageConfig.Delimiter);

                        Interlocked.Increment(ref _rawMessagesRpsCounter);
                    }

                    ReadyToSendBuffer.Set(resultBufferIndex, ms.GetBuffer()[..(int) ms.Position]);
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

                    Console.WriteLine($"RPS: {_shardedCounter.Count}");
                    Console.WriteLine($"RPS raw: {_rawMessagesRpsCounter}");
                    _shardedCounter.Decrease(_shardedCounter.Count);
                    Interlocked.Exchange(ref _rawMessagesRpsCounter, 0);
                }
            }
            catch (Exception e)
            {
                Console.WriteLine(e);
                throw;
            }
        }

        public void Dispose()
        {
            _unserializedMessagesBackpressure?.Dispose();
            _rawMessagesBackpressure?.Dispose();
        }
    }
}