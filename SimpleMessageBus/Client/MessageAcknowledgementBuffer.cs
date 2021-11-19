using System;
using System.Threading;

namespace SimpleMessageBus.Client
{
    // todo, move out
    public struct Node
    {
        public ushort MessageClassId;
        public int MessageId;
        public ushort TimerIndex;
    }

    public class MessageAcknowledgementBuffer
    {
        private readonly int _bufferSize;
        private readonly Memory<Node> _buffer;
        private int _bufferHeadIndex = -1;
        private int _bufferTailIndex;
        private int _bufferItemsAmount;
        private readonly object _bufferLock = new();

        public MessageAcknowledgementBuffer(int bufferSize)
        {
            _bufferSize = bufferSize;
            _buffer = new Node[_bufferSize];
        }

        public void Add(ushort messageClassId, int messageId, ushort timerIndex)
        {
            SpinWait.SpinUntil(() => _bufferItemsAmount < _bufferSize);

            lock (_bufferLock)
            {
                _bufferHeadIndex = _bufferHeadIndex + 1 == _bufferSize ? 0 : _bufferHeadIndex + 1;
                _buffer.Span[_bufferHeadIndex].MessageClassId = messageClassId;
                _buffer.Span[_bufferHeadIndex].MessageId = messageId;
                _buffer.Span[_bufferHeadIndex].TimerIndex = timerIndex;
                _bufferItemsAmount++;
            }
        }

        public Memory<Node> TakeMax()
        {
            SpinWait.SpinUntil(() => _bufferItemsAmount > 0);
            Memory<Node> result;

            lock (_bufferLock)
            {
                result = _bufferHeadIndex < _bufferTailIndex
                    ? _buffer[_bufferTailIndex..]
                    : _buffer[_bufferTailIndex..(_bufferHeadIndex + 1)];
            }

            return result;
        }

        public void AckAmount(int amountOfConsumedItems)
        {
            lock (_bufferLock)
            {
                _bufferItemsAmount -= amountOfConsumedItems;
                _bufferTailIndex = (_bufferTailIndex + amountOfConsumedItems) % _bufferSize;
            }
        }
    }
}