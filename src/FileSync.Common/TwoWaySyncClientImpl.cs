using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Net;
using System.Security.Cryptography;
using System.Threading.Tasks;
using ServiceWire.TcpIp;

namespace FileSync.Common
{
    internal sealed class TwoWaySyncClientImpl : ISyncClient
    {
        private readonly string _serverAddress;
        private readonly int _serverPort;
        private readonly string _baseDir;
        private readonly string _syncDbDir;

        private ITwoWaySyncService _serverProxy;
        private Guid _sessionId;

        public event Action<string> Log;

        public TwoWaySyncClientImpl(string serverAddress, int serverPort, string baseDir, string syncDbDir)
        {
            _serverAddress = serverAddress;
            _serverPort = serverPort;
            _baseDir = baseDir;
            _syncDbDir = syncDbDir;
        }

        public void Sync()
        {
            try
            {
                using (var client =
                    new TcpClient<ITwoWaySyncService>(new IPEndPoint(IPAddress.Parse(_serverAddress), _serverPort)))
                {
                    _serverProxy = client.Proxy;

                    var sessionId = _serverProxy.GetSession();
                    if (sessionId.HasError)
                    {
                        Log?.Invoke($"Unable to create sync session. Server response was '{sessionId.ErrorMsg}'");
                        return;
                    }

                    _sessionId = sessionId.Data;

                    var syncDb = GetSyncDb(out var error);
                    if (syncDb == null)
                    {
                        Log?.Invoke(error);
                        return;
                    }

                    var syncList = _serverProxy.GetSyncList(_sessionId, syncDb.Files);
                    if (syncList.HasError)
                    {
                        Log?.Invoke($"Unable to get sync list. Server response was '{syncList.ErrorMsg}'");
                        return;
                    }

                    if (syncList.Data.Conflicts.Count > 0)
                    {
                        Debugger.Break();
                    }

                    if (!ReceiveFiles(syncList))
                        return;

                    if (!SendFiles(syncList.Data.ToUpload))
                        return;

                    var response = _serverProxy.FinishSession(_sessionId);
                    if (response.HasError)
                    {
                        Log?.Invoke($"Error finishing session. Server response was '{response.ErrorMsg}'");
                    }

                    syncDb.Store(_syncDbDir);
                }
            }
            catch (Exception e)
            {
                Log?.Invoke($"Error during sync {e}");
            }
        }

        private bool SendFiles(List<SyncFileInfo> dataToUpload)
        {
            foreach (var f in dataToUpload)
            {
                var transferId = _serverProxy.StartSendToServer(_sessionId, f.RelativePath, f.HashStr,
                    new FileInfo($"{_baseDir}{f.RelativePath}").Length);
                if (transferId.HasError)
                {
                    Log?.Invoke($"Unable to start file receive. Server response was '{transferId.ErrorMsg}'");
                    return false;
                }

                const int chunkLength = 16 * 1024 * 1024;

                var tcpClient = new System.Net.Sockets.TcpClient();
                tcpClient.Connect(new IPEndPoint(IPAddress.Parse("127.0.0.1"), 9212));
                using (var networkStream = tcpClient.GetStream())
                {
                    var buff = _sessionId.ToByteArray();
                    networkStream.Write(buff, 0, buff.Length);
                    buff = transferId.Data.Id.ToByteArray();
                    networkStream.Write(buff, 0, buff.Length);

                    using (var fStream = File.OpenRead($"{_baseDir}{f.RelativePath}"))
                    {
                        var bytesLeft = fStream.Length;
                        var buffer = new byte[Math.Min(bytesLeft, chunkLength)];
                        do
                        {
                            var readSize = (int) Math.Min(chunkLength, bytesLeft);
                            var read = fStream.Read(buffer, 0, readSize);
                            networkStream.Write(buffer, 0, readSize);
                            networkStream.Flush();
                            bytesLeft -= read;
                        } while (bytesLeft > 0);

                        networkStream.Flush();
                    }
                }


                var finishResponse = _serverProxy.EndSendToServer(_sessionId, transferId.Data.Id);
                if (finishResponse.HasError)
                {
                    Log?.Invoke($"Unable to finish file send. Server response was '{transferId.ErrorMsg}'");
                    return false;
                }
                if (finishResponse.Data.Errors.Count > 0)
                {
                    Log?.Invoke("");
                }
            }

            return true;
        }

