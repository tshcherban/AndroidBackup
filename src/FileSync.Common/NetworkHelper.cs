using System;
using System.IO;
using System.Net.Sockets;
using System.Threading.Tasks;

namespace FileSync.Common
{
    public static class NetworkHelper
    {
        public static async Task<CommandHeader> ReadCommandHeader(Stream stream)
        {
            var commandHeaderBytes = await ReadBytes(stream, Commands.PreambleLength + Commands.CommandLength);
            var payloadLengthBytes = await ReadBytes(stream, sizeof(int));
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

        public static async Task<byte[]> ReadBytes(Stream stream, int count)
        {
            var bytesRead = 0;
            var buffer = new byte[count];
            var bytesLeft = count;
            do
            {
                bytesRead = await stream.ReadAsync(buffer, bytesRead, bytesLeft);
                if (bytesRead == 0)
                    break;

                bytesLeft -= bytesRead;
            } while (bytesLeft > 0);

            return buffer;
        }

        public static async Task ReadToFile(Stream networkStream, string filePath, long fileLength)
        {
            const int chunkSize = 16 * 1024 * 1024;

            var readSize = (int) Math.Min(fileLength, chunkSize);
            var buffer = new byte[readSize];
            var bytesLeft = fileLength;

            var folder = Path.GetDirectoryName(filePath);
            if (!Directory.Exists(folder))
                Directory.CreateDirectory(folder);

            using (var fileStream = File.Create(filePath))
            {
                do
                {
                    var bytesRead = await networkStream.ReadAsync(buffer, 0, readSize);
                    if (bytesRead == 0)
                        break;

                    await fileStream.WriteAsync(buffer, 0, bytesRead);

                    bytesLeft -= bytesRead;
                    readSize = (int) Math.Min(bytesLeft, chunkSize);
                } while (bytesLeft > 0);

                await fileStream.FlushAsync();
                fileStream.Close();
            }
        }

        public static async Task WriteFromFile(NetworkStream networkStream, string filePath)
        {
            const int chunkSize = 16 * 1024 * 1024;

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