using System;
using System.Collections.Generic;
using System.Threading;

namespace SimpleMessageBus.Buffers
{
    /// <summary>
    /// Multi-threaded buffer with maximum N producers and 1 consumer
    /// </summary>
    /// <typeparam name="T"></typeparam>
    public class BoundedAllocatingBlockingBuffer<T>
    {
        private readonly int _bufferSize;
        private readonly Memory<T> _buffer;
        private int _bufferIndex;
        private readonly object _bufferLock = new();

        public BoundedAllocatingBlockingBuffer(int bufferSize)
        {
            _bufferSize = bufferSize;
            _buffer = new Memory<T>(new T[_bufferSize]);
        }

        public void Add(T data)
        {
            SpinWait.SpinUntil(() => _bufferIndex < _bufferSize);

            lock (_bufferLock)
                _buffer.Span[_bufferIndex++] = data;
        }

        public IEnumerable<T> TakeMaxItems()
        {
            T[] result;
            SpinWait.SpinUntil(() => _bufferIndex > 0);

            lock (_bufferLock)
            {
                result = _buffer.Span[.._bufferIndex].ToArray();
                _bufferIndex = 0;
            }

            return result;
        }
    }
}