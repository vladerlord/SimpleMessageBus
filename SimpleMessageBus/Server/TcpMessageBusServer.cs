using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Net;
using System.Net.Sockets;
using System.Threading;
using System.Threading.Tasks;
using SimpleMessageBus.Utils;

namespace SimpleMessageBus.Server
{
    public class TcpMessageBusServer
    {
        private readonly TcpListener _listener;
        private readonly Dictionary<int, TcpMessageBusSession> _tcpSessions = new();
        private readonly object _tcpSessionsLock = new();
        private int _tcpSessionIdIndex;

        private readonly List<Task> _tasks = new();
        private readonly TcpMessageBusBandwidth _bandwidthInfo = new();
        private readonly ServerMessageManager _serverMessageManager;
        private readonly ServerAckManager _ackManager;

        private const int GroupSize = 100;

        public TcpMessageBusServer(string ip, int port, ServerOptions options = null)
        {
            options ??= new ServerOptions();
            ServerConfig.LoadOptions(options);

            _listener = new TcpListener(IPAddress.Parse(ip), port);
            _serverMessageManager = ServerMessageManager.Instance();
            _ackManager = ServerAckManager.Instance();
        }

        public async Task StartAsync()
        {
            Console.WriteLine("Running TCP queue message bus");

            try
            {
                _listener.Start();

                _tasks.Add(Task.Run(Timer));
                _tasks.Add(Task.Run(ListenForConnectionsAsync));
                _tasks.Add(Task.Run(PrepareSessionMessagesWorker));

                await Task.WhenAll(_tasks);
            }
            catch (Exception e)
            {
                Console.WriteLine(e);
                throw;
            }
        }

        private async Task Timer()
        {
            try
            {
                var sw = new Stopwatch();

                while (true)
                {
                    sw.Start();

                    // todo, split?
                    ShowBandwidthInfoAsync();
                    _ackManager.TimerTick();

                    await Task.Delay(1000 - sw.Elapsed.Milliseconds);
                    sw.Reset();
                }
            }
            catch (Exception e)
            {
                Console.WriteLine(e);
                throw;
            }
        }

        private async Task ListenForConnectionsAsync()
        {
            try
            {
                while (true)
                {
                    var socket = await _listener.AcceptSocketAsync();
                    var sessionId = _tcpSessionIdIndex++;
                    var session = new TcpMessageBusSession(this, sessionId, socket);

                    lock (_tcpSessionsLock)
                        _tcpSessions.Add(sessionId, session);

                    _tasks.Add(Task.Run(() => session.StartAsync()));

                    SessionCreated(sessionId);
                }
            }
            catch (Exception e)
            {
                Console.WriteLine(e);
                throw;
            }
        }

        private Task PrepareSessionMessagesWorker()
        {
            try
            {
                while (true)
                {
                    var messageClassesIds = _serverMessageManager.GetMessageClassesIds();

                    lock (_tcpSessionsLock)
                    {
                        for (var i = 0; i < messageClassesIds.Count; i++)
                        {
                            var messageClassId = messageClassesIds[i];
                            var tickCounter = 0;

                            // TODO, try to replace with autoResetEvent
                            SpinWait.SpinUntil(() =>
                                tickCounter++ == 1000 || _serverMessageManager.GetLength(messageClassId) > GroupSize);

                            var subscribedIds = _serverMessageManager.GetSubscribedSessionsIds(messageClassId);

                            if (subscribedIds == null || subscribedIds.Count == 0)
                                continue;

                            var buffer = _serverMessageManager.TakeMaxItems(messageClassId, subscribedIds);
                            Interlocked.Add(ref _bandwidthInfo.SentMessages, buffer.Length);

                            foreach (var sessionId in subscribedIds)
                            {
                                // todo, send unacked once a timeout timer
                                // because, we can send multiple times undelivered messages
                                var enumerator =
                                    _serverMessageManager.GetUndeliveredMessages(messageClassId, sessionId);

                                foreach (var undelivered in enumerator)
                                    _tcpSessions[sessionId].AddMessage(undelivered.Flatten());

                                _tcpSessions[sessionId].AddMessage(buffer.Flatten());
                            }
                        }
                    }
                }
            }
            catch (Exception e)
            {
                Console.WriteLine(e);
                throw;
            }
        }

        private void SessionCreated(int sessionId)
        {
            _ackManager.AddSessionId(sessionId);
        }

        public void Disconnect(int sessionId)
        {
            lock (_tcpSessionsLock)
            {
                _tcpSessions.Remove(sessionId);
                _serverMessageManager.RemoveSessionId(sessionId);
            }

            _ackManager.RemoveSessionId(sessionId);

            Console.WriteLine($"[] Session: {sessionId} is disconnected");
        }

        private void ShowBandwidthInfoAsync()
        {
            CollectClientsBandwidthInfo();

            Console.WriteLine($"Received: {_bandwidthInfo.ReadMessages:N0}. Send: {_bandwidthInfo.SentMessages:N0}");
            Console.WriteLine();

            _bandwidthInfo.Reset();
        }

        private void CollectClientsBandwidthInfo()
        {
            lock (_tcpSessionsLock)
            {
                foreach (var (_, client) in _tcpSessions)
                {
                    _bandwidthInfo.Add(client.BandwidthInfo);
                    client.BandwidthInfo.Reset();
                }
            }
        }
    }
}