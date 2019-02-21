using System;
using System.Collections.Generic;
using System.IO;
using System.Runtime.Serialization.Formatters.Binary;
using System.Threading.Tasks;

namespace FileSync.Common
{
    public static class Commands
    {
        public const int CommandLength = 1;

        public const int PreambleLength = 4;


        public const byte GetSessionCmd = 1;

        public const byte GetSyncListCmd = 2;

        public const byte GetFileCmd = 3;

        public const byte SendFileCmd = 4;

        public const byte FinishSessionCmd = 5;

        public const byte DisconnectCmd = 6;

        public const byte GetIdCmd = 7;

        public const byte RegisterClientCmd = 8;

        public const byte GetClientEndpointsCmd = 9;
    }

    public class CommandHeader
    {
        public CommandHeader(byte commandId)
        {
            Command = commandId;
        }

        public byte Command { get; }

        public int PayloadLength { get; set; }
    }

    public interface IRequest
    {
        byte Command { get; }
    }

    public interface IRequestWithResponse<T> : IRequest
    {
    }

    [Serializable]
    public abstract class Request : IRequest
    {
        public async Task<ServerResponse> Send(Stream stream)
        {
            return await NetworkHelper.SendReceive(stream, this);
        }

        public abstract byte Command { get; }
    }

    [Serializable]
    public abstract class RequestWithResponse<T> : IRequestWithResponse<T>
    {
        public async Task<ServerResponseWithData<T>> Send(Stream stream)
        {
            return await NetworkHelper.SendReceive(stream, this);
        }

        public abstract byte Command { get; }
    }

    [Serializable]
    public class GetSessionRequest : RequestWithResponse<Guid>
    {
        public override byte Command => Commands.GetSessionCmd;

        public Guid ClientId { get; set; }

        public Guid EndpointId { get; set; }
    }

    [Serializable]
    public class GetSyncListRequest : RequestWithResponse<SyncInfo>
    {
        public Guid SessionId { get; set; }

        public List<SyncFileInfo> Files { get; set; }

        public override byte Command => Commands.GetSyncListCmd;
    }

    [Serializable]
    public class GetFileRequest : RequestWithResponse<long>
    {
        public Guid SessionId { get; set; }

        public string RelativeFilePath { get; set; }

        public override byte Command => Commands.GetFileCmd;
    }

    [Serializable]
    public class SendFileRequest : Request
    {
        public Guid SessionId { get; set; }

        public string RelativeFilePath { get; set; }

        public string HashStr { get; set; }

        public long FileLength { get; set; }

        public override byte Command => Commands.SendFileCmd;
    }

    public static class Serializer
    {
        public static byte[] Serialize<T>(T obj)
        {
            using (var ms = new MemoryStream())
            {
                var binaryFormatter = new BinaryFormatter();
                binaryFormatter.Serialize(ms, obj);
                return ms.ToArray();
            }
        }

        public static T Deserialize<T>(byte[] data)
        {
            using (var ms = new MemoryStream(data))
            {
                var binaryFormatter2 = new BinaryFormatter();
                return (T) binaryFormatter2.Deserialize(ms);
            }
        }
    }
}