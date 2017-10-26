namespace FileSync.Common
{
    public class ServerResponse
    {
        public string ErrorMsg { get; set; }

        public bool HasError => !string.IsNullOrEmpty(ErrorMsg);
    }

    public sealed class ServerResponseWithData<T> : ServerResponse
    {
        public T Data { get; set; }
    }
}