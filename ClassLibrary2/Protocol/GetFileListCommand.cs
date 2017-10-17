using System;

namespace Common.Protocol
{
    public abstract class Command<TIn, TOut>
    {
        protected Command()
        {
            Query = $"{GetType().FullName}_{typeof(TIn).FullName}__request";
            Response = $"{GetType().FullName}_{typeof(TOut).FullName}__response";
        }

        public string Query { get; }

        public string Response { get; }
    }

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