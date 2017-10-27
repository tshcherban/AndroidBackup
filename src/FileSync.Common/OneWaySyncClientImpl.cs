using System;
using System.IO;
using System.Linq;
using System.Net;
using System.Security.Cryptography;
using System.Threading.Tasks;

namespace FileSync.Common
{
    public static class SyncClientFactory
    {
        /*public static ISyncClient GetOneWay(string serverAddress, int serverPort, string baseDir)
        {
            return new OneWaySyncClientImpl(serverAddress, serverPort, baseDir);
        }*/

        public static ISyncClient GetTwoWay(string serverAddress, int serverPort, string baseDir, string syncDbDir)
        {
            return new TwoWaySyncClientImpl(serverAddress, serverPort, baseDir, syncDbDir);
        }
    }

    public interface ISyncClient
    {
        event Action<string> Log;

        Task Sync();
    }
}