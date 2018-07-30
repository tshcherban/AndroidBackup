using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Net.Sockets;
using System.Threading.Tasks;
using FileSync.Common;

namespace FileSync.Server
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
                    Console.WriteLine($"Client has connected {tcpClient.Client.RemoteEndPoint}");
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

                Console.WriteLine($"Client has disconnected");
            }
        }

        // Handle new connection
        private async Task HandleConnectionAsync(TcpClient tcpClient)
        {
            await Task.Yield();

            using (var clientHandler = new TwoWaySyncClientHandler(tcpClient, @"C:\shcherban\stest"))
            {
                await clientHandler.Process();
            }
        }

        public static void Main(string[] args)
        {
            if (args?.Length > 0)
            {
                Console.WriteLine("Unknown args, press any key to exit");
                Console.ReadKey();
                return;
            }

            var program = new Program();
            program.StartListener();
            Console.WriteLine("Listening. Press return to quit");

            while (Console.ReadKey().Key != ConsoleKey.Enter) ;

            program._stop = true;

            if (program._connections.Count > 0)
            {
                Console.WriteLine("Waiting for clients to complete...");

                Task.WaitAll(program._connections.ToArray(), TimeSpan.FromSeconds(30));
            }
        }

        private static void WriteLine(string obj)
        {
            Console.WriteLine(obj);
            Trace.WriteLine(obj);
        }
    }
}