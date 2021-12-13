using System;
using System.Collections.Generic;
using NUnit.Framework;
using SimpleMessageBus.Abstractions;
using SimpleMessageBus.Buffers;
using SimpleMessageBus.Client;

namespace SimpleMessageBus.Tests
{
    internal class MessagesBinding : IMessagesIdsBinding
    {
        public Dictionary<Type, ushort> GetMessagesIdsBinding()
        {
            return new Dictionary<Type, ushort> { { typeof(MessagesBinding), 1 } };
        }

        public ushort GetMessageIdByType(Type type)
        {
            return 1;
        }

        public List<ushort> GetMessagesIds()
        {
            return new List<ushort> { 1 };
        }
    }

    public class MessageAcknowledgementManagerTest
    {
        [Test, TestCaseSource(nameof(TestGroupMessagesIdsIntoRangesFixtures))]
        public void GroupMessagesIdsIntoRangesTest(List<int> messageIds, List<AckRangeNode> expected)
        {
            // Arrange
            var messagesIdsBinding = new MessagesBinding();
            var clientMessageManager = new ClientMessageManager(messagesIdsBinding);
            var ackManager = new MessageAcknowledgementManager(clientMessageManager, messagesIdsBinding);

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