using System;
using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;
using SimpleMessageBus.Buffers;
using SimpleMessageBus.Server;

namespace SimpleMessageBus.Tests
{
    public class UnackedNode
    {
        public (int, int) Range;
        public List<int> SessionIds;
    }

    public class Tests
    {
        [Test, TestCaseSource(nameof(TestAcknowledgementFixtures))]
        public void TestAcknowledgement(
            int sessions,
            List<AckRangeNode> expected,
            (int, int) unackedRange,
            List<int> unackedSessionIds,
            Dictionary<int, List<(int, int)>> acked
        )
        {
            // Arrange
            var ackManager = GetManager(sessions);
            var byteMessages = new Memory<byte[]>(new[] { new byte[9] });

            ackManager.AddUnAcked(byteMessages, unackedRange, 1, unackedSessionIds);

            foreach (var (sessionId, ranges) in acked)
            foreach (var range in ranges)
                ackManager.Ack(range, 1, sessionId, 0);

            var lastSession = acked.Last();
            var lastSessionLastRange = lastSession.Value.Last();

            // Act
            var result = ackManager.GetReadyForReleaseNodes(lastSessionLastRange, 1, lastSession.Key, 0);

            foreach (var ackRangeNode in result)
                Console.WriteLine($"{ackRangeNode.First}-{ackRangeNode.Last}");

            // Assert
            CollectionAssert.AreEqual(expected, result.OrderBy(foo => foo.Last), new AckRangeNodeComparer());
        }

        [Test, TestCaseSource(nameof(TestUndeliveredFixtures))]
        public void TestUndelivered(
            int sessions,
            List<AckRangeNode> expectedForRelease,
            Dictionary<int, List<AckRangeNode>> expectedUndelivered,
            (int, int) unackedRange,
            List<int> unackedSessionIds,
            Dictionary<int, List<(int, int)>> ackedBeforeTimeout,
            Dictionary<int, List<(int, int)>> ackedAfterTimeout
        )
        {
            // Arrange
            var ackManager = GetManager(sessions);
            var byteMessages = new Memory<byte[]>(new[] { new byte[9] });

            ackManager.AddUnAcked(byteMessages, unackedRange, 1, unackedSessionIds);

            foreach (var (sessionId, ranges) in ackedBeforeTimeout)
            foreach (var range in ranges)
            {
                Console.WriteLine($"Ack [0]. Session: {sessionId}. Range: {range}");
                ackManager.Ack(range, 1, sessionId, 0);
            }

            for (var i = 0; i < ServerConfig.MessageAckTimeout; i++)
                ackManager.TimerTick();

            foreach (var (sessionId, ranges) in ackedAfterTimeout)
            foreach (var range in ranges)
            {
                Console.WriteLine($"Ack [1]. Session: {sessionId}. Range: {range}");
                ackManager.Ack(range, 1, sessionId, 0);
            }

            var lastSession = ackedAfterTimeout.Last();
            var lastSessionLastRange = lastSession.Value.Last();

            // Act
            var readyForRelease = ackManager.GetReadyForReleaseNodes(lastSessionLastRange, 1, lastSession.Key, 0);

            foreach (var ackRangeNode in readyForRelease)
                Console.WriteLine($"{ackRangeNode.First}-{ackRangeNode.Last}");

            // Assert
            var comparer = new AckRangeNodeComparer();

            CollectionAssert.AreEqual(expectedForRelease, readyForRelease.OrderBy(foo => foo.Last), comparer);

            foreach (var (sessionId, buffer) in ackManager.GetUndeliveredIds(1))
            {
                var undelivered = buffer.GetUnAcked();

                foreach (var ackRangeNode in undelivered)
                    Console.WriteLine($"Undelivered session {sessionId}: {ackRangeNode.First}-{ackRangeNode.Last}");

                CollectionAssert.AreEqual(expectedUndelivered[sessionId], undelivered.OrderBy(foo => foo.Last),
                    comparer);
            }
        }

