using System;
using System.Buffers;

namespace SimpleMessageBus.Utils
{
    public static class BufferExtension
    {
        public static int? GetDelimiterPosition(in this ReadOnlySequence<byte> source)
        {
            var reader = new SequenceReader<byte>(source);
            var crlf = new ReadOnlySpan<byte>(MessageConfig.Delimiter);

            while (!reader.End)
            {
                if (!reader.TryReadTo(out ReadOnlySpan<byte> headerLine, crlf, true))
                    break;

                return headerLine.Length;
            }

            return null;
        }
    }
}