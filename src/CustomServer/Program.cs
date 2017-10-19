using System;
using System.Net;
using Common.Protocol;
using ServiceWire.TcpIp;

namespace CustomServer
{
    class Program
    {
        static void Main(string[] args)
        {
            var host = new TcpHost(new IPEndPoint(IPAddress.Parse("127.0.0.1"), 9211));
            host.AddService<IFileService>(new FileService());
            host.Open();

            while (Console.ReadKey().Key != ConsoleKey.Enter) ;
            host.Dispose();
        }
    }
}