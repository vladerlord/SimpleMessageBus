using SimpleMessageBus.Abstractions;

namespace SimpleMessageBus
{
    public static class MessageConfig
    {
        public static readonly byte[] Delimiter = {(byte) '\r', (byte) '\n'};
        // public static readonly byte[] Delimiter = {(byte) 'w', (byte) 't'};
        public static readonly int DelimiterLength = 2;

        public static byte[] CreateTcpHeader(MessageType messageType, ushort messageClassId)
        {
            var header = new byte[3];

            header[0] = (byte) messageType;
            header[1] = (byte) (messageClassId >> 8);
            header[2] = (byte) messageClassId;

            return header;
        }
    }
}