using System;
using System.Collections.Generic;
using FileSync.Common;

namespace FileSync.Android.Model
{
    internal interface IServiceLocator
    {
        IConfig Config { get; }
        
        Dictionary<Guid, ISyncClient> ActiveClients { get; }

        void Stop();
    }
}