using System;

namespace FileSync.Common
{
    [Serializable]
    public class ServerResponse
    {
        public string ErrorMsg { get; set; }

        public bool HasError => !string.IsNullOrEmpty(ErrorMsg);
    }

    [Serializable]
    public sealed class ServerResponseWithData<T> : ServerResponse
    {
        public T Data { get; set; }
    }
}