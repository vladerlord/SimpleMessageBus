using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Net;
using System.Net.Sockets;
using System.Threading.Tasks;

namespace SimpleMessageBus.Server
{
    public class TcpMessageBusServer
    {
        private readonly TcpListener _listener;
        private readonly Dictionary<int, TcpMessageBusSession> _tcpSessions = new();
        private int _tcpSessionIdIndex;

        private readonly List<Task> _tasks = new();
        private readonly TcpMessageBusBandwidth _bandwidthInfo = new();

        public TcpMessageBusServer(string ip, int port)
        {
            _listener = new TcpListener(IPAddress.Parse(ip), port);
        }

        public async Task StartAsync()
        {
            Console.WriteLine("Running TCP queue message bus");

            try
            {
                _listener.Start();

                _tasks.Add(ListenForConnectionsAsync());
                _tasks.Add(ShowBandwidthInfoAsync());

                await Task.WhenAny(_tasks);
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

                    _tcpSessions.Add(sessionId, session);

                    _tasks.Add(session.StartAsync());
                }
            }
            catch (Exception e)
            {
                Console.WriteLine(e);
                throw;
            }
        }

        public void Disconnect(int sessionId)
        {
            _tcpSessions[sessionId].Dispose();
            Console.WriteLine($"Session: {sessionId} is disconnected");
        }

        private async Task ShowBandwidthInfoAsync()
        {
            while (true)
            {
                var startTime = DateTime.UtcNow;
                var startCpuUsage = Process.GetCurrentProcess().TotalProcessorTime;

                await Task.Delay(1000);

                CollectClientsBandwidthInfo();

                if (_bandwidthInfo.ReadMessages == 0 &&
                    _bandwidthInfo.SentMessages == 0)
                    continue;

                if (_bandwidthInfo.ReadBytes == 0 &&
                    _bandwidthInfo.SentBytes == 0)
                    continue;

                var endTime = DateTime.UtcNow;
                var endCpuUsage = Process.GetCurrentProcess().TotalProcessorTime;
                var cpuUsedMs = (endCpuUsage - startCpuUsage).TotalMilliseconds;
                var totalMsPassed = (endTime - startTime).TotalMilliseconds;
                var cpuUsageTotal = cpuUsedMs / (Environment.ProcessorCount * totalMsPassed);
                var memory = Process.GetCurrentProcess().WorkingSet64;

                Console.WriteLine($"-- Total RPS: {_bandwidthInfo.ReadMessages:N0}");
                Console.WriteLine($"-- CPU USAGE: {cpuUsageTotal * 100}");
                Console.WriteLine($"-- MEMORY USAGE: {memory}");
                Console.WriteLine();

                _bandwidthInfo.Reset();
            }
        }

        private void CollectClientsBandwidthInfo()
        {
            foreach (var (id, client) in _tcpSessions)
            {
                _bandwidthInfo.Add(client.BandwidthInfo);
                client.BandwidthInfo.Reset();
            }
        }
    }
}