        [Test, TestCaseSource(nameof(TestCircularFixtures))]
        public void TestCircular(
            int sessions,
            List<AckRangeNode> expected,
            List<UnackedNode> unacked,
            Dictionary<int, List<(int, int)>> acked
        )
        {
            // Arrange
            var ackManager = GetManager(sessions);
            var byteMessages = new Memory<byte[]>(new[] { new byte[9] });

            foreach (var unackedNode in unacked)
            {
                Console.WriteLine($"Add unacked: {unackedNode.Range}");
                ackManager.AddUnAcked(byteMessages, unackedNode.Range, 1, unackedNode.SessionIds);
            }

            foreach (var (sessionId, ranges) in acked)
            foreach (var range in ranges)
            {
                Console.WriteLine($"Ack {range}, session {sessionId}");
                ackManager.Ack(range, 1, sessionId, 0);
            }

            var lastSession = acked.Last();
            var lastSessionLastRange = lastSession.Value.Last();

            // Act
            var result = ackManager.GetReadyForReleaseNodes(lastSessionLastRange, 1, lastSession.Key, 0);

            foreach (var ackRangeNode in result)
                Console.WriteLine($"{ackRangeNode.First}-{ackRangeNode.Last}");

            // Assert
            CollectionAssert.AreEqual(expected, result.OrderBy(foo => foo.Last), new AckRangeNodeComparer());
        }

        private static IEnumerable<TestCaseData> TestCircularFixtures()
        {
            yield return new TestCaseData(
                    2,
                    new List<AckRangeNode>
                    {
                        new() { First = 1, Last = 3 },
                        new() { First = 5, Last = 6 },
                    },
                    new List<UnackedNode>
                    {
                        new() { Range = (5, 9), SessionIds = new List<int> { 0, 1 } },
                        new() { Range = (0, 5), SessionIds = new List<int> { 0, 1 } },
                    },
                    new Dictionary<int, List<(int, int)>>
                    {
                        { 0, new List<(int, int)> { (5, 6), (1, 3) } },
                        { 1, new List<(int, int)> { (0, 9) } },
                    })
                .SetName("ack contains all un-acked");
        }

