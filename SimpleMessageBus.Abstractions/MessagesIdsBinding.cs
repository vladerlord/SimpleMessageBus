using System;
using System.Collections.Generic;
using System.Linq;

namespace SimpleMessageBus.Abstractions
{
    public abstract class MessagesIdsBinding : IMessagesIdsBinding
    {
        protected readonly Dictionary<Type, ushort> MessageBinding = new();

        public Dictionary<Type, ushort> GetMessagesIdsBinding()
        {
            return MessageBinding;
        }

        public ushort GetMessageIdByType(Type type)
        {
            if (!MessageBinding.ContainsKey(type))
                throw new MessageClassIdNotBindedException();

            return MessageBinding[type];
        }

        public List<ushort> GetMessagesIds()
        {
            return MessageBinding.Values.ToList();
        }
    }
}