using SimpleMessageBus.Abstractions;

namespace SimpleMessageBus
{
    public static class MessageConfig
    {
        public static readonly byte[] Delimiter = {(byte) '\r', (byte) '\n'};
        public const int DelimiterLength = 2;
        public const ushort HeaderLength = 3;

        public static byte[] CreateTcpHeader(MessageType messageType, ushort messageClassId)
        {
            var header = new byte[HeaderLength];

            header[0] = (byte) messageType;

            header[1] = (byte) (messageClassId >> 8);
            header[2] = (byte) messageClassId;

            return header;
        }
    }
}