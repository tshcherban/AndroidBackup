using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Net.Sockets;
using System.Security.Cryptography;
using System.Threading;
using System.Threading.Tasks;

namespace FileSync.Common
{
    public static class NetworkHelper
    {
        const int ChunkSize = 1024*1024;

        public static async Task<CommandHeader> ReadCommandHeader(Stream stream, CancellationToken? token = null)
        {
            var commandHeaderBytes = await ReadBytes(stream, Commands.PreambleLength + Commands.CommandLength, token);
            var payloadLengthBytes = await ReadBytes(stream, sizeof(int), token);
            var payloadLength = BitConverter.ToInt32(payloadLengthBytes, 0);

            var ret = new CommandHeader(commandHeaderBytes[Commands.PreambleLength])
            {
                PayloadLength = payloadLength,
            };

            return ret;
        }

        public static async Task WriteCommandHeader(Stream stream, byte command, int payloadLength = 0)
        {
            const int length = Commands.PreambleLength + Commands.CommandLength;
            var buffer = new byte[length];
            buffer[Commands.PreambleLength] = command;
            await stream.WriteAsync(buffer, 0, length);
            var bytesLength = BitConverter.GetBytes(payloadLength);
            await stream.WriteAsync(bytesLength, 0, bytesLength.Length);
            await stream.FlushAsync();
        }

        public static async Task Write<T>(Stream stream, T data)
        {
            var dataBytes = Serializer.Serialize(data);
            await WriteBytes(stream, dataBytes);
        }

        public static async Task<T> Read<T>(Stream stream, int length)
        {
            var dataBytes = await ReadBytes(stream, length);
            var data = Serializer.Deserialize<T>(dataBytes);
            return data;
        }

        public static async Task WriteBytes(Stream stream, byte[] data)
        {
            await stream.WriteAsync(data, 0, data.Length);
            await stream.FlushAsync();
        }

        public static async Task<byte[]> ReadBytes(Stream stream, int count, CancellationToken? token = null)
        {
            var buffer = new byte[count];
            var totalRead = 0;
            do
            {
                var bytesRead = await stream.ReadAsync(buffer, totalRead, count, token ?? CancellationToken.None);
                if (bytesRead == 0)
                {
                    break;
                }

                count -= bytesRead;
                totalRead += bytesRead;
            } while (count > 0);

            return buffer;
        }

        public static async Task<string> ReadToFile(Stream networkStream, string filePath, long fileLength)
        {
            string log = null;

            var readSize = (int) Math.Min(fileLength, ChunkSize);
            //var buffer = new byte[readSize];
            var bytesLeft = fileLength;

            var folder = Path.GetDirectoryName(filePath);
            if (folder == null)
            {
                throw new InvalidOperationException($"Failed to get file '{filePath}' folder");
            }

            if (!Directory.Exists(folder))
            {
                Directory.CreateDirectory(folder);
            }

            if (fileLength == 0)
            {
                using (var fileStream = File.Create(filePath))
                {
                    fileStream.Close();
                }

                using (HashAlgorithm alg = new MurmurHash3UnsafeProvider())
                {
                    var buffer = new byte[readSize];
                    return alg.ComputeHash(buffer).ToHashString();
                }
            }

            const int buffersCount = 2;

            var producerBuffers = new AsyncQueue<WorkerBuffer>();
            var consumerBuffers = new AsyncQueue<WorkerBuffer>();

            var bfs = Enumerable.Range(0, buffersCount).Select(_ => new WorkerBuffer{Data = new byte[readSize]}).ToList();
            producerBuffers.EnqueueRange(bfs);

            var items = new ConcurrentBag<Item>();

            items.Add(new Item
            {
                time = 0,
                netw = 0,
                hash = 0,
                file = 0,
            });

            using (HashAlgorithm alg = new MurmurHash3UnsafeProvider())
            {
                using (var fileStream = File.Create(filePath))
                {
                    var sw = Stopwatch.StartNew();

                    var producerTask = Task.Run(async () =>
                    {
                        var i = 0;
                        do
                        {
                            var buffer = await producerBuffers.DequeueAsync();

                            items.Add(new Item
                            {
                                time = sw.Elapsed.TotalMilliseconds,
                                netw = 1,
                            });

                            buffer.DataLength = await networkStream.ReadAsync(buffer.Data, 0, readSize);

                            items.Add(new Item
                            {
                                time = sw.Elapsed.TotalMilliseconds,
                                netw = 0,
                            });

                            if (buffer.DataLength == 0)
                            {
                                break;
                            }

                            bytesLeft -= buffer.DataLength;
                            readSize = (int) Math.Min(bytesLeft, ChunkSize);

                            buffer.Id = ++i;
                            buffer.IsFinal = bytesLeft == 0;

                            consumerBuffers.Enqueue(buffer);
                        } while (bytesLeft > 0);
                    });

                    var consumersTask = Task.Run(async () =>
                    {
                        var exit = false;
                        do
                        {
                            var buffer = await consumerBuffers.DequeueAsync();

                            items.Add(new Item
                            {
                                time = sw.Elapsed.TotalMilliseconds,
                                hash = 1,
                                file = 1,
                            });

                            var fileWriteTask = fileStream.WriteAsync(buffer.Data, 0, buffer.DataLength)
                                .ContinueWith(_ => items.Add(new Item
                                {
                                    time = sw.Elapsed.TotalMilliseconds,
                                    file = 0,
                                }));

                            alg.TransformBlock(buffer.Data, 0, buffer.DataLength, null, 0);

                            items.Add(new Item
                            {
                                time = sw.Elapsed.TotalMilliseconds,
                                hash = 0,
                            });

                            await fileWriteTask;

                            if (buffer.IsFinal)
                            {
                                exit = true;
                            }
                            else
                            {
                                buffer.Id = buffer.Id * -1;
                                producerBuffers.Enqueue(buffer);
                            }
                        } while (!exit);
                    });

                    await Task.WhenAll(producerTask, consumersTask);

                    items.Add(new Item
                    {
                        time = sw.Elapsed.TotalMilliseconds,
                        netw = 0,
                        hash = 0,
                        file = 0,
                    });

                    await fileStream.FlushAsync();

                    fileStream.Close();

                    //return alg.Hash.ToHashString();

                    var empty = "*";
                    log = string.Join("\r\n", items
                        .OrderBy(x => x.time)
                        .Select(x => $"{x.time:F2}\t" +
                                     $"{x.netw?.ToString() ?? empty}\t" +
                                     $"{x.hash?.ToString() ?? empty}\t" +
                                     $"{x.file?.ToString() ?? empty}"));

                    return log;
                }
            }
        }

