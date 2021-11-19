using System.Collections.Generic;
using System.Linq;

namespace SimpleMessageBus.Buffers
{
    public class AckRangeNode
    {
        public int First;
        public int Last;
    }

    public class AckRangesBuffer
    {
        private readonly List<AckRangeNode> _buffer = new();

        public void AddUnAcked((int first, int second) range)
        {
            lock (_buffer)
            {
                if (_buffer.Count == 0 || _buffer.Last().Last != range.first)
                {
                    _buffer.Add(new AckRangeNode
                    {
                        First = range.first,
                        Last = range.second
                    });

                    return;
                }

                var lastNode = _buffer.Last();

                lastNode.Last = range.second;
            }
        }

        public void Ack((int first, int last) range)
        {
            lock (_buffer)
            {
                for (var i = 0; i < _buffer.Count; i++)
                {
                    var node = _buffer[i];

                    // 0-5, ack 1-4, (0-1),(4-5)
                    if (node.First < range.first && node.Last > range.last)
                    {
                        _buffer.Insert(i + 1, new AckRangeNode
                        {
                            First = range.last,
                            Last = node.Last
                        });
                        node.Last = range.first;

                        break;
                    }

                    // 0-5, ack 0-4, (4-5)
                    if (node.First == range.first && node.Last > range.last)
                    {
                        node.First = range.last;

                        break;
                    }

                    // 0-5, ack 3-5, (0-3)
                    if (node.First < range.first && node.Last == range.last)
                    {
                        node.Last = range.first;

                        break;
                    }

                    // 0-5, ack 0-5
                    if (node.First == range.first && node.Last == range.last)
                    {
                        _buffer.RemoveAt(i);

                        break;
                    }
                }
            }
        }

        public List<AckRangeNode> GetUnAcked()
        {
            lock (_buffer)
                return new List<AckRangeNode>(_buffer);
        }

        public void Clear()
        {
            lock (_buffer)
                _buffer.Clear();
        }
    }
}