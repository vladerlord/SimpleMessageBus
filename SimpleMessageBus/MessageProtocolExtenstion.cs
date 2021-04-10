using System;
using SimpleMessageBus.Abstractions;

namespace SimpleMessageBus
{
    public static class MessageProtocolExtension
    {
        public static void DecodeMessage(this byte[] content, out MessageType type, out ushort messageClass)
        {
            type = (MessageType) Convert.ToChar(content[0]);
            messageClass = (ushort) (content[1] << 8 | content[2]);
        }

        public static byte[] GetWithoutProtocol(this byte[] content)
        {
            return content[MessageConfig.HeaderLength..];
        }
    }
}