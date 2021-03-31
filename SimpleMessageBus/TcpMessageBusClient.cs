using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Net.Sockets;
using System.Threading.Tasks;
using SimpleMessageBus.Abstractions;

namespace SimpleMessageBus
{
    public class TcpMessageBusClient
    {
        private readonly string _ip;
        private readonly int _port;

        private readonly NetworkStream _stream;
        private readonly Socket _socket;

        private readonly List<Task> _tasks = new();
        private readonly MessageBuffer _messageBuffer = new();

        public TcpMessageBusClient(string ip, int port)
        {
            _socket = new Socket(AddressFamily.InterNetwork, SocketType.Stream, ProtocolType.Tcp);
            _socket.Connect(ip, port);
            _stream = new NetworkStream(_socket);

            _ip = ip;
            _port = port;
        }

        public void Send(string exchange, IMessage message)
        {
            _messageBuffer.AddMessage(message);
        }

        public async Task StartAsync()
        {
            try
            {
                // _tasks.Add(Task.Run(GetCpuUsageForProcess));

                _tasks.Add(Task.Run(WriteBulk));
                _tasks.Add(Task.Run(_messageBuffer.Start));

                await Task.WhenAny(_tasks);
            }
            catch (AggregateException e)
            {
                _socket.Close();
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
                    // or send as span, but without async
                    var toSend = _messageBuffer.ReadyToSendBuffer
                        .TakeMaxReadyElements()
                        .ToArray();

                    foreach (var bytes in toSend)
                    {
                        await _stream.WriteAsync(bytes);
                    }
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
    }
}