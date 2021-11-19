using System;
using System.Collections.Generic;
using SimpleMessageBus.Abstractions;

namespace SimpleMessageBus
{
    public static class MessageProtocolExtension
    {
        /**
         * 0 - message type
         * 1-2 - message class id
         * 3-6 - unique message id inside the buffer
         * 7-8 - server ack timeout index
         */
        public static void DecodeMessage(this byte[] content, out MessageType type, out ushort messageClass,
            out int messageId, out ushort timerIndex)
        {
            type = (MessageType)Convert.ToChar(content[0]);
            messageClass = (ushort)(content[1] << 8 | content[2]);
            content.ExtractMessageId(out messageId);
            content.ExtractTimerIndex(out timerIndex);
        }

        public static byte[] GetWithoutProtocol(this byte[] content)
        {
            return content[MessageConfig.HeaderLength..^MessageConfig.DelimiterLength];
        }

        public static void InjectMessageId(this byte[] message, int messageId)
        {
            message[3] = (byte)(messageId >> 24);
            message[4] = (byte)(messageId >> 16);
            message[5] = (byte)(messageId >> 8);
            message[6] = (byte)messageId;
        }

        private static void ExtractMessageId(this IReadOnlyList<byte> message, out int messageId)
        {
            messageId =
                (int)message[3] << 24
                | (int)message[4] << 16
                | (int)message[5] << 8
                | message[6];
        }

        public static void InjectTimerIndex(this byte[] message, ushort timerIndex)
        {
            message[7] = (byte)(timerIndex >> 8);
            message[8] = (byte)timerIndex;
        }

        private static void ExtractTimerIndex(this IReadOnlyList<byte> message, out ushort timerIndex)
        {
            timerIndex = (ushort)(message[7] << 8 | message[8]);
        }
    }
}