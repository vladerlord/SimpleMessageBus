using System;
using System.Collections.Generic;
using System.Linq;

namespace SimpleMessageBus.Server
{
    public class ServerMessageManager
    {
        private static ServerMessageManager _instance;
        private static readonly object InstanceLock = new();

        private readonly ServerAckManager _ackManager;

        // message class id => list of subscribed session ids to that message class id
        private readonly Dictionary<ushort, List<int>> _subscribers = new();

        // message class id => message buffer
        private readonly Dictionary<ushort, ServerMessagesBuffer> _buffers = new();
        private readonly List<ushort> _buffersMessageClassIds = new();
        public List<ushort> GetMessageClassesIds() => _buffersMessageClassIds;

        private readonly object _acknowledgeLock = new();

        private ServerMessageManager()
        {
            _ackManager = ServerAckManager.Instance();

            // TODO, move to separate protocol message
            // because checking with containsKey in every addMessage is costly
            AddMessageClassId(1);
        }

        public static ServerMessageManager Instance()
        {
            if (_instance != null)
                return _instance;

            lock (InstanceLock)
                _instance ??= new ServerMessageManager();

            return _instance;
        }

        private void AddMessageClassId(ushort messageClassId)
        {
            _buffers.Add(messageClassId, new ServerMessagesBuffer(50_000_000));
            _buffersMessageClassIds.Add(messageClassId);
            _ackManager.AddMessageClassId(messageClassId);
        }

        public IEnumerable<Memory<byte[]>> GetUndeliveredMessages(ushort messageClassId, int sessionId)
        {
            var undeliveredRanges = _ackManager.GetUndeliveredIds(messageClassId, sessionId);

            if (undeliveredRanges == null)
                yield break;

            foreach (var undeliveredRange in undeliveredRanges)
                yield return _buffers[messageClassId].TakeByRangeNode(undeliveredRange);
        }

        public void AddMessage(byte[] content, ushort messageClassId)
        {
            _buffers[messageClassId].Add(content);
        }

        public int GetLength(ushort messageClassId)
        {
            return _buffers[messageClassId].GetLength();
        }

        public Memory<byte[]> TakeMaxItems(ushort messageClassId, List<int> sessionsIds)
        {
            var result = _buffers[messageClassId].TakeMax(out var range);

            if (result.Length <= 0)
                return result;

            _ackManager.AddUnAcked(result, range, messageClassId, sessionsIds);

            return result;
        }

        public void Acknowledge((int first, int last) range, ushort messageClassId, int sessionId, ushort timerIndex)
        {
            lock (_acknowledgeLock)
            {
                _ackManager.Ack(range, messageClassId, sessionId, timerIndex);

                var toRelease = _ackManager.GetReadyForReleaseNodes(range, messageClassId, sessionId, timerIndex);

                foreach (var ackRangeNode in toRelease)
                {
#if SERVER_MESSAGE_MANAGER_DEBUG
                    Console.WriteLine($"=========== releasing from buffer: {ackRangeNode.First}-{ackRangeNode.Last}");
#endif
                    _buffers[messageClassId].Acknowledge(ackRangeNode);
                }
            }
        }

        public void Subscribe(ushort messageClassId, int sessionId)
        {
            lock (_subscribers)
            {
                if (!_subscribers.ContainsKey(messageClassId))
                    _subscribers.Add(messageClassId, new List<int>());

                Console.WriteLine($"[] {sessionId} is subscribed on {messageClassId} now");

                _subscribers[messageClassId].Add(sessionId);
            }
        }

        public void RemoveSessionId(int sessionId)
        {
            lock (_subscribers)
            {
                foreach (var sessions in _subscribers.Values.Where(sessions => sessions.Contains(sessionId)))
                    sessions.Remove(sessionId);
            }
        }

        public List<int> GetSubscribedSessionsIds(ushort messageClassId)
        {
            lock (_subscribers)
                return _subscribers.ContainsKey(messageClassId) ? _subscribers[messageClassId] : null;
        }
    }
}