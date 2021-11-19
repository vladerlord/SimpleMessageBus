using System;

namespace SimpleMessageBus.Utils
{
    public static class ArrayExtension
    {
        public static byte[] Flatten(this Memory<byte[]> arrays)
        {
            var length = 0;

            for (var j = 0; j < arrays.Length; j++)
                length += arrays.Span[j].Length;

            var buffer = new byte[length];
            var offset = 0;

            foreach (var array in arrays.Span)
            {
                Buffer.BlockCopy(array, 0, buffer, offset, array.Length);
                offset += array.Length;
            }

            return buffer;
        }
    }
}