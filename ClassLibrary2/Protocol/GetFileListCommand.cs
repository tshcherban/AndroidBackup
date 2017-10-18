using System;

namespace Common.Protocol
{
    public sealed class GetFileListCommand : Command<GetFileListRequest, GetFileListResponse>
    {
        public static readonly GetFileListCommand Instance = new GetFileListCommand();
    }

    [Serializable]
    public sealed class GetFileListRequest
    {
        public string RequestData { get; set; }
    }

    [Serializable]
    public sealed class GetFileListResponse
    {
        public string Data { get; set; }
    }
}