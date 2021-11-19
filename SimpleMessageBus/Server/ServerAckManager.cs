using System;
using System.Collections.Generic;
using SimpleMessageBus.Buffers;

namespace SimpleMessageBus.Server
{
    public class ServerAckManager
    {
        private static ServerAckManager _instance;
        private static readonly object InstanceLock = new();

        private const ushort MessageAckTimeout = 6;
        private ushort _timerIndex;
        private readonly List<int> _sessionIds = new();

        // messageClassId => sessionId => timerIndex => buffer
        private readonly Dictionary<ushort, Dictionary<int, Dictionary<ushort, AckRangesBuffer>>> _unacked = new();
        private readonly object _unackedLock = new();
        // messageClassId => sessionId
        private readonly Dictionary<ushort, Dictionary<int, AckRangesBuffer>> _undelivered = new();

        public static ServerAckManager Instance
        {
            get
            {
                if (_instance != null)
                    return _instance;

                lock (InstanceLock)
                    _instance ??= new ServerAckManager();

                return _instance;
            }
        }

        public void TimerTick()
        {
            lock (_unackedLock)
            {
                _timerIndex = (ushort)((_timerIndex + 1) % MessageAckTimeout);

                foreach (var (messageClassId, sessions) in _unacked)
                {
                    foreach (var (sessionId, buffers) in sessions)
                    {
                        var ranges = buffers[_timerIndex].GetUnAcked();

                        foreach (var node in ranges)
                        {
                            _undelivered[messageClassId][sessionId].AddUnAcked((node.First, node.Last));
                            Console.WriteLine(
                                $"MsgClass: {messageClassId}. Session: {sessionId}. Unacked: {node.First}-{node.Last}");
                        }

                        buffers[_timerIndex].Clear();
                    }
                }
            }
        }

        public List<AckRangeNode> GetUnAckedIds(ushort messageClassId, int sessionId)
        {
            return _undelivered[messageClassId][sessionId].GetUnAcked();
        }

        public void AddUnAcked(Memory<byte[]> messages, (int first, int last) range, ushort messageClassId,
            int sessionId)
        {
            lock (_unackedLock)
            {
                foreach (var bytes in messages.Span)
                    bytes.InjectTimerIndex(_timerIndex);

                _unacked[messageClassId][sessionId][_timerIndex].AddUnAcked(range);
            }
        }

        public void Ack((int first, int last) range, ushort messageClassId, int sessionId, ushort timerIndex)
        {
            lock (_unackedLock)
            {
                _unacked[messageClassId][sessionId][timerIndex].Ack(range);
                _undelivered[messageClassId][sessionId].Ack(range);
            }
        }

        public void AddMessageClassId(ushort messageClassId)
        {
            lock (_unackedLock)
            {
                var sessionsForUndelivered = new Dictionary<int, AckRangesBuffer>();
                var sessionsForUnackedV2 = new Dictionary<int, Dictionary<ushort, AckRangesBuffer>>();

                foreach (var sessionId in _sessionIds)
                {
                    var buffersForUnAcked = new Dictionary<ushort, AckRangesBuffer>();

                    for (ushort i = 0; i < MessageAckTimeout; i++)
                        buffersForUnAcked.Add(i, new AckRangesBuffer());

                    sessionsForUndelivered.Add(sessionId, new AckRangesBuffer());
                    sessionsForUnackedV2.Add(sessionId, buffersForUnAcked);
                }

                _undelivered.Add(messageClassId, sessionsForUndelivered);
                _unacked.Add(messageClassId, sessionsForUnackedV2);
            }
        }

        public void AddSessionId(int sessionId)
        {
            lock (_unackedLock)
            {
                _sessionIds.Add(sessionId);

                foreach (var sessions in _unacked.Values)
                {
                    var buffer = new Dictionary<ushort, AckRangesBuffer>();

                    for (ushort i = 0; i < MessageAckTimeout; i++)
                        buffer.Add(i, new AckRangesBuffer());

                    sessions.Add(sessionId, buffer);
                }

                foreach (var sessions in _undelivered.Values)
                    sessions.Add(sessionId, new AckRangesBuffer());
            }
        }

        public void RemoveSessionId(int sessionId)
        {
            lock (_unackedLock)
            {
                foreach (var messageClassesIds in _unacked.Values)
                    messageClassesIds.Remove(sessionId);

                foreach (var messageClassesIds in _undelivered.Values)
                    messageClassesIds.Remove(sessionId);
            }
        }
    }
}