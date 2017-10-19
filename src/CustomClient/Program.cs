using System;
using System.Net;
using Common.Protocol;
using ServiceWire.TcpIp;

namespace CustomClient
{
    class Program
    {
        static void Main(string[] args)
        {

            using (var client = new TcpClient<IFileService>(new IPEndPoint(IPAddress.Parse("127.0.0.1"), 9211)))
            {
                while (true)
                {
                    var k = Console.ReadKey();
                    if (k.Key == ConsoleKey.Enter)
                        break;

                    var proxy = client.Proxy;
                    var fl = proxy.GetFileList();
                    Console.WriteLine();
                    Console.WriteLine($"Received {fl.Count}");
                }
            }
        }
    }
}