        private class Item
        {
            public double time { get; set; }

            public int? netw { get; set; }

            public int? hash { get; set; }

            public int? file { get; set; }
        }

        private class WorkerBuffer
        {
            public int Id { get; set; }

            public byte[] Data { get; set; }

            public bool IsFinal { get; set; }
            
            public int DataLength { get; set; }
        }

        public static async Task WriteFromFile(NetworkStream networkStream, string filePath)
        {
            using (var fileStream = File.OpenRead(filePath))
            {
                var readSize = (int) Math.Min(fileStream.Length, ChunkSize);
                var buffer = new byte[readSize];
                var bytesLeft = fileStream.Length;

                do
                {
                    var bytesRead = await fileStream.ReadAsync(buffer, 0, readSize);
                    if (bytesRead == 0)
                        break;

                    await networkStream.WriteAsync(buffer, 0, bytesRead);

                    bytesLeft -= bytesRead;
                    readSize = (int) Math.Min(bytesLeft, ChunkSize);
                } while (bytesLeft > 0);

                await networkStream.FlushAsync();
            }
        }
    }

    public class AsyncQueue<T>
    {
        private readonly SemaphoreSlim _sem;
        private readonly ConcurrentQueue<T> _que;

        public AsyncQueue()
        {
            _sem = new SemaphoreSlim(0);
            _que = new ConcurrentQueue<T>();
        }

        public void Enqueue(T item)
        {
            _que.Enqueue(item);
            _sem.Release();
        }

        public void EnqueueRange(IEnumerable<T> source)
        {
            var n = 0;
            foreach (var item in source)
            {
                _que.Enqueue(item);
                n++;
            }
            _sem.Release(n);
        }

        public async Task<T> DequeueAsync(CancellationToken cancellationToken = default(CancellationToken))
        {
            for (; ; )
            {
                await _sem.WaitAsync(cancellationToken);

                if (_que.TryDequeue(out T item))
                {
                    return item;
                }
            }
        }
    }

