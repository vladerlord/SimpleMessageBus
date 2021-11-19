using System;
using System.Buffers;

namespace SimpleMessageBus.Utils
{
    public static class BufferExtension
    {
        private static readonly ReadOnlyMemory<byte> Delimiter = new(MessageConfig.Delimiter);

        public static int? GetDelimiterPosition(in this ReadOnlySequence<byte> source)
        {
            var reader = new SequenceReader<byte>(source);

            // skip parsing header because header can contain delimiter bytes
            if (source.Length >= MessageConfig.HeaderLength)
                reader.Advance(MessageConfig.HeaderLength);

            while (!reader.End)
            {
                if (!reader.TryReadTo(out ReadOnlySpan<byte> message, Delimiter.Span))
                    break;

                return message.Length + MessageConfig.HeaderLength;
            }

            return null;
        }
    }
}