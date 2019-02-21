using System;
using System.Net;
using FileSync.Common;

namespace FileSync.TestClient
{
    class Program
    {
        static void Main(string[] args)
        {
            var srv = new SyncServer(9211, Guid.NewGuid(), "config.json");
            srv.Msg += Console.WriteLine;
            srv.Start();

            var client = SyncClientFactory.GetTwoWay(new IPEndPoint(IPAddress.Parse("127.0.0.1"), 9211), @"D:\taras\stest\client", @"D:\taras\stest\client\.sync", Guid.Empty, Guid.Empty);
            client.Log += Console.WriteLine;
            client.Sync().Wait();

            Console.WriteLine("Done");
            Console.ReadKey();

            srv.Stop();
        }
    }
}