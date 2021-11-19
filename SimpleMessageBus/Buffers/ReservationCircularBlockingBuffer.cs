using System;
using System.Collections.Generic;
using System.Threading;

namespace SimpleMessageBus.Buffers
{
    /// <summary>
    /// Bounded circular buffer with maximum N publishers and 1 consumer that 
    /// can help to achieve ordered results in multithreading environment.
    /// The idea is that first you reserve index for result then you make some processing
    /// and in the end you inject result by reserved index
    /// Take method cannot return result of unset indexes it'll wait for the first index
    /// to be set
    /// 
    /// </summary>
    /// <typeparam name="T"></typeparam>
    public class ReservationCircularBlockingBuffer<T>
    {
        private readonly int _bufferSize;
        private readonly Memory<T> _buffer;
        private readonly bool[] _bufferReadinessState;

        private readonly SemaphoreSlim _bufferOverflowControl;
        private readonly AutoResetEvent _bufferUnderflowControl = new(false);

        private int _leftIndex;
        private int _reservedSpaceIndex;
        private readonly object _indexesLock = new();
        private readonly List<int> _reservedIndexes;

        public ReservationCircularBlockingBuffer(int bufferSize)
        {
            _bufferSize = bufferSize;
            _buffer = new Memory<T>(new T[_bufferSize]);
            _bufferReadinessState = new bool[_bufferSize];
            _bufferOverflowControl = new SemaphoreSlim(_bufferSize);
            _reservedIndexes = new List<int>(_bufferSize);
        }

        /// <summary>
        /// If there are N consumers this method should be called under the shared lock
        /// Task1 called ReserveBufferIndex first
        /// Task2 called ReserveBufferIndex second
        /// but _bufferOverflowControl.Wait() can let Task2 to be the first to enter the method
        /// this may be fixed by one more lock over .wait()
        /// </summary>
        /// <returns></returns>
        public int ReserveBufferIndex()
        {
            _bufferOverflowControl.Wait();

            lock (_indexesLock)
            {
                var reservedIndex = _reservedSpaceIndex;

                _reservedSpaceIndex = (_reservedSpaceIndex + 1) % _bufferSize;
                _reservedIndexes.Add(reservedIndex);

                return reservedIndex;
            }
        }

        public void Set(int index, T data)
        {
            _buffer.Span[index] = data;
            _bufferReadinessState[index] = true;

            lock (_indexesLock)
            {
                // release only when we hit the first reserved index
                // (0, 1, 2) - reserved
                // if we receive 1 in this method there is no need to fire Take method
                // as it'll try to take 0 and this index is not set
                if (index == _reservedIndexes[0])
                    _bufferUnderflowControl.Set();

                _reservedIndexes.Remove(index);
            }
        }

        public Memory<T> TakeMaxReadyElements()
        {
            var result = Memory<T>.Empty;

            do
            {
                _bufferUnderflowControl.WaitOne();

                var elementsAmount = 0;
                var rightIndex = _leftIndex == _reservedSpaceIndex || _leftIndex > _reservedSpaceIndex
                    ? _bufferSize
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

                if (_leftIndex >= _bufferSize)
                    _leftIndex = 0;
            } while (result.IsEmpty);

            return result;
        }

        public void Release(int amount)
        {
            _bufferOverflowControl.Release(amount);
        }
    }
}