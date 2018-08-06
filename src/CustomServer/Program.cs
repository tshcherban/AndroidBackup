using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Threading.Tasks;
using FileSync.Common;

namespace FileSync.Server
{
    internal class Program
    {
        private const int Port = 9211;

        private readonly object _lock = new object(); // sync lock 
        private readonly List<Task> _connections = new List<Task>(); // pending connections

        private bool _stop;

        // The core server task
        private Task StartListener()
        {
            return Task.Run(async () =>
            {
                var tcpListener = TcpListener.Create(Port);
                tcpListener.Start();
                while (!_stop)
                {
                    var tcpClient = await tcpListener.AcceptTcpClientAsync();
                    Console.WriteLine($"Client has connected {tcpClient.Client.RemoteEndPoint}");
                    var task = StartHandleConnectionAsync(tcpClient);
                    // if already faulted, re-throw any error on the calling context
                    if (task.IsFaulted)
                    {
                        task.Wait();
                    }
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
            {
                _connections.Add(connectionTask);
            }

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
                {
                    _connections.Remove(connectionTask);
                }

                Console.WriteLine($"Client has disconnected");
            }
        }

        // Handle new connection
        private async Task HandleConnectionAsync(TcpClient tcpClient)
        {
            await Task.Yield();

            //const string path = @"C:\shcherban\stest";
            const string path = @"H:\SyncTest\Dst";

            using (var clientHandler = new TwoWaySyncClientHandler(tcpClient, path))
            {
                clientHandler.Msg += ClientHandlerOnMsg;
                await clientHandler.Process();
            }
        }

        private void ClientHandlerOnMsg(string s)
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

            var program = new Program();
            program.StartListener();
            Console.WriteLine("Listening. Press return to quit");

            Task.Run(() => { program.Discover(); });

            while (Console.ReadKey().Key != ConsoleKey.Enter) ;

            program._stop = true;

            if (program._connections.Count > 0)
            {
                Console.WriteLine("Waiting for clients to complete...");

                Task.WaitAll(program._connections.ToArray(), TimeSpan.FromSeconds(30));
            }
        }

        private async void Discover()
        {
            using (var server = new UdpClient(8888))
            {
                var responseData = Encoding.ASCII.GetBytes($"port:{Port}");

                while (!_stop)
                {
                    var clientRequestData = await server.ReceiveAsync();// (ref clientEp);
                    var clientRequest = Encoding.ASCII.GetString(clientRequestData.Buffer);

                    Console.WriteLine("Recived {0} from {1}, sending response", clientRequest, clientRequestData.RemoteEndPoint.Address);
                    await server.SendAsync(responseData, responseData.Length, clientRequestData.RemoteEndPoint);
                    break;
                }
            }
        }
    }
}