        private static IEnumerable<TestCaseData> TestAcknowledgementFixtures()
        {
            // sessions amount
            // expected
            // unacked range
            // unacked session ids
            // ack sessionId => list of (range)
            // redelivered sessionId => list of (range), node 0..9, redelivered 0..3, 3..9 will become undelivered

            // left space = node [2..3] ack range [0..3] result is [0..2]
            // right space = last node [3..5] ack range [3..7] result is [5..7] 

            // session0 unacked [0..9]
            // session1 unacked [0..9]
            // session0 ack [0..3][4..5][6..7]
            // session0 unacked [3..4][5..6][7..9]
            // session1 ack [1..8]
            // result [1..3][4..5][6..7]
            yield return new TestCaseData(
                    2,
                    new List<AckRangeNode>
                    {
                        new() { First = 1, Last = 3 },
                        new() { First = 4, Last = 5 },
                        new() { First = 6, Last = 7 },
                    },
                    (0, 9),
                    new List<int> { 0, 1 },
                    new Dictionary<int, List<(int, int)>>
                    {
                        { 0, new List<(int, int)> { (0, 3), (4, 5), (6, 7) } },
                        { 1, new List<(int, int)> { (1, 8) } },
                    })
                .SetName("left space; multiple un-acked;");
            // session0 unacked [0..9]
            // session1 unacked [0..9]
            // session0 ack [0..3][4..5][6..7][8..9]
            // session0 unacked [3..4][5..6][7..8]
            // session1 ack [1..9]
            // result [1..3][4..5][6..7][8..9]
            yield return new TestCaseData(
                    2,
                    new List<AckRangeNode>
                    {
                        new() { First = 1, Last = 3 },
                        new() { First = 4, Last = 5 },
                        new() { First = 6, Last = 7 },
                        new() { First = 8, Last = 9 },
                    },
                    (0, 9),
                    new List<int> { 0, 1 },
                    new Dictionary<int, List<(int, int)>>
                    {
                        { 0, new List<(int, int)> { (0, 3), (4, 5), (6, 7), (8, 9) } },
                        { 1, new List<(int, int)> { (1, 9) } },
                    })
                .SetName("left space; few un-acked; right space");
            // session0 unacked [0..10]
            // session1 unacked [0..10]
            // session0 ack [0..4][6..10]
            // session0 unacked [4..6]
            // session1 ack [1..9]
            // result [1..4][6..9]
            yield return new TestCaseData(
                    2,
                    new List<AckRangeNode>
                    {
                        new() { First = 1, Last = 4 },
                        new() { First = 6, Last = 9 },
                    },
                    (0, 10),
                    new List<int> { 0, 1 },
                    new Dictionary<int, List<(int, int)>>
                    {
                        { 0, new List<(int, int)> { (0, 4), (6, 10) } },
                        { 1, new List<(int, int)> { (1, 9) } },
                    })
                .SetName("left space; one un-acked; right space");
            // session0 took [0..9]
            // session1 took [0..9]
            // session0 ack [0..3]
            // session0 ack [4..9]
            // session0 unacked [3..4]
            // session1 ack [3..5]
            // result [4, 5]
            yield return new TestCaseData(
                    2,
                    new List<AckRangeNode>
                    {
                        new() { First = 4, Last = 5 },
                    },
                    (0, 9),
                    new List<int> { 0, 1 },
                    new Dictionary<int, List<(int, int)>>
                    {
                        { 0, new List<(int, int)> { (0, 3), (4, 9) } },
                        { 1, new List<(int, int)> { (3, 5) } },
                    })
                .SetName("; node first equals ack first; right space");
            // session0 unacked [0..9]
            // session1 unacked [0..9]
            // session0 ack [2..5], unacked - [0..2][5..9]
            // session1 ack [1..7]
            // result [2..5]
            yield return new TestCaseData(
                    2,
                    new List<AckRangeNode>
                    {
                        new() { First = 2, Last = 5 },
                    },
                    (0, 9),
                    new List<int> { 0, 1 },
                    new Dictionary<int, List<(int, int)>>
                    {
                        { 0, new List<(int, int)> { (2, 5) } },
                        { 1, new List<(int, int)> { (1, 7) } },
                    })
                .SetName("; one un-acked;");
            // session0 unacked [0..9]
            // session1 unacked [0..9]
            // session0 ack [0..9]
            // release []
            yield return new TestCaseData(
                    2,
                    new List<AckRangeNode>(),
                    (0, 9),
                    new List<int> { 0, 1 },
                    new Dictionary<int, List<(int, int)>>
                    {
                        { 0, new List<(int, int)> { (0, 9) } },
                    })
                .SetName("; one full un-acked;");
            // session0 + session1 + session2 unacked [0..9]
            // session0 ack [1..3][4..5]
            // session1 ack [0..3][4..5]
            // session2 ack [0..5] <--
            // = session0 unacked [0..1][3..4][5..9]
            // = session1 unacked [3..4][5..9]
            // = session2 unacked [3..4][5..9]
            // release [1..3][4..5]
            yield return new TestCaseData(
                    3,
                    new List<AckRangeNode>
                    {
                        new() { First = 1, Last = 3 },
                        new() { First = 4, Last = 5 },
                    },
                    (0, 9),
                    new List<int> { 0, 1, 2 },
                    new Dictionary<int, List<(int, int)>>
                    {
                        { 0, new List<(int, int)> { (1, 3), (4, 5) } },
                        { 1, new List<(int, int)> { (0, 3), (4, 5) } },
                        { 2, new List<(int, int)> { (0, 5) } },
                    })
                .SetName("3 sessions; ; multiple un-acked;");
            yield return new TestCaseData(
                    2,
                    new List<AckRangeNode>
                    {
                        new() { First = 0, Last = 2 },
                    },
                    (0, 1_000),
                    new List<int> { 0, 1 },
                    new Dictionary<int, List<(int, int)>>
                    {
                        { 0, new List<(int, int)> { (0, 119) } },
                        { 1, new List<(int, int)> { (0, 2) } },
                    })
                .SetName("; ack range is on left side from node;");
            yield return new TestCaseData(
                    2,
                    new List<AckRangeNode>
                    {
                        new() { First = 101, Last = 102 },
                    },
                    (0, 1_000),
                    new List<int> { 0, 1 },
                    new Dictionary<int, List<(int, int)>>
                    {
                        { 0, new List<(int, int)> { (100, 119) } },
                        { 1, new List<(int, int)> { (101, 102) } },
                    })
                .SetName("; ack range is on right side from node;");
            yield return new TestCaseData(
                    2,
                    new List<AckRangeNode>(),
                    (0, 9),
                    new List<int> { 0, 1 },
                    new Dictionary<int, List<(int, int)>>
                    {
                        { 0, new List<(int, int)> { (0, 2) } },
                        { 1, new List<(int, int)> { (3, 9) } },
                    })
                .SetName("; unacked range;");
            // session0 took [0..9]
            // session1 took [0..9]
            // session0 ack [0..3]
            // session0 ack [4..5]
            // session0 ack [6..7]
            // session0 unacked [3..4][5..6]
            // session1 ack [3..9]
            // result [4, 5][6..7]
            yield return new TestCaseData(
                    2,
                    new List<AckRangeNode>
                    {
                        new() { First = 4, Last = 5 },
                        new() { First = 6, Last = 7 },
                    },
                    (0, 9),
                    new List<int> { 0, 1 },
                    new Dictionary<int, List<(int, int)>>
                    {
                        { 0, new List<(int, int)> { (0, 3), (4, 5), (6, 7) } },
                        { 1, new List<(int, int)> { (3, 9) } },
                    })
                .SetName("; node first equals ack first;");
            // session0 took [0..9]
            // session1 took [0..9]
            // session0 ack [0..2]
            // session0 ack [3..9]
            // session1 ack [0..9]
            // result [0..2][3..9]
            yield return new TestCaseData(
                    2,
                    new List<AckRangeNode>
                    {
                        new() { First = 0, Last = 2 },
                        new() { First = 3, Last = 9 },
                    },
                    (0, 9),
                    new List<int> { 0, 1 },
                    new Dictionary<int, List<(int, int)>>
                    {
                        { 0, new List<(int, int)> { (0, 2), (3, 9) } },
                        { 1, new List<(int, int)> { (0, 9) } },
                    })
                .SetName("; node is contained in ack;");
        }

