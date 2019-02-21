using System;
using System.Collections.Generic;
using System.Net;
using FileSync.Common;

namespace FileSync.TestClient
{
    class Program
    {
        private const int ServerPort = 9211;

        static void Main(string[] args)
        {
            var cfg = new DummyConfig();
            cfg.Clients.Add(new RegisteredClient
            {
                Id = Guid.Empty,
                FolderEndpoints =
                {
                    new ClientFolderEndpoint
                    {
                        Id = Guid.Empty,
                        DisplayName = "f1",
                        LocalPath = @"C:\shcherban\stest\tests\server",
                    }
                }
            });

            var srv = new SyncServer(ServerPort, Guid.Empty, cfg);
            srv.Msg += Console.WriteLine;
            srv.Start();

            var ipEndPoint = new IPEndPoint(IPAddress.Parse("127.0.0.1"), ServerPort);
            var client = SyncClientFactory.GetTwoWay(ipEndPoint, @"C:\shcherban\stest\tests\client", null, Guid.Empty, Guid.Empty);
            client.Log += Console.WriteLine;
            client.Sync().Wait();

            Console.WriteLine("Done");
            Console.ReadKey();

            srv.Stop();
        }
    }

    class DummyConfig : IServerConfig
    {
        public List<RegisteredClient> Clients { get; } = new List<RegisteredClient>();

        public void Load()
        {
        }

        public void Store()
        {
        }
    }
}