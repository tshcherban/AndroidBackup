using System;
using System.Collections.Generic;
using System.IO;
using System.Runtime.Serialization.Formatters.Binary;

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

        protected CommandHeader()
        {
        }

        public virtual byte Command { get; }

        public int PayloadLength { get; set; }
    }

    [Serializable]
    public abstract class CommandHeaderWithData<T>
    {
        private T _data;

        protected CommandHeaderWithData()
        {
        }

        public abstract byte Command { get; }

        public int PayloadLength => Bytes.Length;

        public void SetData(T data)
        {
            Bytes = Serializer.Serialize(data);

        }

        public byte[] Bytes { get; set; }
    }

    public class GetClientEndpointsRequest : CommandHeaderWithData<Guid>
    {
        public GetClientEndpointsRequest(Guid clientId)
        {
            SetData(clientId);
        }

        public override byte Command => Commands.GetClientEndpointsCmd;
    }

    [Serializable]
    public class GetSyncListCommandData
    {
        public Guid SessionId { get; set; }

        public List<SyncFileInfo> Files { get; set; }
    }

    [Serializable]
    public class GetFileCommandData
    {
        public Guid SessionId { get; set; }

        public string RelativeFilePath { get; set; }
    }

    [Serializable]
    public class SendFileCommandData
    {
        public Guid SessionId { get; set; }

        public string RelativeFilePath { get; set; }

        public string HashStr { get; set; }

        public long FileLength { get; set; }
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