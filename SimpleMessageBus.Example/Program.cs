using System;
using System.Net;
using SimpleMessageBus.Server;

namespace SimpleMessageBus.Example
{
    class Program
    {
        static void Main(string[] args)
        {
            var port = 8888;
            var ipEndPoint = new IPEndPoint(IPAddress.Any, port);
            var ip = ipEndPoint.Address.ToString();
            
            var server = new TcpMessageBusServer(ip, port);
            
            Console.WriteLine($"Message bus is running on {ip}:{port}");
            
            server.StartAsync().Wait();
        }
    }
}