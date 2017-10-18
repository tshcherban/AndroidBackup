using System;
using Common.Protocol;

namespace CustomClient
{
    class Program
    {
        static void Main(string[] args)
        {
            var comm = new Communicator("127.0.0.1");

            while (true)
            {
                var k = Console.ReadKey();
                if (k.Key == ConsoleKey.Enter)
                    break;

                var arg = new GetFileListRequest {RequestData = DateTime.Now.ToString("G")};
                var response = comm.SendReceiveCommand(GetFileListCommand.Instance, arg);
                Console.WriteLine($"Received {response.Data}");
            }
        }
    }
}