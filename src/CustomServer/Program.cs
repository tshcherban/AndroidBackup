using System;
using System.Net;
using FileSync.Common;
using ServiceWire;
using ServiceWire.TcpIp;

namespace CustomServer
{
    class Program
    {
        static void Main(string[] args)
        {
            var logger = new Logger(logLevel:LogLevel.Debug);
            var stats = new Stats();
            var host = new TcpHost(new IPEndPoint(IPAddress.Parse("127.0.0.1"), 9211), logger, stats);
            host.AddService<ISyncFileService>(new SyncSyncFileService());
            host.Open();

            while (Console.ReadKey().Key != ConsoleKey.Enter) ;
            host.Dispose();
        }
    }
}