    /*
     
    public static class NetworkHelper
    {
        const int chunkSize = 1 * 1024 * 1024;

        public static async Task<CommandHeader> ReadCommandHeader(Stream stream, CancellationToken? token = null)
        {
            var commandHeaderBytes = await ReadBytes(stream, Commands.PreambleLength + Commands.CommandLength, token);
            var payloadLengthBytes = await ReadBytes(stream, sizeof(int), token);
            var payloadLength = BitConverter.ToInt32(payloadLengthBytes, 0);

            var ret = new CommandHeader(commandHeaderBytes[Commands.PreambleLength])
            {
                PayloadLength = payloadLength,
            };

            return ret;
        }

        public static async Task WriteCommandHeader(Stream stream, byte command, int payloadLength = 0)
        {
            const int length = Commands.PreambleLength + Commands.CommandLength;
            var buffer = new byte[length];
            buffer[Commands.PreambleLength] = command;
            await stream.WriteAsync(buffer, 0, length);
            var bytesLength = BitConverter.GetBytes(payloadLength);
            await stream.WriteAsync(bytesLength, 0, bytesLength.Length);
            await stream.FlushAsync();
        }

        public static async Task Write<T>(Stream stream, T data)
        {
            var dataBytes = Serializer.Serialize(data);
            await WriteBytes(stream, dataBytes);
        }

        public static async Task<T> Read<T>(Stream stream, int length)
        {
            var dataBytes = await ReadBytes(stream, length);
            var data = Serializer.Deserialize<T>(dataBytes);
            return data;
        }

        public static async Task WriteBytes(Stream stream, byte[] data)
        {
            await stream.WriteAsync(data, 0, data.Length);
            await stream.FlushAsync();
        }

        public static async Task<byte[]> ReadBytes(Stream stream, int count, CancellationToken? token = null)
        {
            var buffer = new byte[count];
            var totalRead = 0;
            do
            {
                var bytesRead = await stream.ReadAsync(buffer, totalRead, count, token ?? CancellationToken.None);
                if (bytesRead == 0)
                {
                    break;
                }

                count -= bytesRead;
                totalRead += bytesRead;
            } while (count > 0);

            return buffer;
        }

        public static async Task<string> ReadToFile(Stream networkStream, string filePath, long fileLength)
        {
            string log = null;

            var readSize = (int) Math.Min(fileLength, chunkSize);
            var buffer = new byte[readSize];
            var bytesLeft = fileLength;

            var folder = Path.GetDirectoryName(filePath);
            if (folder == null)
            {
                throw new InvalidOperationException($"Failed to get file '{filePath}' folder");
            }

            if (!Directory.Exists(folder))
            {
                Directory.CreateDirectory(folder);
            }

            if (fileLength == 0)
            {
                using (var fileStream = File.Create(filePath))
                {
                    fileStream.Close();
                }

                using (HashAlgorithm alg = new MurmurHash3UnsafeProvider())
                {
                    return alg.ComputeHash(buffer).ToHashString();
                }
            }

            using (HashAlgorithm alg = new MurmurHash3UnsafeProvider())
            {
                //const FileOptions fileFlagNoBuffering = (FileOptions)0x20000000;
                //const FileOptions fileOptions = fileFlagNoBuffering | FileOptions.SequentialScan;

                const int chunkSize = 1 * 1024 * 1024;

                var readBufferSize = chunkSize;
                readBufferSize += ((readBufferSize + 1023) & ~1023) - readBufferSize;

                using (var fileStream = new FileStream(filePath, FileMode.CreateNew, FileAccess.Write, FileShare.None, readBufferSize, FileOptions.WriteThrough))
                //using (var fileStream = File.Create(filePath))
                {
                    var sw = Stopwatch.StartNew();

                    do
                    {
                        var networkReadStart = sw.Elapsed.TotalMilliseconds;

                        var bytesRead = await networkStream.ReadAsync(buffer, 0, readSize);
                        if (bytesRead == 0)
                        {
                            break;
                        }

                        var networkReadEnd = sw.Elapsed.TotalMilliseconds;
                        var hashStart = networkReadEnd;

                        alg.TransformBlock(buffer, 0, bytesRead, null, 0);

                        var hashEnd = sw.Elapsed.TotalMilliseconds;
                        var fileWriteStart = hashEnd;

                        await fileStream.WriteAsync(buffer, 0, bytesRead);

                        var fileWriteEnd = sw.Elapsed.TotalMilliseconds;

                        bytesLeft -= bytesRead;
                        readSize = (int) Math.Min(bytesLeft, chunkSize);

                        log += $"{networkReadStart:F2}\t1\t0\t0\r\n";
                        log += $"{networkReadEnd:F2}\t0\t0\t0\r\n";

                        log += $"{hashStart:F2}\t0\t1\t0\r\n";
                        log += $"{hashEnd:F2}\t0\t0\t0\r\n";

                        log += $"{fileWriteStart:F2}\t0\t0\t1\r\n";
                        log += $"{fileWriteEnd:F2}\t0\t0\t0\r\n";
                    } while (bytesLeft > 0);

                    await fileStream.FlushAsync();

                    fileStream.Close();

                    alg.TransformFinalBlock(buffer, 0, 0);

                    return alg.Hash.ToHashString();
                    //return log;
                }
            }
        }

        public static async Task WriteFromFile(NetworkStream networkStream, string filePath)
        {
            using (var fileStream = File.OpenRead(filePath))
            {
                var readSize = (int) Math.Min(fileStream.Length, chunkSize);
                var buffer = new byte[readSize];
                var bytesLeft = fileStream.Length;

                do
                {
                    var bytesRead = await fileStream.ReadAsync(buffer, 0, readSize);
                    if (bytesRead == 0)
                        break;

                    await networkStream.WriteAsync(buffer, 0, bytesRead);

                    bytesLeft -= bytesRead;
                    readSize = (int) Math.Min(bytesLeft, chunkSize);
                } while (bytesLeft > 0);

                await networkStream.FlushAsync();
            }
        }
    }

     */
}