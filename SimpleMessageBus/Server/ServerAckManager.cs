using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using SimpleMessageBus.Buffers;

namespace SimpleMessageBus.Server
{
    public class ServerAckManager
    {
        private static ServerAckManager _instance;
        private static readonly object InstanceLock = new();

        private readonly ushort _messageAckTimeout;
        private ushort _timerIndex;
        private readonly List<int> _sessionIds = new();

        // messageClassId => sessionId => timerIndex => buffer
        private readonly Dictionary<ushort, Dictionary<int, Dictionary<ushort, AckRangesBuffer>>> _unacked = new();

        private readonly object _unackedLock = new();

        // messageClassId => sessionId
        private readonly Dictionary<ushort, Dictionary<int, AckRangesBuffer>> _undelivered = new();

        private ServerAckManager()
        {
            _messageAckTimeout = ServerConfig.MessageAckTimeout;
        }

        public static ServerAckManager Instance()
        {
            if (_instance != null)
                return _instance;

            lock (InstanceLock)
                _instance ??= new ServerAckManager();

            return _instance;
        }

        public static void DestroyInstance()
        {
            _instance = null;
        }

        public void TimerTick()
        {
            lock (_unackedLock)
            {
                _timerIndex = (ushort)((_timerIndex + 1) % _messageAckTimeout);

                foreach (var (messageClassId, sessions) in _unacked)
                {
                    foreach (var (sessionId, buffers) in sessions)
                    {
                        var ranges = buffers[_timerIndex].GetUnAcked();

                        foreach (var node in ranges)
                        {
                            _undelivered[messageClassId][sessionId].AddUnAcked((node.First, node.Last));

                            Debug.WriteLine(
                                $"MsgClass: {messageClassId}. Session: {sessionId}. Unacked: {node.First}-{node.Last}");
                        }

                        buffers[_timerIndex].Clear();
                    }
                }
            }
        }

        public List<AckRangeNode> GetUndeliveredIds(ushort messageClassId, int sessionId)
        {
            return _undelivered[messageClassId][sessionId].GetUnAcked();
        }

        public Dictionary<int, AckRangesBuffer> GetUndeliveredIds(ushort messageClassId)
        {
            return _undelivered[messageClassId];
        }

        public void AddUnAcked(Memory<byte[]> messages, (int first, int last) range, ushort messageClassId,
            List<int> sessionsIds)
        {
            lock (_unackedLock)
            {
                foreach (var bytes in messages.Span)
                    bytes.InjectTimerIndex(_timerIndex);

                foreach (var sessionId in sessionsIds)
                    _unacked[messageClassId][sessionId][_timerIndex].AddUnAcked(range);
            }
        }

        public List<AckRangeNode> GetReadyForReleaseNodes((int First, int Last) range, ushort messageClassId,
            int sessionId,
            ushort timerIndex)
        {
            lock (_unackedLock)
            {
                var result = new List<AckRangeNode> { new() { First = range.First, Last = range.Last } };

                // if there is just 1 session there is no need for modification process
                // just release the whole range
                if (_sessionIds.Count == 1) return result;

                foreach (var id in _sessionIds.Where(id => id != sessionId))
                {
                    _unacked[messageClassId][id][timerIndex].Difference(result);

                    // it means that ack range is equal/contained in _unacked
                    // and ack range has been removed from result
                    if (result.Count == 0) break;

                    _undelivered[messageClassId][id].Difference(result);
                }

                return result;
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
                var sessionsForUnacked = new Dictionary<int, Dictionary<ushort, AckRangesBuffer>>();

                foreach (var sessionId in _sessionIds)
                {
                    var buffersForUnAcked = new Dictionary<ushort, AckRangesBuffer>();

                    for (ushort i = 0; i < _messageAckTimeout; i++)
                        buffersForUnAcked.Add(i, new AckRangesBuffer());

                    sessionsForUndelivered.Add(sessionId, new AckRangesBuffer());
                    sessionsForUnacked.Add(sessionId, buffersForUnAcked);
                }

                _undelivered.Add(messageClassId, sessionsForUndelivered);
                _unacked.Add(messageClassId, sessionsForUnacked);
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

                    for (ushort i = 0; i < _messageAckTimeout; i++)
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