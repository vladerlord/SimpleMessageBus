using System;
using System.Net;
using System.Threading.Tasks;
using SimpleMessageBus.Server;

namespace SimpleMessageBus.ExampleServer
{
    internal static class Program
    {
        private static async Task Main()
        {
            var port = 8888;
            var ipEndPoint = new IPEndPoint(IPAddress.Any, port);
            var ip = ipEndPoint.Address.ToString();

            var server = new TcpMessageBusServer(ip, port);

            Console.WriteLine($"Message bus is running on {ip}:{port}");

            await server.StartAsync();
        }
    }
}