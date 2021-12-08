using System.Collections;
using System.Collections.Generic;
using SimpleMessageBus.Buffers;

namespace SimpleMessageBus.Tests
{
    public class AckRangeNodeComparer : IComparer, IComparer<AckRangeNode>
    {
        public int Compare(AckRangeNode x, AckRangeNode y)
        {
            if (x == null || y == null) return -1;
            
            return x.First == y.First && x.Last == y.Last ? 0 : -1;
        }

        public int Compare(object x, object y)
        {
            return Compare(x as AckRangeNode, y as AckRangeNode);
        }
    }
}