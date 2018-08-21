using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Net;
using System.Net.Sockets;
using System.Security.Cryptography;
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
            Task.Run(async () =>
            {
                var tcpListener = TcpListener.Create(Port + 1);
                tcpListener.Start();
                while (!_stop)
                {
                    var tcpClient = await tcpListener.AcceptTcpClientAsync();
                    Console.WriteLine($"Client1 has connected {tcpClient.Client.RemoteEndPoint}");
                    var task = StartHandleConnectionAsync1(tcpClient);
                    // if already faulted, re-throw any error on the calling context
                    if (task.IsFaulted)
                    {
                        task.Wait();
                    }
                }
            });

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

        private async Task StartHandleConnectionAsync1(TcpClient tcpClient)
        {
            using (NetworkStream ns = tcpClient.GetStream())
            {
                var fname = @"C:\shcherban\stest\file";
                if (File.Exists(fname))
                {
                    File.Delete(fname);
                }

                await NetworkHelper.ReadToFile(ns, fname, 131880010);
            }
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
            //const string path = @"H:\SyncTest\Dst";
            const string path = @"D:\taras\stest";

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

            var fs = new FileInfo(@"C:\shcherban\shcherban.7z");

            //var h1 = TestHash(xxHash64Algo.Create(), fs.FullName, (int) fs.Length, BufferSizeMib * 1024 * 1024).Result;
            //var h2 = TestHash(xxHash64Algo.Create(), fs.FullName, (int) fs.Length, 133202).Result;

            var sww = Stopwatch.StartNew();
            //using (var ff = fs.OpenRead())
            const FileOptions fileFlagNoBuffering = (FileOptions)0x20000000;
            const FileOptions fileOptions = fileFlagNoBuffering | FileOptions.SequentialScan;

            const int chunkSize = BufferSizeMib * 1024 * 1024;

            var readBufferSize = chunkSize;
            readBufferSize += ((readBufferSize + 1023) & ~1023) - readBufferSize;

            using (HashAlgorithm hashAlgorithm = SHA1.Create())
            using (var ff = new FileStream(fs.FullName, FileMode.Open, FileAccess.Read, FileShare.Read, readBufferSize, fileOptions))
            {
                var hash2 = XxHash64New.ComputeHash(ff, BufferSizeMib*1024*1024);
            }
            sww.Stop();

            Console.WriteLine($"***** {sww.Elapsed.TotalMilliseconds:F2} ms (speed - {(fs.Length / 1024.0m / 1024.0m) / (decimal) sww.Elapsed.TotalSeconds:F2} mb/s)");

            //Console.WriteLine($"*****RESULT***** {h1 == h2}");

            Console.ReadLine();

            return;


            /*
             using (HashAlgorithm h = new MurmurHash3UnsafeProvider())
            //using (HashAlgorithm h = SHA1.Create())
            using (var f = File.OpenRead(@"D:\taras\stest\ghh.mp4"))
            {
                const int le = 133810;
                var bf = new byte[le];
                var left = f.Length;
                for (; left > 0; )
                {
                    
                    var toRead = (int)Math.Min(le, left);
                    var read = f.Read(bf, 0, toRead);
                    if (read == 0)
                        throw null;

                    left -= read;
                    h.TransformBlock(bf, 0, read, null, -1);
                }
                var hh0 = h.TransformFinalBlock(bf, 0, 0).ToHashString();
                var hh1 = h.Hash.ToHashString();
            }

            return;
             */
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

        private const int BufferSizeMib = 16;

        private static async Task<string> TestHash(HashAlgorithm alg, string fname, int length, int chunkSize)
        {
            var sw = Stopwatch.StartNew();

            using (alg)
            {
                using (var fileStream = File.OpenRead(fname))
                {
                    var readSize = Math.Min(length, chunkSize);
                    var buffer = new byte[readSize];
                    var bytesLeft = length;

                    do
                    {
                        var bytesRead = await fileStream.ReadAsync(buffer, 0, readSize);
                        if (bytesRead == 0)
                        {
                            break;
                        }

                        alg.TransformBlock(buffer, 0, bytesRead, null, 0);

                        bytesLeft -= bytesRead;
                        readSize = (int) Math.Min(bytesLeft, chunkSize);
                    } while (bytesLeft > 0);

                    fileStream.Close();

                    alg.TransformFinalBlock(buffer, 0, 0);

                    sw.Stop();

                    Console.WriteLine($"***** {alg.GetType().Name} {sw.Elapsed.TotalMilliseconds:F2} ms (buffer - {chunkSize/1024m:F2} kbytes, speed - {(length / 1024.0m / 1024.0m) / (decimal) sw.Elapsed.TotalSeconds:F2} mb/s)");

                    return alg.Hash.ToHashString();
                }
            }
        }

        private async void Discover()
        {
            using (var server = new UdpClient(8888))
            {
                var responseData = Encoding.ASCII.GetBytes($"port: {Port}");

                while (!_stop)
                {
                    try
                    {
                        var clientRequestData = await server.ReceiveAsync();// (ref clientEp);
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
        }
    }
}