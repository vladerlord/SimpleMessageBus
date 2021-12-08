using System.Collections.Generic;
using System.Linq;

namespace SimpleMessageBus.Buffers
{
    public class AckRangeNode
    {
        public int First;
        public int Last;

        public bool Contains(AckRangeNode node)
        {
            return node.First - First >= 0 && Last - node.First >= 0 && node.Last - First >= 0 &&
                   Last - node.Last >= 0;
        }

        public bool Contains((int First, int Last) range)
        {
            return range.First - First >= 0 && Last - range.First >= 0 && range.Last - First >= 0 &&
                   Last - range.Last >= 0;
        }

        public bool Contains(int number)
        {
            return number - First >= 0 && Last - number >= 0;
        }

        public bool ContainsOneOf(AckRangeNode node)
        {
            return node.First - First >= 0 && Last - node.First >= 0 || node.Last - First >= 0 &&
                Last - node.Last >= 0;
        }

        public bool Equals((int First, int Last) range)
        {
            return First == range.First && Last == range.Last;
        }
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

        public void Difference(List<AckRangeNode> nodes)
        {
            lock (_buffer)
            {
                var j = 0;

                while (j < nodes.Count)
                {
                    var ack = nodes[j];

#if (DEBUG_ACK_BUFFER)
                    Console.WriteLine($"== Ack loop: {ack.First}-{ack.Last}");
#endif
                    var shouldIncrementNodesIndex = true;

                    foreach (var node in _buffer)
                    {
#if DEBUG_ACK_BUFFER
                        Console.WriteLine($"n {node.First}-{node.Last} a {ack.First}-{ack.Last}");
#endif
                        // if node contains or equals ack range it means that he whole ack range is unacked
                        if (node.Equals(ack) || node.Contains(ack))
                        {
#if DEBUG_ACK_BUFFER
                            Console.WriteLine($"[0] - remove {ack.First}-{ack.Last} from result");
#endif
                            nodes.RemoveAt(j);

                            // if we remove item from nodes the index should not be changed
                            // because current index already equals next item in nodes
                            shouldIncrementNodesIndex = false;

                            break;
                        }

                        // node [119..1000] ack [0..2]
                        // node [3..4] ack [1..8]
                        if (!node.ContainsOneOf(ack) && !ack.ContainsOneOf(node)) continue;

                        // if there is left space. node [2..5] ack [0..3], 0..2 is left space
                        if (node.First - ack.First > 0)
                        {
                            // case 1: n 2..3, a 0..2, a will become 2..2 = broken range
                            // case 2: n 2..3 a 0..3, a will become 2..3 = equals to node
                            // case 3: n 2..5 a 0..3, a will become 2..3 = contained in node
                            if (ack.Last == node.First
                                || node.Equals((node.First, ack.Last))
                                || node.Contains((node.First, ack.Last))
                            )
                            {
                                // just change the current ack and stop
                                ack.Last = node.First;
#if DEBUG_ACK_BUFFER
                                Console.WriteLine($"[0] change a last to {node.First} and go next");
#endif
                                continue;
                            }

                            nodes.Add(new AckRangeNode { First = ack.First, Last = node.First });
#if DEBUG_ACK_BUFFER
                            Console.WriteLine($"[0] + add node: {ack.First}-{node.First}");
#endif
                            ack.First = node.First;

#if DEBUG_ACK_BUFFER
                            Console.WriteLine($"[0] change a first to {node.First}, a {ack.First}-{ack.Last}");
#endif
                        }

                        // if node contains first index the first index should be moved behind the node
                        // n [0..3] a [1..6] a will become [3..6]
                        if (node.Contains(ack.First))
                        {
                            ack.First = node.Last;

#if DEBUG_ACK_BUFFER
                            Console.WriteLine($"[1] change a first to {node.Last}");
#endif
                        }
                    }

                    if (shouldIncrementNodesIndex) j++;
                }
            }
        }

        public void Clear()
        {
            lock (_buffer)
                _buffer.Clear();
        }
    }
}