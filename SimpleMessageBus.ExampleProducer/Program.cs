using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using SimpleMessageBus.Abstractions;
using SimpleMessageBus.Client;

namespace SimpleMessageBus.ExampleProducer
{
    public class Program
    {
        public static async Task Main()
        {
            await Produce();
        }

        private static async Task Produce()
        {
            const string ip = "127.0.0.1";
            const int port = 8888;

            Console.WriteLine($"Producer is running on {ip}:{port}");

            var client = new TcpMessageBusClient(ip, port);
            client.AddBinding(typeof(PersonMessage), 1);

            var tasks = new List<Task>();

            // var client1 = new TcpMessageBusClient(ip, 8888);
            // client1.Subscribe((PersonMessage personMessage) =>
            // {
            //     Console.WriteLine($"[consumer1] Id: {personMessage.Id}");
            // });
            // tasks.Add(Task.Run(() => client1.StartAsync()));

            var idCounter = -1;

            client.Subscribe((PersonMessage personMessage) =>
            {
                // Console.WriteLine($"Person message: {personMessage.Id}");
                // if (personMessage.Id != ++idCounter)
                //     throw new Exception($"Wrong order. Must be: {idCounter} is {personMessage.Id}");
            });

            tasks.Add(Task.Run(() => client.StartAsync()));
            tasks.Add(Task.Run(() =>
            {
                for (var i = 0; i < 50_000_000; i++)
                {
                    client.Send(new PersonMessage
                    {
                        Id = i,
                        Name = $"name{i}\r\n"
                    });
                }

                Console.WriteLine("END OF MESSAGES");
            }));

            await Task.WhenAll(tasks);
        }
    }
}