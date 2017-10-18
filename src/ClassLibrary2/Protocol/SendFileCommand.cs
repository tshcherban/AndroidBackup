using System;

namespace Common.Protocol
{
    public class SendFileCommand : Command<SendFileCommandData, object>
    {
        public static readonly SendFileCommand Instance = new SendFileCommand();
    }

    [Serializable]
    public class SendFileCommandData
    {
        public byte[] Data { get; set; }
    }
}