using System;

namespace SimpleMessageBus.Abstractions
{
    public class MessageClassIdNotBindedException : Exception
    {
        public MessageClassIdNotBindedException() : base("Define your type in message ids binding first")
        {
        }
    }
}