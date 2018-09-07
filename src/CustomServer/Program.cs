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
        //const string path = @"C:\shcherban\stest";
        //const string path = @"H:\SyncTest\Dst";
        private const string path = @"D:\taras\stest";

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
            using (var ns = tcpClient.GetStream())
            {
                var fname = @"D:\Taras\test\file";
                if (File.Exists(fname))
                {
                    File.Delete(fname);
                }

                var fileLengthBytesCount = sizeof(long);
                var fileLengthBytes = new byte[fileLengthBytesCount];

                var read = await ns.ReadAsync(fileLengthBytes, 0, fileLengthBytesCount);
                if (read != fileLengthBytesCount)
                {
                    throw new InvalidOperationException("Invalid data length read");
                }

                var fileLength = BitConverter.ToInt64(fileLengthBytes, 0);

                var sw = Stopwatch.StartNew();

                var hash = await NetworkHelperSequential.ReadToFileAndHashAsync(ns, fname, (int) fileLength);

                sw.Stop();

                var hashBuffer = new byte[sizeof(ulong)];

                await ns.ReadAsync(hashBuffer, 0, sizeof(ulong));

                var h1 = hash.ToHashString();
                var h2 = hashBuffer.ToHashString();

                Console.WriteLine($"{h1 == h2}, {(double) fileLength / 1024d / 1024d / sw.Elapsed.TotalSeconds:F2} mb/s");

                var fs = new FileInfo(fname);

                fileLengthBytes = BitConverter.GetBytes(fs.Length);
                fileLengthBytesCount = sizeof(long);

                await ns.WriteAsync(fileLengthBytes, 0, fileLengthBytesCount);

                sw.Restart();

                hash = await NetworkHelperSequential.WriteFromFileAndHashAsync(ns, fs.FullName, (int)fs.Length);

                sw.Stop();

                await ns.WriteAsync(hash, 0, sizeof(ulong));

                Console.WriteLine($"{h1 == h2}, {(double)fileLength / 1024d / 1024d / sw.Elapsed.TotalSeconds:F2} mb/s");

                Console.WriteLine("Client1 has disconnected");
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

            //var fs = new FileInfo(@"C:\shcherban\shcherban.7z");
            //var fs = new FileInfo(@"D:\Summer Special Super Mix 2017 - Best Of Deep House Sessions Music 2017 Chill Out Mix by Drop G.mp4");
            //var fs = new FileInfo(@"D:\JetBrains.ReSharperUltimate.2017.3.2.exe");

            /*const FileOptions fileFlagNoBuffering = (FileOptions)0x20000000;
            const FileOptions fileOptions = fileFlagNoBuffering | FileOptions.SequentialScan;

            const int chunkSize = BufferSizeMib * 1024 * 1024;

            var readBufferSize = chunkSize;
            readBufferSize += ((readBufferSize + 1023) & ~1023) - readBufferSize;
            var sww = Stopwatch.StartNew();

            byte[] hash1;

            using (var f1 = new FileStream(fs.FullName, FileMode.Open, FileAccess.Read, FileShare.Read, readBufferSize, fileOptions))
            using (var ff = new BufferedStream(f1, readBufferSize))
            {
                hash1 = XxHash64Callback.ComputeHash(ff, 133479, (int)fs.Length, (buffer, length) => Task.CompletedTask).Result;
            }
            sww.Stop();

            Console.WriteLine($"{(fs.Length / 1024.0m / 1024.0m) / (decimal)sww.Elapsed.TotalSeconds:F2} mb/s)");
            sww = Stopwatch.StartNew();

            byte[] hash2;

            using (var f1 = new FileStream(fs.FullName, FileMode.Open, FileAccess.Read, FileShare.Read, readBufferSize, fileOptions))
            using (var ff = new BufferedStream(f1, readBufferSize))
            {
                hash2 = XxHash64Callback.ComputeHash(ff, BufferSizeMib * 1024 * 1024, (int)fs.Length, (buffer, length) => Task.CompletedTask).Result;
            }
            sww.Stop();

            Console.WriteLine($"{(fs.Length / 1024.0m / 1024.0m) / (decimal)sww.Elapsed.TotalSeconds:F2} mb/s)");

            Console.WriteLine(hash1.ToHashString() == hash2.ToHashString());

            //Console.WriteLine($"*****RESULT***** {h1 == h2}");

            Console.ReadLine();

            return;*/

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

        private const int BufferSizeMib = 32;

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

                    Console.WriteLine($"***** {alg.GetType().Name} {sw.Elapsed.TotalMilliseconds:F2} ms (buffer - {chunkSize / 1024m:F2} kbytes, speed - {(length / 1024.0m / 1024.0m) / (decimal) sw.Elapsed.TotalSeconds:F2} mb/s)");

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
        }
    }
}