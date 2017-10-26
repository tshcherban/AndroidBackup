using FileSync.Common;

namespace FileSync.TestClient
{
    class Program
    {
        static void Main(string[] args)
        {
            var client = SyncClientFactory.GetTwoWay("127.0.0.1", 9211, @"G:\SyncTest\Src", @"G:\SyncTest\Src\.sync");
            client.Sync();
        }
    }
}