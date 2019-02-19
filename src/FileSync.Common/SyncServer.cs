using System;
using System.Collections.Generic;
using System.Net.Sockets;
using System.Threading.Tasks;

namespace FileSync.Common
{
    public class SyncServer
    {
        private readonly int _port;
        private readonly string _syncDir;
        private readonly Guid _id;
        private readonly object _lock = new object(); // sync lock 
        private readonly List<Task> _connections = new List<Task>(); // pending connections

        private bool _stop;

        private TcpListener _tcpListener;

        private Task _listenerTask;

        public event Action<string> Msg;

        public SyncServer(int port, string syncDir, Guid id)
        {
            _port = port;
            _syncDir = syncDir;
            _id = id;
        }

        public void Stop()
        {
            try
            {
                Msg?.Invoke("Stopping main loop");

                _stop = true;

                Task[] connections;

                lock (_lock)
                {
                    connections = _connections.ToArray();
                }

                if (connections.Length > 0)
                {
                    Msg?.Invoke("Waiting for clients to complete...");

                    Task.WaitAll(connections, TimeSpan.FromSeconds(30));
                }

                Msg?.Invoke("Stopping listener");

                _tcpListener?.Stop();

                _listenerTask.Wait();
            }
            catch (Exception e)
            {
                Msg?.Invoke(e.ToString());
            }
        }

        // The core server task
        public void Start()
        {
            _listenerTask = Task.Run(async () =>
            {
                _tcpListener = TcpListener.Create(_port);
                _tcpListener.Start();
                while (!_stop)
                {
                    TcpClient tcpClient;

                    try
                    {
                        tcpClient = await _tcpListener.AcceptTcpClientAsync();
                    }
                    catch (ObjectDisposedException) when (_stop)
                    {
                        return;
                    }

                    Msg?.Invoke($"Client has connected {tcpClient.Client.RemoteEndPoint}");
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
                Msg?.Invoke(ex.ToString());
            }
            finally
            {
                // remove pending task
                lock (_lock)
                {
                    _connections.Remove(connectionTask);
                }

                try
                {
                    tcpClient.Dispose();
                }
                catch (Exception e)
                {
                    Console.WriteLine(e);
                }

                Msg?.Invoke("Client has disconnected");
            }
        }

        // Handle new connection
        private async Task HandleConnectionAsync(TcpClient tcpClient)
        {
            await Task.Yield();

            using (var clientHandler = new TwoWaySyncClientHandler(tcpClient, _syncDir, _id))
            {
                clientHandler.Msg += ClientHandlerOnMsg;
                await clientHandler.Process();
            }
        }

        private void ClientHandlerOnMsg(string msg)
        {
            Msg?.Invoke(msg);
        }
    }
}