using System;
using System.Collections.Generic;
using System.IO.Pipelines;
using System.Net.Sockets;
using System.Threading;
using System.Threading.Tasks;
using SimpleMessageBus.Abstractions;
using SimpleMessageBus.Buffers;
using SimpleMessageBus.Utils;

namespace SimpleMessageBus.Server
{
    public class TcpMessageBusSession : TcpSocket
    {
        private readonly Socket _socket;
        private readonly NetworkStream _stream;
        private readonly TcpMessageBusServer _tcpMessageBusServer;
        private readonly int _sessionId;

        public TcpMessageBusBandwidth BandwidthInfo { get; } = new();
        private readonly ServerMessageManager _serverMessageManager;

        private readonly BoundedAllocatingBlockingBuffer<byte[]> _messageBuffer = new(2_000_000);

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
                _tasks.Add(Task.Run(() => FillPipeAsync(_socket, pipe.Writer)));
                _tasks.Add(Task.Run(() => ReadPipeAsync(pipe.Reader)));
                _tasks.Add(Task.Run(WriteBulk));

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

        protected override void OnMessageReceived(byte[] message)
        {
            try
            {
                message.DecodeMessage(out var type, out var messageClassId, out var messageId, out var timerIndex);

                switch (type)
                {
                    case MessageType.Heartbeat:
                        break;
                    case MessageType.Message:
                        _serverMessageManager.AddMessage(message, messageClassId);
                        Interlocked.Increment(ref BandwidthInfo.ReadMessages);

                        break;
                    case MessageType.Subscribe:
                        _serverMessageManager.Subscribe(messageClassId, _sessionId);

                        break;
                    case MessageType.Ack:
                        message = message.GetWithoutProtocol();
                        var ids = message.GetString().Split("-");

                        _serverMessageManager.Acknowledge(
                            (int.Parse(ids[0]), int.Parse(ids[1])),
                            messageClassId,
                            _sessionId,
                            timerIndex
                        );

                        break;
                    case MessageType.Connect:
                        // todo, deserialize connect message, extract message classes ids binding

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
                    var toSend = _messageBuffer.TakeMaxItems();

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

        public void AddMessage(byte[] message)
        {
            _messageBuffer.Add(message);
        }
    }
}