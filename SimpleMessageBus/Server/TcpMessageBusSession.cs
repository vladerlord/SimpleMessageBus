using System;
using System.Buffers;
using System.Collections.Generic;
using System.IO.Pipelines;
using System.Net.Sockets;
using System.Threading.Tasks;
using SimpleMessageBus.Abstractions;
using SimpleMessageBus.Buffers;
using SimpleMessageBus.Utils;

namespace SimpleMessageBus.Server
{
    public class TcpMessageBusSession : IDisposable
    {
        private readonly Socket _socket;
        private readonly NetworkStream _stream;
        private readonly TcpMessageBusServer _tcpMessageBusServer;
        private readonly int _sessionId;

        public TcpMessageBusBandwidth BandwidthInfo { get; } = new();
        private readonly ServerMessageManager _serverMessageManager;
        private readonly BoundedSingleThreadBlockingArrayBuffer<MessageNode> _messageBuffer = new(1000);

        private readonly List<Task> _tasks = new();

        public TcpMessageBusSession(TcpMessageBusServer messageBusServer, int sessionId, Socket socket)
        {
            _tcpMessageBusServer = messageBusServer;
            _sessionId = sessionId;
            _socket = socket;
            _stream = new NetworkStream(socket);
            _serverMessageManager = ServerMessageManager.GetInstance();
        }

        public async Task StartAsync()
        {
            var pipe = new Pipe();

            try
            {
                _tasks.Add(FillPipeAsync(_socket, pipe.Writer));
                _tasks.Add(Task.Run(() => ReadPipeAsync(pipe.Reader)));
                _tasks.Add(Task.Run(WriteBulk));
                _tasks.Add(Ping());

                await Task.WhenAny(_tasks);
            }
            catch (Exception e)
            {
                Console.WriteLine(e);
            }
            finally
            {
                _tcpMessageBusServer.Disconnect(_sessionId);
            }
        }

        private static async Task FillPipeAsync(Socket socket, PipeWriter writer)
        {
            try
            {
                const int minimumBufferSize = 100;

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

            line = buffer.Slice(0, position.Value);
            buffer = buffer.Slice(position.Value + MessageConfig.DelimiterLength);

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
                message.DecodeMessage(out var type, out var messageClass);
                message = message.GetWithoutProtocol();

                switch (type)
                {
                    case MessageType.Heartbeat:
                        break;
                    case MessageType.Message:
                        // var personMessage = message.Deserialize<PersonMessage>();

                        // _serverMessageManager.AddMessage(message, messageClass, channel);

                        // if (personMessage.Id != ++lastid && !check)
                        // {
                        //     Console.WriteLine($"Should be: {lastid}, is: {personMessage.Id}");
                        //     check = true;
                        // }

                        BandwidthInfo.ReadBytes += message.Length;
                        BandwidthInfo.ReadMessages++;

                        break;
                    case MessageType.Subscribe:
                        break;
                    case MessageType.Ack:
                        // Console.WriteLine(message.GetString());

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

        private async Task WriteBulk()
        {
            try
            {
                while (true)
                {
                    var toSend = _messageBuffer.TakeMaxItems().ToArray();

                    foreach (var message in toSend)
                    {
                        await _stream.WriteAsync(MessageConfig.CreateTcpHeader(message.MessageType,
                            message.MessageClass));
                        await _stream.WriteAsync(message.Content);
                        await _stream.WriteAsync(MessageConfig.Delimiter);
                    }
                }
            }
            catch (Exception e)
            {
                Console.WriteLine(e);
                throw;
            }
        }

        private async Task Ping()
        {
            while (true)
            {
                await Task.Delay(1000);

                _messageBuffer.Add(new MessageNode()
                {
                    MessageType = MessageType.Heartbeat,
                    Content = "PING".ToBytes(),
                });
            }
        }

        public void Dispose()
        {
            _socket?.Dispose();
            _stream?.Dispose();
        }
    }
}