using System;
using System.Collections.Generic;

namespace SimpleMessageBus.Abstractions
{
    public interface IMessagesIdsBinding
    {
        // type => messageClassId
        public Dictionary<Type, ushort> GetMessagesIdsBinding();

        public ushort GetMessageIdByType(Type type);

        public List<ushort> GetMessagesIds();
    }
}