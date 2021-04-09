using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using SimpleMessageBus.Abstractions;

namespace SimpleMessageBus.ExampleProducer
{
    class Program
    {
        static async Task Main(string[] args)
        {
            // Process.GetCurrentProcess().PriorityClass = ProcessPriorityClass.RealTime;
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
            var counter = 0;

            tasks.Add(Task.Run(async () =>
            {
                for (var i = 0u; i < 40_000_000; i++)
                {
                    client.AcknowledgeMessage(i);

                    if (++counter >= 6_000)
                    {
                        await Task.Delay(2);
                        counter = 0;
                    }
                }
            }));


            await Task.WhenAll(tasks);
        }
    }
}