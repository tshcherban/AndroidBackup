using System;
using Common.Protocol;

namespace CustomServer
{
    class Program
    {
        static void Main(string[] args)
        {
            var comm = new Communicator("127.0.0.1");
            comm.AppendHandler(GetFileListCommand.Instance, Handler);

            while (Console.ReadKey().Key != ConsoleKey.Enter) ;
            comm.Shutdown();
        }

        private static GetFileListResponse Handler(GetFileListRequest arg)
        {
            Console.WriteLine($"Client requested {arg.RequestData}");

            return new GetFileListResponse
            {
                Data = DateTime.Now.ToString("G"),
            };
        }
    }
}