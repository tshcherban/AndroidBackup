using System;
using System.IO;
using System.Net.Sockets;
using System.Threading;
using System.Threading.Tasks;

namespace FileSync.Common
{
    public static class CommandHelper
    {
        public static async Task WriteCommandResponse<T>(NetworkStream stream, byte command, T data)
        {
            var responseBytes = Serializer.Serialize(data);
            var length = responseBytes.Length;
            await NetworkHelper.WriteCommandHeader(stream, command, length);
            await NetworkHelper.WriteBytes(stream, responseBytes);
        }
    }

    public static class NetworkHelper
    {
        private const FileOptions FileFlagNoBuffering = (FileOptions) 0x20000000;
        private const FileOptions FileOptions = FileFlagNoBuffering | System.IO.FileOptions.SequentialScan;

        private const int ChunkSize = 4 * 1024 * 1024;
        private const int ReadBufferSize = ChunkSize + ((ChunkSize + 1023) & ~1023) - ChunkSize;

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

        public static async Task WriteRequest<T>(Stream stream, T request)
            where T : IRequest
        {
            var data = Serializer.Serialize(request);
            await WriteCommandHeader(stream, request.Command, data.Length);
            await WriteBytes(stream, data);
        }

        public static async Task<ServerResponse> SendReceive(Stream stream, IRequest request)
        {
            var data = Serializer.Serialize(request);
            await WriteCommandHeader(stream, request.Command, data.Length);
            await WriteBytes(stream, data);

            var cmdHeader = await ReadCommandHeader(stream);
            if (cmdHeader.Command != request.Command)
                return new ServerResponse {ErrorMsg = "Wrong command received"};

            if (cmdHeader.PayloadLength == 0)
                return new ServerResponse {ErrorMsg = "No data received"};

            var responseBytes = await ReadBytes(stream, cmdHeader.PayloadLength);
            var response = Serializer.Deserialize<ServerResponse>(responseBytes);

            return response;
        }

        public static async Task<ServerResponseWithData<TResp>> SendReceive<TResp>(Stream stream, IRequestWithResponse<TResp> request)
        {
            var data = Serializer.Serialize(request);
            await WriteCommandHeader(stream, request.Command, data.Length);
            await WriteBytes(stream, data);

            var cmdHeader = await ReadCommandHeader(stream);
            if (cmdHeader.Command != request.Command)
                return new ServerResponseWithData<TResp> {ErrorMsg = "Wrong command received"};

            if (cmdHeader.PayloadLength == 0)
                return new ServerResponseWithData<TResp> {ErrorMsg = "No data received"};

            var responseBytes = await ReadBytes(stream, cmdHeader.PayloadLength);
            var response = Serializer.Deserialize<ServerResponseWithData<TResp>>(responseBytes);

            return response;
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

        public static async Task<byte[]> ReadToFileAndHashAsync(Stream networkStream, string filePath, long fileLength)
        {
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
                using (File.Create(filePath)) ;

                return XxHash64Callback.EmptyHash;
            }

            using (var fileStream = new FileStream(filePath, FileMode.CreateNew, FileAccess.Write, FileShare.None, ReadBufferSize, FileOptions.WriteThrough | FileOptions.Asynchronous))
            {
                async Task WriteToFile(byte[] bytes, int length)
                {
                    await fileStream.WriteAsync(bytes, 0, length);
                }

                return await XxHash64Callback.ComputeHash(networkStream, ChunkSize, fileLength, WriteToFile);
            }
        }

        public static async Task<byte[]> WriteFromFileAndHashAsync(NetworkStream destinationStream, string filePath, int fileLength)
        {
            if (fileLength == 0)
                return XxHash64Callback.EmptyHash;

            async Task WriteToNetwork(byte[] bytes, int length)
            {
                await destinationStream.WriteAsync(bytes, 0, length);
            }

            using (var fileStream = new FileStream(filePath, FileMode.Open, FileAccess.Read, FileShare.Read, ReadBufferSize, FileOptions))
            using (var bufferedStream = new BufferedStream(fileStream, ReadBufferSize))
            {
                return await XxHash64Callback.ComputeHash(bufferedStream, ChunkSize, fileLength, WriteToNetwork);
            }
        }

        public static async Task<byte[]> HashFileAsync(string filePath)
        {
            return await HashFileAsync(new FileInfo(filePath));
        }

        public static async Task<byte[]> HashFileAsync(FileInfo inf)
        {
            if (inf.Length == 0)
                return XxHash64Callback.EmptyHash;

            using (var fileStream = new FileStream(inf.FullName, FileMode.Open, FileAccess.Read, FileShare.Read, ReadBufferSize, FileOptions))
            using (var bufferedStream = new BufferedStream(fileStream, ReadBufferSize))
            {
                return await XxHash64Callback.ComputeHash(bufferedStream, ChunkSize, inf.Length, (bytes, i) => Task.CompletedTask);
            }
        }
    }
}