using System;
using System.Diagnostics;
using System.IO;
using System.Net.Sockets;
using System.Security.Cryptography;
using System.Threading;
using System.Threading.Tasks;

namespace FileSync.Common
{
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
}