using System;

namespace SimpleMessageBus.Abstractions
{
    public enum MessageType
    {
        Heartbeat = '0',
        Message = '1',
        Subscribe = '2',
        Ack = '3'
    }

    public interface IMessage : ICloneable
    {
        public long MessageId { get; set; }
    }
}