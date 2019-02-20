using System;
using System.Collections.Generic;

namespace FileSync.Android
{
    public interface IConfig
    {
        IReadOnlyCollection<ServerConfigItem> Servers { get; }

        Guid ClientId { get; }

        ServerConfigItem AddServer(Guid id, string url);

        void Load();

        void Store();
        
        void RemoveServer(string serverAddress);
    }
}