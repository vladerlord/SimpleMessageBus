using System;
using System.Collections.Generic;
using System.IO.Pipelines;
using System.Net.Sockets;
using System.Threading;
using System.Threading.Tasks;
using Newtonsoft.Json;
using SimpleMessageBus.Abstractions;

namespace SimpleMessageBus.Client
{
    public class TcpMessageBusClient : TcpSocket
    {
        private readonly NetworkStream _stream;
        private readonly Socket _socket;
        private bool _isConnected;

        private readonly List<Task> _tasks = new();

        private readonly ClientMessageManager _clientMessageManager;
        private readonly MessageAcknowledgementManager _messageAcknowledgementManager;
        private readonly IMessagesIdsBinding _messagesIdsBinding;

        // messageClassId => consumer callback
        private readonly Dictionary<ushort, Action<IMessage>> _subscribers = new();

        public TcpMessageBusClient(string ip, int port, IMessagesIdsBinding messagesIdsBinding)
        {
            _socket = new Socket(AddressFamily.InterNetwork, SocketType.Stream, ProtocolType.Tcp);
            _clientMessageManager = new ClientMessageManager(messagesIdsBinding);
            _socket.Connect(ip, port);
            _stream = new NetworkStream(_socket);

            _messageAcknowledgementManager =
                new MessageAcknowledgementManager(_clientMessageManager, messagesIdsBinding);
            _messagesIdsBinding = messagesIdsBinding;

            _clientMessageManager.AddRawMessage(MessageType.Connect,
                JsonConvert.SerializeObject(messagesIdsBinding.GetMessagesIds()), 0, 0, 0);
        }

        public void Send(IMessage message)
        {
            if (!_isConnected) SpinWait.SpinUntil(() => _isConnected);

            _clientMessageManager.AddMessage(message, _messagesIdsBinding.GetMessageIdByType(message.GetType()));
        }

        public void Subscribe<T>(Action<T> action) where T : IMessage
        {
            if (!_isConnected) SpinWait.SpinUntil(() => _isConnected);

            var messageClassId = _messagesIdsBinding.GetMessageIdByType(typeof(T));

            if (_subscribers.ContainsKey(messageClassId))
            {
                _subscribers[messageClassId] += message => action((T)message);
                return;
            }

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
                        var sessionId = BitConverter.ToInt32(message);

                        _clientMessageManager.SessionId = sessionId;
                        _isConnected = true;

                        Console.WriteLine($"Connected as {sessionId} session");

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
                            _subscribers[message.MessageClassId](message.Content);
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