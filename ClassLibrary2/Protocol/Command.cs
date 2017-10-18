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
}