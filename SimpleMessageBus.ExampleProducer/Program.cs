using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using SimpleMessageBus.Client;

namespace SimpleMessageBus.ExampleProducer
{
    public static class Program
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

            var tasks = new List<Task>();

            // var idCounter1 = -1;
            // var client1 = new TcpMessageBusClient(ip, 8888);
            // client1.AddBinding(typeof(PersonMessage), 1);
            // client1.Subscribe((PersonMessage personMessage) =>
            // {
            //     // Console.WriteLine($"[Client1]: {personMessage.Id}");
            //     if (personMessage.Id != ++idCounter1)
            //         throw new Exception($"[1] Wrong order. Must be: {idCounter1} is {personMessage.Id}");
            // });
            // tasks.Add(Task.Run(() => client1.StartAsync()));

            var idCounter2 = -1;
            var client2 = new TcpMessageBusClient(ip, port);
            client2.AddBinding(typeof(PersonMessage), 1);
            client2.Subscribe((PersonMessage personMessage) =>
            {
                // Console.WriteLine($"[Client2]: {personMessage.Id}");
                if (personMessage.Id != ++idCounter2)
                     throw new Exception($"[2] Wrong order. Must be: {idCounter2} is {personMessage.Id}");
            });
            tasks.Add(Task.Run(() => client2.StartAsync()));

            tasks.Add(Task.Run(async () =>
            {
                // Todo, should be replaces by IsConnected flag in client
                await Task.Delay(2000);

                for (var i = 0; i < 50_000_000; i++)
                {
                    client2.Send(new PersonMessage
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