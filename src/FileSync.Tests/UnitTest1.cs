using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Net;
using FileSync.Common;
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

        [TestMethod]
        public void PathHelpers_Test2()
        {
            return;
            var hashes = new HashSet<ulong>();
            var collisions = new List<Tuple<ulong, int, byte>>();

            var data = new byte[1048576];
            for (var lengthIdx = 0; lengthIdx < data.Length; ++lengthIdx)
            {
                if (lengthIdx % 1000 == 0)
                {
                    Console.WriteLine($"{lengthIdx/1000} k");

                }

                for (byte byteIdx = 1; byteIdx < byte.MaxValue; ++byteIdx)
                {
                    data[lengthIdx] = byteIdx;
                    using (var str = new MemoryStream(data))
                    {
                        var h = XxHash64Unsafe.ComputeHash(str);
                        if (!hashes.Add(h))
                        {
                            Console.WriteLine("Collision found");
                            collisions.Add(Tuple.Create(h, lengthIdx, byteIdx));
                        }
                    }
                }
            }
        }

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