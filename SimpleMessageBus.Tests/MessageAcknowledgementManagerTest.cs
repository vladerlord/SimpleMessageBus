using System;
using System.Collections.Generic;
using NUnit.Framework;
using SimpleMessageBus.Buffers;
using SimpleMessageBus.Client;

namespace SimpleMessageBus.Tests
{
    public class MessageAcknowledgementManagerTest
    {
        [Test, TestCaseSource(nameof(TestGroupMessagesIdsIntoRangesFixtures))]
        public void GroupMessagesIdsIntoRangesTest(List<int> messageIds, List<AckRangeNode> expected)
        {
            // Arrange
            var clientMessageManager = new ClientMessageManager(1);
            var ackManager = new MessageAcknowledgementManager(clientMessageManager);

            ackManager.AddMessageClassId(1);

            foreach (var messageId in messageIds)
                ackManager.AcknowledgeMessage(1, messageId, 0);

            ackManager.GroupMessagesIdsIntoRanges();

            // Act
            var result = ackManager.GetAckRanges(1, 0);

            foreach (var ackRangeNode in result)
                Console.WriteLine($"{ackRangeNode.First}-{ackRangeNode.Last}");

            // Assert
            CollectionAssert.AreEqual(expected, result, new AckRangeNodeComparer());
        }

        private static IEnumerable<TestCaseData> TestGroupMessagesIdsIntoRangesFixtures()
        {
            // message ids to be acked
            // expected grouped nodes

            yield return new TestCaseData(
                new List<int> { 0, 1 },
                new List<AckRangeNode>
                {
                    new() { First = 0, Last = 2 }
                }
            ).SetName("ordered");
            yield return new TestCaseData(
                new List<int> { 5, 6, 7, 0, 1, 2 },
                new List<AckRangeNode>
                {
                    new() { First = 5, Last = 8 },
                    new() { First = 0, Last = 3 },
                }
            ).SetName("circular; ordered");
            yield return new TestCaseData(
                new List<int> { 5, 7, 0, 2 },
                new List<AckRangeNode>
                {
                    new() { First = 5, Last = 6 },
                    new() { First = 7, Last = 8 },
                    new() { First = 0, Last = 1 },
                    new() { First = 2, Last = 3 },
                }
            ).SetName("circular; unordered");
        }
    }
}