using SimpleMessageBus.Abstractions;

namespace SimpleMessageBus
{
    public static class MessageConfig
    {
        public static readonly byte[] Delimiter = { (byte)'<', (byte)'\r', (byte)'\n', (byte)'>' };

        public const int DelimiterLength = 4;
        public const ushort HeaderLength = 9;

        public static byte[] CreateTcpHeader(MessageType messageType, ushort messageClassId, int messageId, ushort timerIndex)
        {
            var header = new byte[HeaderLength];

            // type. heartbeat, subscribe, message etc
            header[0] = (byte)messageType;

            // id of message class. OrderCreatedEvent but as short
            header[1] = (byte)(messageClassId >> 8);
            header[2] = (byte)messageClassId;

            // 4 bytes. unique message id for ack, index in buffer
            header.InjectMessageId(messageId);
            
            // 2 bytes. timer index that represents the time the message was sent from server to clint
            // used to detect undelivered messages
            header.InjectTimerIndex(timerIndex);

            return header;
        }
    }
}