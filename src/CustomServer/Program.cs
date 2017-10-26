using System;
using System.Diagnostics;
using System.Net;
using System.Net.Sockets;
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

            var s = new TcpListener(new IPEndPoint(IPAddress.Parse("127.0.0.1"), 9212));
            s.Start();
            var ipEndPoint = new IPEndPoint(IPAddress.Parse("127.0.0.1"), 9211);
            var host = new TcpHost(ipEndPoint, logger, stats);
            var syncFileService = new TwoWaySyncService(s);
            syncFileService.Log += WriteLine;
            host.AddService<ITwoWaySyncService>(syncFileService);
            host.Open();

            while (Console.ReadKey().Key != ConsoleKey.Enter) ;
            syncFileService.Log -= WriteLine;
            host.Dispose();
        }

        private static void WriteLine(string obj)
        {
            Console.WriteLine(obj);
            Trace.WriteLine(obj);
        }
    }
}