        private static IEnumerable<TestCaseData> TestUndeliveredFixtures()
        {
            // sessions
            // expectedForRelease
            // expectedUndelivered
            // unacked range
            // unacked session ids
            // ackedBeforeTimeout
            // ackedAfterTimeout

            // session0 took 0..9
            // session1 took 0..9
            // session0 ack before timeout [0..3]
            // session0 undelivered [3..9]
            // session1 undelivered [0..9]
            // session0 ack [6..7]
            // session1 ack [1..3]
            // result [1..3]
            yield return new TestCaseData(
                    2,
                    new List<AckRangeNode>
                    {
                        new() { First = 1, Last = 3 },
                    },
                    new Dictionary<int, List<AckRangeNode>>
                    {
                        {
                            0, new List<AckRangeNode>
                            {
                                new() { First = 3, Last = 6 },
                                new() { First = 7, Last = 9 }
                            }
                        },
                        {
                            1, new List<AckRangeNode>
                            {
                                new() { First = 0, Last = 1 },
                                new() { First = 3, Last = 9 }
                            }
                        }
                    },
                    (0, 9),
                    new List<int> { 0, 1 },
                    new Dictionary<int, List<(int, int)>>
                    {
                        { 0, new List<(int, int)> { (0, 3) } },
                    },
                    new Dictionary<int, List<(int, int)>>
                    {
                        { 0, new List<(int, int)> { (6, 7) } },
                        { 1, new List<(int, int)> { (1, 3) } },
                    })
                .SetName("2 sessions; undelivered; ");
            // session0 took 0..9
            // session1 took 0..9
            // session0 undelivered [0..9]
            // session1 undelivered [0..9]
            // session0 ack [6..7]
            // result []
            yield return new TestCaseData(
                    2,
                    new List<AckRangeNode>(),
                    new Dictionary<int, List<AckRangeNode>>
                    {
                        {
                            0, new List<AckRangeNode>
                            {
                                new() { First = 0, Last = 6 },
                                new() { First = 7, Last = 9 }
                            }
                        },
                        {
                            1, new List<AckRangeNode>
                            {
                                new() { First = 0, Last = 9 },
                            }
                        }
                    },
                    (0, 9),
                    new List<int> { 0, 1 },
                    new Dictionary<int, List<(int, int)>>(),
                    new Dictionary<int, List<(int, int)>>
                    {
                        { 0, new List<(int, int)> { (6, 7) } },
                    })
                .SetName("2 sessions; all nodes undelivered; ");
        }

        private static ServerAckManager GetManager(int sessions = 2, ushort timeout = 6)
        {
            ServerConfig.MessageAckTimeout = timeout;

            ServerAckManager.DestroyInstance();
            var ackManager = ServerAckManager.Instance();

            ackManager.AddMessageClassId(1);

            for (var i = 0; i < sessions; i++)
                ackManager.AddSessionId(i);

            return ackManager;
        }
    }
}