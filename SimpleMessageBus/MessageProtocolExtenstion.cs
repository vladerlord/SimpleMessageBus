using System;

namespace SimpleMessageBus
{
    public static class MessageProtocolExtension
    {
        // public static readonly byte[] Delimiter = {0x13, 0x14};
        private const ushort HeaderLength = 1;

        public static byte[] EncodeMessage(this byte[] content, MessageType type)
        {
            return content
                .AddDelimiter()
                .PrefixWithProtocol(type);
        }

        public static void DecodeMessage(this byte[] content, out MessageType type)
        {
            type = (MessageType) Convert.ToChar(content[0]);
        }

        public static byte[] GetWithoutProtocol(this byte[] content)
        {
            return content[HeaderLength..];
        }

        private static byte[] AddDelimiter(this byte[] content)
        {
            var buffer = new byte[content.Length + MessageConfig.DelimiterLength];

            content.CopyTo(buffer, 0);
            buffer[^1] = MessageConfig.Delimiter;

            return buffer;
        }

        private static byte[] PrefixWithProtocol(this byte[] content, MessageType type)
        {
            var header = new byte[HeaderLength];
            var buffer = new byte [header.Length + content.Length];
            header[0] = (byte) type;

            header.CopyTo(buffer, 0);
            content.CopyTo(buffer, header.Length);

            return buffer;
        }
    }
}