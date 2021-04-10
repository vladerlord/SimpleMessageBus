using System;
using System.Threading;

namespace SimpleMessageBus.Server
{
    public class ServerMessageManager
    {
        private static ServerMessageManager _instance;

        private const int MessagesBufferSize = 1000;

        // channel => message
        private readonly Memory<MessageNode> _buffer = new(new MessageNode[MessagesBufferSize]);
        private readonly object _bufferLock = new();
        private readonly SemaphoreSlim _bufferOverflow = new(MessagesBufferSize);
        private int _bufferHead = 0;

        private ServerMessageManager()
        {
            for (var i = 0; i < MessagesBufferSize; i++)
                _buffer.Span[i] = new MessageNode();
        }

        public static ServerMessageManager GetInstance()
        {
            return _instance ??= new ServerMessageManager();
        }

        public void AddMessage(byte[] content, ushort messageClass)
        {
            _bufferOverflow.Wait();

            lock (_bufferLock)
            {
                if (++_bufferHead == MessagesBufferSize)
                    _bufferHead = 0;

                _buffer.Span[_bufferHead].Content = content;
                _buffer.Span[_bufferHead].MessageClass = messageClass;
            }
        }
    }
}