using System;
using FileSync.Common;

namespace FileSync.Server
{
    public class Program
    {
        private const int Port = 9211;

        //const string Path = @"C:\shcherban\stest";
        //const string Path = @"H:\SyncTest\Dst";
        private const string Path = @"D:\taras\stest";

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

            var server = new SyncServer(Port, Path);

            server.Msg += ClientHandlerOnMsg;

            server.Start();

            Console.WriteLine("Listening. Press return to quit");

            while (Console.ReadKey().Key != ConsoleKey.Enter) ;

            server.Stop();
        }

        /*private static async void Discover()
        {
            using (var server = new UdpClient(8888))
            {
                var responseData = Encoding.ASCII.GetBytes($"port: {Port}");

                while (!_stop)
                {
                    try
                    {
                        var clientRequestData = await server.ReceiveAsync(); // (ref clientEp);
                        var clientRequest = Encoding.ASCII.GetString(clientRequestData.Buffer);

                        Console.WriteLine("Request from {0}, sending discover response", clientRequestData.RemoteEndPoint.Address);
                        await server.SendAsync(responseData, responseData.Length, clientRequestData.RemoteEndPoint);
                    }
                    catch (Exception e)
                    {
                        Console.WriteLine($"Failed on discovery: {e}");
                    }
                }
            }
        }*/
    }
}