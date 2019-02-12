using System;
using System.IO;
using System.Net;
using System.Threading.Tasks;

namespace FileSync.Common
{
    public static class SyncClientFactory
    {
        /*public static ISyncClient GetOneWay(string serverAddress, int serverPort, string baseDir)
        {
            return new OneWaySyncClientImpl(serverAddress, serverPort, baseDir);
        }*/

        public static ISyncClient GetTwoWay(IPEndPoint endpoint, string baseDir, string syncDbDir = null)
        {
            if (string.IsNullOrEmpty(syncDbDir))
            {
                syncDbDir = Path.Combine(baseDir, ".sync");
            }

            return new TwoWaySyncClientImpl(endpoint, baseDir, syncDbDir);
        }
    }

    public interface ISyncClient
    {
        event Action<string> Log;

        Task Sync();
    }
}