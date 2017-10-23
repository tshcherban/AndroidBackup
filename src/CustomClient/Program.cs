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

                var syncList = proxy.GetSyncList(localInfos);

                foreach (var i in syncList.ToUpload)
                {
                    var f = baseDir + i.RelativePath;
                    var file = File.ReadAllBytes(f);
                    proxy.SendFile(i.RelativePath, i.Hash, file);
                }

                Console.ReadKey();
            }
        }
    }
}