using System.IO;
using System.Linq;
using System.Net;
using System.Security.Cryptography;
using ServiceWire.TcpIp;

namespace FileSync.Common
{
    public static class SyncClientFactory
    {
        public static ISyncClient GetOneWaySyncClient(string serverAddress, int serverPort, string baseDir)
        {
            return new OneWaySyncClientImpl(serverAddress, serverPort, baseDir);
        }
    }

    public interface ISyncClient
    {
        void Sync();
    }

    internal sealed class OneWaySyncClientImpl : ISyncClient
    {
        private readonly string _serverAddress;
        private readonly int _serverPort;
        private readonly string _baseDir;

        public OneWaySyncClientImpl(string serverAddress, int serverPort, string baseDir)
        {
            _serverAddress = serverAddress;
            _serverPort = serverPort;
            _baseDir = baseDir;
        }

        public void Sync()
        {
            using (var client = new TcpClient<ISyncFileService>(new IPEndPoint(IPAddress.Parse(_serverAddress), _serverPort)))
            {
                var proxy = client.Proxy;

                var localFiles = Directory.GetFiles(_baseDir, "*", SearchOption.AllDirectories);
                var localInfos = localFiles.Select(i =>
                {
                    HashAlgorithm alg = SHA1.Create();
                    alg.ComputeHash(File.OpenRead(i));
                    return new SyncFileInfo { Hash = alg.Hash, RelativePath = i.Replace(_baseDir, string.Empty) };
                }).ToList();

                var sessionId = proxy.GetSession();

                var syncList = proxy.GetSyncList(sessionId, localInfos);

                foreach (var i in syncList.ToUpload)
                {
                    var f = _baseDir + i.RelativePath;
                    var file = File.ReadAllBytes(f);
                    proxy.SendFile(sessionId, i.RelativePath, i.Hash, file);
                }

                proxy.CompleteSession(sessionId);
            }
        }
    }
}