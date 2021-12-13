using System.Collections.Generic;

namespace SimpleMessageBus.Server
{
    public static class ServerMessagesIdsBinding
    {
        public static readonly List<ushort> MessageClassIds = new();
        public static readonly object MessageClassIdsLock = new();

        public static event OnMessageClassIdAddDelegate OnMessageClassIdAdd;

        public delegate void OnMessageClassIdAddDelegate(ushort messageClassId);

        public static void AddMessagesClassesIds(IEnumerable<ushort> messagesClassesIds)
        {
            lock (MessageClassIdsLock)
            {
                foreach (var messageClassId in messagesClassesIds)
                {
                    if (MessageClassIds.Contains(messageClassId)) continue;

                    MessageClassIds.Add(messageClassId);
                    OnMessageClassIdAdd?.Invoke(messageClassId);
                }
            }
        }
    }
}