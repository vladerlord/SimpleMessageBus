using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Threading.Tasks;
using SimpleMessageBus.ExampleAbstractions;

namespace SimpleMessageBus.ExampleProducer
{
    class Program
    {
        static async Task Main(string[] args)
        {
            Process.GetCurrentProcess().PriorityClass = ProcessPriorityClass.RealTime;
            await GetProducer();
        }

        private static async Task GetProducer()
        {
            const string ip = "127.0.0.1";
            const int port = 8888;

            Console.WriteLine($"Producer is running on {ip}:{port}");

            var client = new TcpMessageBusClient(ip, port);
            var tasks = new List<Task>();

            tasks.Add(Task.Run(() => client.StartAsync()));
            tasks.Add(Task.Run(() =>
            {
                for (var i = 0; i < 40_000_000; i++)
                {
                    client.Send("person.events",
                        new PersonMessage
                        {
                            Id = i,
                            Name = $"name{i}"
                        });
                }
            }));

            await Task.WhenAll(tasks);
        }
    }
}