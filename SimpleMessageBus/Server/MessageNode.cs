using SimpleMessageBus.Abstractions;

namespace SimpleMessageBus.Server
{
    public class MessageNode
    {
        public MessageType MessageType;
        public byte[] Content;
        public ushort MessageClass;
    }
}