using System;
using System.Threading;

namespace SimpleMessageBus.Buffers
{
    /// <summary>
    /// Multi threaded buffer with max 1 producer and 1 consumer 
    /// </summary>
    /// <typeparam name="T"></typeparam>
    public class BoundedSingleThreadBlockingArrayBuffer<T>
    {
        private readonly int _bufferSize;
        private readonly SemaphoreSlim _overflowControl;
        private readonly SemaphoreSlim _underFlowControl = new(0);
        private readonly Memory<T> _buffer;
        private int _bufferIndex = 0;
        private readonly object _bufferLock = new();
        private readonly AutoResetEvent _autoReset = new(false);

        public BoundedSingleThreadBlockingArrayBuffer(int bufferSize)
        {
            _bufferSize = bufferSize;
            _overflowControl = new SemaphoreSlim(_bufferSize);
            _buffer = new Memory<T>(new T[_bufferSize]);
        }

        public void Add(T data)
        {
            _overflowControl.Wait();

            lock (_bufferLock)
                _buffer.Span[_bufferIndex++] = data;

            _autoReset.Set();
        }

        public Span<T> TakeMaxItems()
        {
            _autoReset.WaitOne();

            Span<T> result;

            lock (_bufferLock)
            {
                result = _buffer.Span[.._bufferIndex];
                _bufferIndex = 0;
            }

            _overflowControl.Release(result.Length);

            return result;
        }
    }
}