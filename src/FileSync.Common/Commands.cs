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

        public const byte GetSessionCmd = 0x01;

        public const byte GetSyncListCmd = 0x02;

        public const byte GetFileCmd = 0x03;

        public const byte SendFileCmd = 0x04;

        public const byte FinishSessionCmd = 0x05;

        public const byte DisconnectCmd = 0x06;
    }

    public class CommandHeader
    {
        public CommandHeader(byte commandId)
        {
            Command = commandId;
        }

        public virtual byte Command { get; }

        public int PayloadLength { get; set; }
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