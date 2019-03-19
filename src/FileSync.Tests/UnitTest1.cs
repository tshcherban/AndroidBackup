using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Net;
using FileSync.Common;
using FileSync.Common.Config;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace FileSync.Tests
{
    [TestClass]
    public class UnitTest1
    {
        private readonly Dictionary<string, string> _paths = new Dictionary<string, string>
        {
            {@"C:\\dir1/file1.txt", @"C:\dir1\file1.txt"},
            {@"C:\\dir1\file1.txt", @"C:\dir1\file1.txt"},
            {@"C:\dir1\file1.txt", @"C:\dir1\file1.txt"},
            {@"C:/dir1\file1.txt", @"C:\dir1\file1.txt"},
            {@"C://dir1\file1.txt", @"C:\dir1\file1.txt"},
            {@"C://dir1\\file1.txt", @"C:\dir1\file1.txt"},
            {@"C://dir1\\/file1.txt", @"C:\dir1\file1.txt"},
            {@"C://dir1/\file1.txt", @"C:\dir1\file1.txt"},
            {@"C://dir1/\\file1.txt", @"C:\dir1\file1.txt"},
        };

        [TestMethod]
        public void PathHelpers_Test1()
        {
            foreach (var i in _paths)
            {
                Assert.AreEqual(PathHelpers.Normalize(i.Key), i.Value);
            }
        }

        /*[TestMethod]
        public void ServerConfig_Test()
        {
            var store = new SyncServiceConfigStore(@"D:\Taras\stest\main.json");
            var conf = store.ReadServiceOrDefault();
            if (conf.Endpoints == null)
            {
                conf.Endpoints = new List<SyncEndpointConfigModel>();
            }

            conf.Endpoints.Add(new SyncEndpointConfigModel
            {
                BaseDir = @"D:\Taras\stest",
                DbDir = @"D:\Taras\stest\.sync",
                SyncMode = SyncMode.TwoWay,
            });

            store.Save(conf);
        }

        [TestMethod]
        public void ClientConfig_Test()
        {
            var store = new SyncServiceConfigStore(@"D:\Taras\stest\client.json");
            var conf = store.ReadClientOrDefault();
            if (conf.Pairs == null)
            {
                conf.Pairs = new List<SyncPairConfigModel>();
            }

            conf.Pairs.Add(new SyncPairConfigModel
            {
                BaseDir = @"/storage/emulated/0/stest",
                DbDir = @"/storage/emulated/0/stest/.sync",
                SyncMode = SyncMode.TwoWay,
                ServerAddress = "127.0.0.2:-111",
            });

            store.Save(conf);
        }*/

        [TestMethod]
        public void GetMultipleSessions_WaitsAppropriateTime_Test()
        {
            const int num = 5;

            var expectedTimeMin = (num - 1) * SessionStorage.Instance.CreateSessionTimeoutSeconds;
            var expectedTimeMax = num * SessionStorage.Instance.CreateSessionTimeoutSeconds;

            var sw = new Stopwatch();
            sw.Start();

            var ids = Enumerable.Range(1, num).AsParallel().Select(i => SessionStorage.Instance.GetNewSession()).ToList();

            sw.Stop();

            Assert.AreEqual(ids.Count, num, $"Got {ids.Count} sessions, but expected {num}");

            Assert.IsTrue(sw.Elapsed.TotalSeconds <= expectedTimeMax && sw.Elapsed.TotalSeconds >= expectedTimeMin,
                $"Got {num} sessions in {sw.Elapsed.TotalSeconds:F1} s, but expected in {expectedTimeMin:F1} - {expectedTimeMax:F1} s");
        }

        /*[TestMethod]
        public void GetMultipleSessions_WaitsAppropriateTime_Integration_Test()
        {
            using (var client = new TcpClient<IOneWaySyncService>(new IPEndPoint(IPAddress.Parse("127.0.0.1"), 9211)))
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
            }
        }*/
    }
}