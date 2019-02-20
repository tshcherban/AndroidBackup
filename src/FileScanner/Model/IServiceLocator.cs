namespace FileSync.Android
{
    internal interface IServiceLocator
    {
        IConfig Config { get; }
        
        void Stop();
    }
}