using System;
using System.Buffers;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO.Pipelines;
using System.Net.Sockets;
using System.Threading.Tasks;
using SimpleMessageBus.Abstractions;
using SimpleMessageBus.Utils;

namespace SimpleMessageBus.Client
{
    public class TcpMessageBusClient : IDisposable
    {
        private readonly string _ip;
        private readonly int _port;

        private readonly NetworkStream _stream;
        private readonly Socket _socket;

        private readonly List<Task> _tasks = new();
        private readonly ClientMessageManager _clientMessageManager = new();
        private readonly Dictionary<ushort, Type> _messageBinding = new();

        public TcpMessageBusClient(string ip, int port)
        {
            _socket = new Socket(AddressFamily.InterNetwork, SocketType.Stream, ProtocolType.Tcp);
            _socket.Connect(ip, port);
            _stream = new NetworkStream(_socket);

            _ip = ip;
            _port = port;

            _messageBinding.Add(1, typeof(PersonMessage));
        }

        public void Send(IMessage message)
        {
            _clientMessageManager.AddMessage(message);
        }

        public void AcknowledgeMessage(ulong messageId)
        {
            _clientMessageManager.AddRawMessage(MessageType.Ack, messageId.ToString());
        }

        public void Subscribe(string channel)
        {
        }

        public async Task StartAsync()
        {
            var pipe = new Pipe();

            try
            {
                // _tasks.Add(Task.Run(GetCpuUsageForProcess));
                _tasks.Add(Task.Run(() => FillPipeAsync(_socket, pipe.Writer)));
                _tasks.Add(Task.Run(() => ReadPipeAsync(pipe.Reader)));

                _tasks.Add(Task.Run(WriteBulk));
                _tasks.Add(Task.Run(_clientMessageManager.Start));

                await Task.WhenAny(_tasks);
            }
            catch (AggregateException e)
            {
                Console.WriteLine(e);
                throw;
            }
            finally
            {
                Dispose();
                Console.WriteLine("Disconnect");
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

        private void OnMessageReceived(byte[] message)
        {
            try
            {
                message.DecodeMessage(out var type, out var messageClass);
                message = message.GetWithoutProtocol();

                switch (type)
                {
                    case MessageType.Heartbeat:
                        break;
                    case MessageType.Message:
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
                    var toSend = _clientMessageManager.ReadyToSendBuffer
                        .TakeMaxReadyElements()
                        .ToArray();

                    foreach (var bytes in toSend)
                        await _stream.WriteAsync(bytes);
                }
            }
            catch (Exception e)
            {
                Console.WriteLine(e);
                throw;
            }
        }

        private async Task GetCpuUsageForProcess()
        {
            while (true)
            {
                var startTime = DateTime.UtcNow;
                var startCpuUsage = Process.GetCurrentProcess().TotalProcessorTime;
                await Task.Delay(1000);

                var endTime = DateTime.UtcNow;
                var endCpuUsage = Process.GetCurrentProcess().TotalProcessorTime;
                var cpuUsedMs = (endCpuUsage - startCpuUsage).TotalMilliseconds;
                var totalMsPassed = (endTime - startTime).TotalMilliseconds;
                var cpuUsageTotal = cpuUsedMs / (Environment.ProcessorCount * totalMsPassed);

                Console.WriteLine($"CPU USAGE: {cpuUsageTotal * 100}");
            }
        }

        public void Dispose()
        {
            _stream?.Dispose();
            _socket?.Dispose();
            _clientMessageManager?.Dispose();
        }
    }
}