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

        public void Send(string channel, IMessage message)
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
            try
            {
                // _tasks.Add(Task.Run(GetCpuUsageForProcess));

                _tasks.Add(Task.Run(WriteBulk));
                _tasks.Add(Task.Run(_clientMessageManager.Start));

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
                    var toSend = _clientMessageManager.ReadyToSendBuffer
                        .TakeMaxReadyElements()
                        .ToArray();
                    
//Length: 32, msg: 1??W
//                     name1440962
//31578308
//System.IO.EndOfStreamException: Attempted to read past the end of the stream.

                    foreach (var bytes in toSend)
                    {
                        // Console.WriteLine(bytes.GetString());
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