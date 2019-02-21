using System;
using System.Threading.Tasks;
using FileSync.Common;

namespace FileSync.Server
{
    public class Program
    {
        private const int Port = 9211;

        private static bool _stopping;

        private static void ClientHandlerOnMsg(string s)
        {
            Console.WriteLine(s);
        }

        public static void Main(string[] args)
        {
            if (args?.Length > 0)
            {
                Console.WriteLine("Unknown args, press any key to exit");
                Console.ReadKey();
                return;
            }

            var server = new SyncServer(Port, ServerId, "config.json");

            server.Msg += ClientHandlerOnMsg;

            server.Start();

            Console.WriteLine("Listening. Press return to quit");

            Task.Run(Discover);

            while (Console.ReadKey().Key != ConsoleKey.Enter) ;

            _stopping = true;

            server.Stop();
        }

        private static readonly Guid ServerId = Guid.ParseExact("5991611F-62F1-4B50-8546-447C04C0049E", "D");

        private static async Task Discover()
        {
            using (var server = new System.Net.Sockets.UdpClient(8888))
            {
                var responseData = System.Text.Encoding.ASCII.GetBytes($"port: {Port}|id: {ServerId:D}");

                while (!_stopping)
                {
                    try
                    {
                        var clientRequestData = await server.ReceiveAsync(); // (ref clientEp);
                        var clientRequest = System.Text.Encoding.ASCII.GetString(clientRequestData.Buffer);
                        if (clientRequest == "sync-service")
                        {
                            Console.WriteLine("Request from {0}, sending discover response", clientRequestData.RemoteEndPoint.Address);
                            await server.SendAsync(responseData, responseData.Length, clientRequestData.RemoteEndPoint);
                        }
                        else
                        {
                            Console.WriteLine("Request from {0} invalid {1}", clientRequestData.RemoteEndPoint.Address, clientRequest);
                            await server.SendAsync(responseData, responseData.Length, clientRequestData.RemoteEndPoint);
                        }
                    }
                    catch (Exception e)
                    {
                        Console.WriteLine($"Failed on discovery: {e}");
                        await Task.Delay(2000);
                    }
                }
            }
        }
    }
}