using System;
using System.Threading;
using SimpleMessageBus.Buffers;

namespace SimpleMessageBus.Server
{
    /// <summary>
    /// N producers and 1 consumer
    /// </summary>
    public class ServerMessagesBuffer
    {
        private readonly int _bufferSize;
        private readonly Memory<byte[]> _buffer;

        private readonly object _bufferLock = new();

        // index can be _inUse = false && _ack = false
        // it means that we got ack for index but there
        // are un-acked indexes before the index and we should wait for them
        private readonly bool[] _inUse;
        private readonly bool[] _ack;
        private readonly bool[] _hasData;

        private int _headIndex = -1;
        private int _tailIndex;
        private int _acknowledgeTailIndex;

        private int _bufferItemsAmount;

        public ServerMessagesBuffer(int bufferSize)
        {
            _bufferSize = bufferSize;

            _buffer = new Memory<byte[]>(new byte[_bufferSize][]);
            _inUse = new bool[_bufferSize];
            _ack = new bool[_bufferSize];
            _hasData = new bool[_bufferSize];

            for (var i = 0; i < _ack.Length; i++)
                _ack[i] = true;
        }

        public void Add(byte[] item)
        {
            // todo, decide how not to lose messages
            // ack on client side?
            // additional endless buffer?
            if (!SpinWait.SpinUntil(() => _bufferItemsAmount < _bufferSize, 1_000))
                return;

            lock (_bufferLock)
            {
                _headIndex = (_headIndex + 1) % _bufferSize;
                _buffer.Span[_headIndex] = item;
                _hasData[_headIndex] = true;
                _bufferItemsAmount++;

                item.InjectMessageId(_headIndex);
            }
        }

        public Memory<byte[]> TakeByRangeNode(AckRangeNode rangeNode)
        {
            return _buffer[rangeNode.First..rangeNode.Last];
        }

        public int GetLength()
        {
            var counter = 0;

            lock (_bufferLock)
            {
                for (var i = _tailIndex; i < _bufferSize; i++)
                {
                    if (!_hasData[i])
                        break;

                    if (!_ack[i])
                        break;

                    counter++;
                }
            }

            return counter;
        }

        public Memory<byte[]> TakeMax(out (int left, int right) range)
        {
            var result = Memory<byte[]>.Empty;
            var elementsToReturn = 0;
            range.left = range.right = 0;

            lock (_bufferLock)
            {
                if (_bufferItemsAmount == 0)
                    return result;

                for (var i = _tailIndex; i < _bufferSize; i++)
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
                    return result;

                result = _buffer.Slice(_tailIndex, elementsToReturn);
                range.left = _tailIndex;
                range.right = _tailIndex + elementsToReturn;
                _tailIndex = (_tailIndex + elementsToReturn) % _bufferSize;

                // try to release some items in case they are not in use anymore
                var elementsToRelease = 0;

                for (var i = _acknowledgeTailIndex; i < _bufferSize; i++)
                {
                    if (_inUse[i] || _ack[i])
                        break;

                    _ack[i] = true;
                    elementsToRelease++;
                }

                if (elementsToRelease == 0)
                    return result;

                _bufferItemsAmount -= elementsToRelease;
                _acknowledgeTailIndex = (_acknowledgeTailIndex + elementsToRelease) % _bufferSize;
            }

            return result;
        }

        public void Acknowledge(AckRangeNode range)
        {
            lock (_bufferLock)
            {
                // release only when there is no space for new items
                var shouldRelease = range.First == _acknowledgeTailIndex;
                var elementsToRelease = 0;

                for (var i = range.First; i < _bufferSize; i++)
                {
                    if (range.First <= i && i < range.Last)
                    {
                        _inUse[i] = false;
                        _hasData[i] = false;

                        if (!shouldRelease)
                            continue;

                        _ack[i] = true;
                        elementsToRelease++;

                        continue;
                    }

                    if (_inUse[i] || _ack[i]) break;
                    if (!shouldRelease) break;

                    _ack[i] = true;
                    elementsToRelease++;
                }

                if (!shouldRelease || elementsToRelease <= 0) return;

                _bufferItemsAmount -= elementsToRelease;
                _acknowledgeTailIndex = (_acknowledgeTailIndex + elementsToRelease) % _bufferSize;
            }
        }
    }
}