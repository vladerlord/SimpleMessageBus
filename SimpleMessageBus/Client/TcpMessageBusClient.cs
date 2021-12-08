using System;
using System.Collections.Generic;
using System.IO.Pipelines;
using System.Net.Sockets;
using System.Threading.Tasks;
using SimpleMessageBus.Abstractions;

namespace SimpleMessageBus.Client
{
    public class TcpMessageBusClient : TcpSocket
    {
        private readonly NetworkStream _stream;
        private readonly Socket _socket;

        private readonly List<Task> _tasks = new();

        private readonly ClientMessageManager _clientMessageManager;
        private readonly MessageAcknowledgementManager _messageAcknowledgementManager;

        // type => messageClassId
        private readonly Dictionary<Type, ushort> _messageBinding = new();

        // messageClassId => consumer callback
        private readonly Dictionary<ushort, Action<IMessage>> _subscribers = new();

        // todo, replace with session id from server by Connect packet
        private static int _clientIdCounter;

        public TcpMessageBusClient(string ip, int port)
        {
            _clientIdCounter++;
            _socket = new Socket(AddressFamily.InterNetwork, SocketType.Stream, ProtocolType.Tcp);
            _clientMessageManager = new ClientMessageManager(_clientIdCounter);
            _socket.Connect(ip, port);
            _stream = new NetworkStream(_socket);

            _messageAcknowledgementManager = new MessageAcknowledgementManager(_clientMessageManager);
        }

        public void AddBinding(Type type, ushort messageClassId)
        {
            _messageBinding.Add(type, messageClassId);
            _messageAcknowledgementManager.AddMessageClassId(messageClassId);
            _clientMessageManager.AddBinding(type, messageClassId);
        }

        public void Send(IMessage message)
        {
            _clientMessageManager.AddMessage(message, _messageBinding[message.GetType()]);
        }

        public void Subscribe<T>(Action<T> action) where T : IMessage
        {
            var messageClassId = _messageBinding[typeof(T)];
            _subscribers.Add(messageClassId, message => action((T)message));

            _clientMessageManager.AddRawMessage(MessageType.Subscribe, null, messageClassId, 0, 0);
        }

        public async Task StartAsync()
        {
            var pipe = new Pipe();

            try
            {
                _tasks.Add(Task.Run(() => FillPipeAsync(_socket, pipe.Writer)));
                _tasks.Add(Task.Run(() => ReadPipeAsync(pipe.Reader)));

                _tasks.Add(Task.Run(WriteBulk));
                _tasks.Add(Task.Run(ProcessReceivedMessages));
                _tasks.Add(Task.Run(_clientMessageManager.Start));
                _tasks.Add(Task.Run(_messageAcknowledgementManager.Start));

                await Task.WhenAny(_tasks);
            }
            catch (AggregateException e)
            {
                Console.WriteLine(e);
                throw;
            }
            finally
            {
                Console.WriteLine("Disconnect");
            }
        }

        protected override void OnMessageReceived(byte[] message)
        {
            try
            {
                message.DecodeMessage(out var type, out var messageClass, out var messageId, out var timerIndex);
                message = message.GetWithoutProtocol();

                switch (type)
                {
                    case MessageType.Heartbeat:
                        break;
                    case MessageType.Message:
                        _clientMessageManager.AddReceivedMessage(message, messageClass, messageId, timerIndex);

                        break;
                    case MessageType.Subscribe:
                        break;
                    case MessageType.Ack:
                        break;
                    case MessageType.Connect:
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

        private Task ProcessReceivedMessages()
        {
            try
            {
                while (true)
                {
                    var stack = _clientMessageManager.ReadyToReceive
                        .TakeMaxReadyElements();

                    foreach (var messages in stack.Span)
                    {
                        foreach (var message in messages)
                        {
                            _subscribers[message.MessageClassId].Invoke(message.Content);

                            _messageAcknowledgementManager.AcknowledgeMessage(message.MessageClassId,
                                message.MessageId, message.TimerIndex);
                        }
                    }

                    _clientMessageManager.ReadyToReceive.Release(stack.Length);
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
                        .TakeMaxReadyElements();

                    for (var index = 0; index < toSend.Span.Length; index++)
                    {
                        var bytes = toSend.Span[index];

                        await _stream.WriteAsync(bytes);
                    }

                    _clientMessageManager.ReadyToSendBuffer.Release(toSend.Length);
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