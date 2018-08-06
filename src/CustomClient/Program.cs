using System;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using FileSync.Common;

namespace FileSync.TestClient
{
    class Program
    {
        static void Main(string[] args)
        {
            /*var client = SyncClientFactory.GetTwoWay("127.0.0.1", 9211, @"G:\SyncTest\Src", @"G:\SyncTest\Src\.sync");
            client.Log += Console.WriteLine;
            client.Sync().Wait();*/

            var pr = new MurmurHash3UnsafeProvider();
            const FileOptions fileFlagNoBuffering = (FileOptions) 0x20000000;
            const FileOptions fileOptions = fileFlagNoBuffering | FileOptions.SequentialScan;

            const int chunkSize = 128 * 1024 * 1024;

            var readBufferSize = chunkSize;
            readBufferSize += ((readBufferSize + 1023) & ~1023) - readBufferSize;

            var filePath = @"C:\shcherban\shcherban.7z";

            var sw = Stopwatch.StartNew();

            using (HashAlgorithm hashAlgorithm = pr)
            using (var sourceStream = new FileStream(filePath, FileMode.Open, FileAccess.Read, FileShare.Read, readBufferSize, fileOptions))
            using (var bfs = new BufferedStream(sourceStream, readBufferSize))
            {
                var length = sourceStream.Length;

                var readSize = Convert.ToInt32(Math.Min(chunkSize, length));

                var hash = hashAlgorithm.ComputeHash(bfs);

                sw.Stop();

                var ll = hash.Select(i => new {s = i, h = i.ToString("x2")}).ToList();


                var str = hash.ToHashString().ToUpper();
                Console.WriteLine(str);

                

                Console.WriteLine($"{sw.Elapsed.TotalMilliseconds:F2} ms (speed - {(sourceStream.Length/1024m/1024m)/(decimal)sw.Elapsed.TotalSeconds:F2} mb/s)");
            }

            Console.WriteLine("Done");
            Console.ReadKey();
        }
    }
}