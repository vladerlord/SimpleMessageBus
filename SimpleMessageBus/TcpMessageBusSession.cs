using System;
using System.Buffers;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO.Pipelines;
using System.Net.Sockets;
using System.Threading.Tasks;
using SimpleMessageBus.Abstractions;

namespace SimpleMessageBus
{
    public class TcpMessageBusSession
    {
        private readonly Socket _socket;
        private readonly NetworkStream _stream;
        private readonly TcpMessageBusServer _tcpMessageBusServer;

        public TcpMessageBusBandwidth BandwidthInfo { get; } = new();

        private readonly List<Task> _tasks = new();

        public TcpMessageBusSession(TcpMessageBusServer messageBusServer, Socket socket)
        {
            _tcpMessageBusServer = messageBusServer;
            _socket = socket;
            _stream = new NetworkStream(socket);
        }

        public async Task StartAsync()
        {
            var pipe = new Pipe();

            try
            {
                _tasks.Add(FillPipeAsync(_socket, pipe.Writer));
                _tasks.Add(Task.Run(() => ReadPipeAsync(pipe.Reader)));

                await Task.WhenAny(_tasks);
            }
            catch (Exception e)
            {
                Console.WriteLine(e);
                throw;
            }
        }

        private static async Task FillPipeAsync(Socket socket, PipeWriter writer)
        {
            try
            {
                const int minimumBufferSize = 9000;

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

        private async Task ReadPipeAsync(PipeReader reader)
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

        private static bool TryReadLine(ref ReadOnlySequence<byte> buffer, out ReadOnlySequence<byte> line)
        {
            var position = buffer.PositionOf(MessageConfig.Delimiter);

            if (position == null)
            {
                line = default;
                return false;
            }

            line = buffer.Slice(0, position.Value);
            buffer = buffer.Slice(buffer.GetPosition(1, position.Value));

            return true;
        }
        
        private static int lastid = -1;
        private static bool check;

        private void OnMessageReceived(byte[] message)
        {
            // BandwidthInfo.ReadBytes += message.Length;
            // BandwidthInfo.ReadMessages++;

            try
            {
                //
                // var q =  (MessageType) Convert.ToChar(message[0]);
                // Console.WriteLine(q);
                
                message.DecodeMessage(out var type);
                message = message.GetWithoutProtocol();

                // Console.WriteLine(message.GetString());

                switch (type)
                {
                    case MessageType.Heartbeat:
                        break;
                    case MessageType.Message:
                        var personMessage = message.Deserialize<PersonMessage>();

                        if (personMessage.Id != ++lastid && !check)
                        {
                            Console.WriteLine($"Should be: {lastid}, is: {personMessage.Id}");
                            check = true;
                            // Console.WriteLine("FUCK");
                        }
                        
                        

                        // Console.WriteLine(deserialized.MessageId);
                        // var returnType = Type.GetType(deserialized.MessageClass);
                        // var messageDecoded = (Person)deserialized.Content.Deserialize(returnType);

                        BandwidthInfo.ReadBytes += message.Length;
                        BandwidthInfo.ReadMessages++;

                        // Console.WriteLine(messageDecoded.Id);

                        // if (messageDecoded.Id != counter++)
                        // {
                        //     Console.WriteLine("WTF");
                        // }

                        break;
                    case MessageType.Subscribe:
                        break;
                    case MessageType.Ack:
                        break;
                    default:
                        throw new ArgumentOutOfRangeException(nameof(type), type, null);
                }
            }
            catch (Exception e)
            {
                Console.WriteLine(e);
                throw;
            }
        }
    }
}