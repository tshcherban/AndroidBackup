using System;
using System.IO;
using System.Linq;
using System.Net;
using System.Security.Cryptography;
using FileSync.Common;
using ServiceWire.TcpIp;

namespace FileSync.TestClient
{
    class Program
    {
        static void Main(string[] args)
        {

            using (var client = new TcpClient<ISyncFileService>(new IPEndPoint(IPAddress.Parse("127.0.0.1"), 9211)))
            {
                var proxy = client.Proxy;
                const string baseDir = @"G:\SyncTest\Src";

                var localFiles = Directory.GetFiles(baseDir, "*", SearchOption.AllDirectories);
                var localInfos = localFiles.Select(i =>
                {
                    HashAlgorithm alg = SHA1.Create();
                    alg.ComputeHash(File.OpenRead(i));
                    return new SyncFileInfo { Hash = alg.Hash, RelativePath = i.Replace(baseDir, string.Empty) };
                }).ToList();

                var sessionId = proxy.GetSession();

                Console.WriteLine($"Got session {sessionId}");
                Console.ReadKey();

                var syncList = proxy.GetSyncList(sessionId, localInfos);

                Console.WriteLine($"Got sync list. {syncList.ToUpload} to upload");
                Console.ReadKey();

                foreach (var i in syncList.ToUpload)
                {
                    var f = baseDir + i.RelativePath;
                    var file = File.ReadAllBytes(f);
                    proxy.SendFile(sessionId, i.RelativePath, i.Hash, file);

                    Console.WriteLine($"{i.RelativePath} sent");
                }

                Console.WriteLine($"About to complete session");
                Console.ReadKey();

                proxy.CompleteSession(sessionId);

                Console.WriteLine($"Session completed");

                Console.ReadKey();
            }
        }
    }
}