using System;
using System.Threading;

namespace SimpleMessageBus
{
    public class CircularByteBuffer<T>
    {
        private const int BufferSize = 1000;
        private readonly Memory<T[]> _buffer = new(new T[BufferSize][]);
        private readonly bool[] _bufferReadinessState = new bool[BufferSize];
        private readonly SemaphoreSlim _bufferOverflowControl = new(BufferSize);
        private readonly SemaphoreSlim _bufferUnderflowControl = new(0);

        private int _leftIndex;
        private int _reservedSpaceIndex;
        private readonly object _indexesLock = new();

        public int ReserveBufferIndex()
        {
            _bufferOverflowControl.Wait();

            lock (_indexesLock)
            {
                var reservedIndex = _reservedSpaceIndex;

                if (_reservedSpaceIndex + 1 == BufferSize)
                    _reservedSpaceIndex = 0;
                else
                    _reservedSpaceIndex++;

                return reservedIndex;
            }
        }

        public void Set(int index, T[] data)
        {
            _buffer.Span[index] = data;
            _bufferReadinessState[index] = true;

            _bufferUnderflowControl.Release(1);
        }

        public Memory<T[]> TakeMaxReadyElements()
        {
            var result = Memory<T[]>.Empty;

            do
            {
                _bufferUnderflowControl.Wait();

                var elementsAmount = 0;

                var rightIndex = _leftIndex == _reservedSpaceIndex || _leftIndex > _reservedSpaceIndex
                    ? BufferSize
                    : _reservedSpaceIndex;

                for (var i = _leftIndex; i < rightIndex; i++)
                {
                    if (_bufferReadinessState[i] == false)
                        break;

                    _bufferReadinessState[i] = false;
                    elementsAmount++;
                }

                if (elementsAmount == 0)
                    continue;

                result = _buffer.Slice(_leftIndex, elementsAmount);
                _leftIndex += elementsAmount;

                if (_leftIndex >= BufferSize)
                    _leftIndex = 0;

                _bufferOverflowControl.Release(elementsAmount);
            } while (result.IsEmpty);

            return result;
        }
    }
}