        private bool ReceiveFiles(ServerResponseWithData<SyncInfo> syncList)
        {
            foreach (var f in syncList.Data.ToDownload)
            {
                var transferId = _serverProxy.StartSendToClient(_sessionId, f.RelativePath);
                if (transferId.HasError)
                {
                    Log?.Invoke($"Unable to start file receive. Server response was '{transferId.ErrorMsg}'");
                    return false;
                }

                var tcpClient = new System.Net.Sockets.TcpClient();
                tcpClient.Connect(new IPEndPoint(IPAddress.Parse("127.0.0.1"), 9212));
                using (var stream = tcpClient.GetStream())
                {
                    var buffer = new byte[16];
                    stream.Write(_sessionId.ToByteArray(), 0, 16);
                    stream.Write(transferId.Data.Id.ToByteArray(), 0, 16);

                    const int chunkLength = 16 * 1024 * 1024;

                    var bytesLeft = transferId.Data.FileLength;
                    buffer = new byte[Math.Min(bytesLeft, chunkLength)];

                    using (var fStream = File.Create($"{_baseDir}{transferId.Data.RelativePath}._sync"))
                    {
                        do
                        {
                            var readSize = (int) Math.Min(chunkLength, bytesLeft);
                            var read = stream.Read(buffer, 0, readSize);
                            bytesLeft -= read;
                            fStream.Write(buffer, 0, readSize);
                        } while (bytesLeft > 0);
                    }
                }
                tcpClient.Dispose();

                var resp = _serverProxy.EndSendToClient(_sessionId, transferId.Data.Id);
                if (resp.HasError)
                {
                    Log?.Invoke($"Unable to complete file receive. Server response was '{resp.ErrorMsg}'");
                    return false;
                }
            }
            return true;
        }

        private SyncDatabase GetSyncDb(out string error)
        {
            error = null;
            var syncDb = SyncDatabase.Get(_baseDir, _syncDbDir);
            if (syncDb == null)
            {
                syncDb = SyncDatabase.Initialize(_baseDir, _syncDbDir);
                if (syncDb != null)
                    return syncDb;

                error = "Unable to create sync database.";
                return null;
            }

            CheckState(syncDb);

            return syncDb;
        }

        private void CheckState(SyncDatabase syncDb)
        {
            var localFiles = Directory.GetFiles(_baseDir, "*", SearchOption.AllDirectories).ToList();
            var dbDirInBase = _syncDbDir.StartsWith(_baseDir);

            foreach (var stored in syncDb.Files)
            {
                var localFileIdx = localFiles.IndexOf($"{_baseDir}{stored.RelativePath}");
                if (localFileIdx < 0)
                {
                    stored.State = SyncFileState.Deleted;
                }
                else
                {
                    var localFile = localFiles[localFileIdx];
                    localFiles.RemoveAt(localFileIdx);
                    using (HashAlgorithm alg = SHA1.Create())
                    {
                        alg.ComputeHash(File.OpenRead(localFile));

                        if (alg.Hash.ToHashString() != stored.HashStr)
                            stored.State = SyncFileState.Modified;
                    }
                }
            }

            var localInfos = localFiles.Select(localFile =>
            {
                if (dbDirInBase && localFile.StartsWith(_syncDbDir))
                    return null;

                var localFileRelativePath = localFile.Replace(_baseDir, string.Empty);

                using (HashAlgorithm alg = SHA1.Create())
                {
                    alg.ComputeHash(File.OpenRead(localFile));

                    return new SyncFileInfo
                    {
                        HashStr = alg.Hash.ToHashString(),
                        RelativePath = localFileRelativePath,
                        AbsolutePath = localFile,
                        State = SyncFileState.New,
                    };
                }
            }).Where(i => i != null).ToList();
            syncDb.Files.AddRange(localInfos);
        }
    }
}