using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Net;
using System.Net.Sockets;
using System.Threading.Tasks;
using FileSync.Common;

namespace CustomServer
{
    internal class Program
    {
        private readonly object _lock = new object(); // sync lock 
        private readonly List<Task> _connections = new List<Task>(); // pending connections

        private bool _stop;

        // The core server task
        private Task StartListener()
        {
            return Task.Run(async () =>
            {
                var tcpListener = TcpListener.Create(9211);
                tcpListener.Start();
                while (!_stop)
                {
                    var tcpClient = await tcpListener.AcceptTcpClientAsync();
                    Console.WriteLine("[Server] Client has connected");
                    var task = StartHandleConnectionAsync(tcpClient);
                    // if already faulted, re-throw any error on the calling context
                    if (task.IsFaulted)
                        task.Wait();
                }
            });
        }

        // Register and handle the connection
        private async Task StartHandleConnectionAsync(TcpClient tcpClient)
        {
            // start the new connection task
            var connectionTask = HandleConnectionAsync(tcpClient);

            // add it to the list of pending task 
            lock (_lock)
                _connections.Add(connectionTask);

            // catch all errors of HandleConnectionAsync
            try
            {
                await connectionTask;
                // we may be on another thread after "await"
            }
            catch (Exception ex)
            {
                // log the error
                Console.WriteLine(ex.ToString());
            }
            finally
            {
                // remove pending task
                lock (_lock)
                    _connections.Remove(connectionTask);
            }
        }

        // Handle new connection
        private async Task HandleConnectionAsync(TcpClient tcpClient)
        {
            await Task.Yield();
            // continue asynchronously on another threads

            var service = new TwoWaySyncService();
            using (var networkStream = tcpClient.GetStream())
            {
                const int commandLength = 1;
                var commandBuffer = new byte[commandLength];
                var bytesRead = await networkStream.ReadAsync(commandBuffer, 0, commandLength);
                switch (commandBuffer[0])
                {
                    case Commands.GetSession:
                        var sessionId = service.GetSession();
                        var buffer = new byte[16];
                        await networkStream.WriteAsync(sessionId.ToByteArray(), 0, 16);
                        await networkStream.FlushAsync();
                        break;
                }
            }
        }

        static class Commands
        {
            public const byte GetSession = 0x00;
        }

        private static void Main(string[] args)
        {
            var program = new Program();
            var listenerTask = program.StartListener();

            while (Console.ReadKey().Key != ConsoleKey.Enter) ;

            program._stop = true;

            listenerTask.Wait();
        }

        private static void WriteLine(string obj)
        {
            Console.WriteLine(obj);
            Trace.WriteLine(obj);
        }
    }
}