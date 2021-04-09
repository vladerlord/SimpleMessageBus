using System;
using System.Threading;

namespace SimpleMessageBus.Buffers
{
    public class CircularBuffer<T>
    {
        private const int BufferSize = 1000;
        private readonly Memory<T> _buffer = new(new T[BufferSize]);
        private readonly bool[] _inUse = new bool[BufferSize];
        private readonly bool[] _ack = new bool[BufferSize];
        private readonly bool[] _hasData = new bool[BufferSize];
        private readonly SemaphoreSlim _bufferOverflowControl = new(BufferSize);
        private readonly SemaphoreSlim _bufferUnderflowControl = new(0);

        private int _tailIndex;
        private int _headIndex = -1;
        private readonly object _indexesLock = new();

        public CircularBuffer()
        {
            for (var i = 0; i < _ack.Length; i++)
                _ack[i] = true;
        }

        public void Add(T data)
        {
            _bufferOverflowControl.Wait();

            lock (_indexesLock)
            {
                _headIndex = ++_headIndex == BufferSize ? 0 : _headIndex;
                _buffer.Span[_headIndex] = data;
                _hasData[_headIndex] = true;
            }

            _bufferUnderflowControl.Release();
        }

        public Span<T> TakeMaxItems(out Range range)
        {
            var result = Span<T>.Empty;
            range = Range.All;

            do
            {
                _bufferUnderflowControl.Wait();

                var elementsToReturn = 0;

                lock (_indexesLock)
                {
                    for (var i = _tailIndex; i < BufferSize; i++)
                    {
                        if (!_hasData[i])
                            break;

                        if (!_ack[i])
                            break;

                        _ack[i] = false;
                        _inUse[i] = true;
                        elementsToReturn++;
                    }

                    if (elementsToReturn == 0)
                        continue;

                    range = _tailIndex..(_tailIndex + elementsToReturn);
                    result = _buffer.Span[range];

                    _tailIndex = _tailIndex + elementsToReturn == BufferSize ? 0 : _tailIndex + elementsToReturn;

                    // rule 1
                    var elementsToRelease = 0;

                    for (var i = _tailIndex; i < BufferSize; i++)
                    {
                        if (_inUse[i] || _ack[i])
                            break;

                        _ack[i] = true;
                        elementsToRelease++;
                    }

                    if (elementsToRelease == 0)
                        continue;

                    _bufferOverflowControl.Release(elementsToRelease);
                }
            } while (result.IsEmpty);

            return result;
        }

        public void AcknowledgeRange(Range range)
        {
            lock (_indexesLock)
            {
                var shouldRelease = range.Start.Value == _tailIndex;
                var elementsToRelease = 0;

                for (var i = range.Start.Value; i < BufferSize; i++)
                {
                    if (i < range.End.Value)
                    {
                        _inUse[i] = false;
                        _hasData[i] = false;

                        if (!shouldRelease)
                            continue;

                        _ack[i] = true;
                        elementsToRelease++;

                        continue;
                    }

                    if (_inUse[i] || _ack[i])
                        break;

                    if (!shouldRelease)
                        break;

                    _ack[i] = true;
                    elementsToRelease++;
                }

                if (shouldRelease && elementsToRelease > 0)
                    _bufferOverflowControl.Release(elementsToRelease);
            }

            _bufferUnderflowControl.Release(2);
        }
    }
}