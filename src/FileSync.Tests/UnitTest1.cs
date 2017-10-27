using System.Diagnostics;
using System.Linq;
using System.Net;
using FileSync.Common;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace FileSync.Tests
{
    [TestClass]
    public class UnitTest1
    {
        [TestMethod]
        public void GetMultipleSessions_WaitsAppropriateTime_Test()
        {
            /*using (var client = new TcpClient<IOneWaySyncService>(new IPEndPoint(IPAddress.Parse("127.0.0.1"), 9211)))
            {
                var proxy = client.Proxy;

                const int num = 5;

                var expectedTimeMin = (num - 1) * SessionStorage.CreateSessionTimeoutSeconds;
                var expectedTimeMax = num * SessionStorage.CreateSessionTimeoutSeconds;

                var sw = new Stopwatch();
                sw.Start();

                var ids = Enumerable.Range(1, num).AsParallel().Select(i => proxy.GetSessionCmd()).ToList();

                sw.Stop();

                Assert.AreEqual(ids.Count, num, $"Got {ids.Count} sessions, but expected {num}");

                Assert.IsTrue(sw.Elapsed.TotalSeconds <= expectedTimeMax && sw.Elapsed.TotalSeconds >= expectedTimeMin,
                    $"Got {num} sessions in {sw.Elapsed.TotalSeconds} s, but expected in {expectedTimeMin} - {expectedTimeMax} s");
            }*/
        }
    }
}