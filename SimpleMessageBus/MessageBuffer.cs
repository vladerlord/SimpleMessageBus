using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using ProtoBuf;

namespace SimpleMessageBus
{
    public class MessageBuffer
    {
        private const int SerializationWorkersAmount = 3;

        private const int UnserializedMessagesBufferSize = 1000;
        private readonly SemaphoreSlim _unserializedMessagesBackpressure = new(UnserializedMessagesBufferSize);
        private readonly Memory<IMessage> _unserializedMessages = new(new IMessage[UnserializedMessagesBufferSize]);
        private readonly object _unserializedMessagesLock = new();
        private int _unserializedMessagesIndex;

        public CircularByteBuffer<byte> ReadyToSendBuffer { get; } = new();

        private readonly ShardedCounter _shardedCounter = new();

        public Task Start()
        {
            var tasks = new List<Task>();

            for (var i = 0; i < SerializationWorkersAmount; i++)
            {
                var k = i;
                tasks.Add(Task.Factory.StartNew(() => SerializationWorker(k), TaskCreationOptions.LongRunning));
            }

            tasks.Add(Task.Run(ShowWorkersHit));

            return Task.WhenAny(tasks);
        }

        public void AddMessage(IMessage message)
        {
            _unserializedMessagesBackpressure.Wait();

            lock (_unserializedMessagesLock)
                _unserializedMessages.Span[_unserializedMessagesIndex++] = message;
        }

        private void SerializationWorker(int worker)
        {
            try
            {
                while (true)
                {
                    Span<IMessage> toSerialize;

                    while (_unserializedMessagesIndex == 0)
                    {
                    }

                    int resultBufferIndex;

                    lock (_unserializedMessagesLock)
                    {
                        if (_unserializedMessagesIndex == 0)
                            continue;

                        toSerialize = _unserializedMessages.Span[.._unserializedMessagesIndex];
                        _unserializedMessagesIndex = 0;

                        resultBufferIndex = ReadyToSendBuffer.ReserveBufferIndex();
                    }

                    if (toSerialize.Length == 0)
                        continue;

                    using var ms = new MemoryStream();

                    foreach (var message in toSerialize)
                    {
                        ms.Write(Encoding.ASCII.GetBytes(new[] {(char) MessageType.Message}));
                        Serializer.Serialize(ms, message);
                        ms.WriteByte(MessageConfig.Delimiter);
                        _shardedCounter.Increase(1);
                    }

                    ReadyToSendBuffer.Set(resultBufferIndex, ms.GetBuffer()[..(int) ms.Position]);
                    _unserializedMessagesBackpressure.Release(toSerialize.Length);
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
                    _shardedCounter.Decrease(_shardedCounter.Count);
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