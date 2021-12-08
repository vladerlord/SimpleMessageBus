using System;
using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;
using SimpleMessageBus.Buffers;

namespace SimpleMessageBus.Tests
{
    public class AckRangesBufferTest
    {
        [Test]
        public void ServerAckManagerTest()
        {
            // Arrange
            var buffer = new AckRangesBuffer();

            var expected = new List<AckRangeNode>
            {
                new() { First = 0, Last = 1 },
                new() { First = 3, Last = 4 },
                new() { First = 5, Last = 9 }
            };

            buffer.AddUnAcked((0, 9));
            buffer.Ack((1, 3));
            buffer.Ack((4, 5));

            // Act
            var unacked = buffer.GetUnAcked();

            foreach (var ackRangeNode in unacked)
                Console.WriteLine($"{ackRangeNode.First}-{ackRangeNode.Last}");

            // Assert
            CollectionAssert.AreEqual(expected, unacked.OrderBy(foo => foo.Last), new AckRangeNodeComparer());
        }
    }
}