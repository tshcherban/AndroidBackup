using FileSync.Common;

namespace FileSync.TestClient
{
    class Program
    {
        static void Main(string[] args)
        {
            var client = SyncClientFactory.GetOneWaySyncClient("127.0.0.1", 9211, @"G:\SyncTest\Src");
            client.Sync();
        }
    }
}