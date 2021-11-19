using System;
using System.Buffers;
using System.IO.Pipelines;
using System.Net.Sockets;
using System.Threading.Tasks;
using SimpleMessageBus.Utils;

namespace SimpleMessageBus
{
    public abstract class TcpSocket
    {
        protected static async Task FillPipeAsync(Socket socket, PipeWriter writer)
        {
            try
            {
                const int minimumBufferSize = 1000;

                while (true)
                {
                    var memory = writer.GetMemory(minimumBufferSize);
                    var bytesRead = await socket.ReceiveAsync(memory, SocketFlags.None);

                    if (bytesRead == 0)
                        break;

                    writer.Advance(bytesRead);

                    await writer.FlushAsync();
                }
            }
            catch (Exception e)
            {
                Console.WriteLine(e);
                throw;
            }
        }

        protected async Task ReadPipeAsync(PipeReader reader)
        {
            try
            {
                while (true)
                {
                    var result = await reader.ReadAsync();
                    var buffer = result.Buffer;

                    while (TryReadLine(ref buffer, out var line))
                        OnMessageReceived(line.ToArray());

                    reader.AdvanceTo(buffer.Start, buffer.End);
                }
            }
            catch (Exception e)
            {
                Console.WriteLine(e);
                throw;
            }
        }

        private static bool TryReadLine(ref ReadOnlySequence<byte> buffer, out ReadOnlySequence<byte> line)
        {
            var position = buffer.GetDelimiterPosition();

            if (position == null)
            {
                line = default;
                return false;
            }

            line = buffer.Slice(0, position.Value + MessageConfig.DelimiterLength);
            buffer = buffer.Slice(position.Value + MessageConfig.DelimiterLength);

            return true;
        }

        protected abstract void OnMessageReceived(byte[] message);
    }
}