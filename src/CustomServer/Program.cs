using System;
using Common.Protocol;

namespace CustomServer
{
    class Program
    {
        static void Main(string[] args)
        {
            var comm = new Communicator("127.0.0.1");
            comm.AppendReceiveSendHandler(GetFileListCommand.Instance, GetFileListHandler);
            comm.AppendReceiveSendHandler(SendFileCommand.Instance, SendFileHandler);

            while (Console.ReadKey().Key != ConsoleKey.Enter) ;
            comm.Shutdown();
        }

        private static object SendFileHandler(SendFileCommandData arg)
        {
            Console.WriteLine($"Received {arg.Data.Length} bytes");
            return "strr";
        }

        private static GetFileListResponse GetFileListHandler(GetFileListRequest arg)
        {
            Console.WriteLine($"Client requested {arg.RequestData}");

            return new GetFileListResponse
            {
                Data = DateTime.Now.ToString("G"),
            };
        }
    }
}