using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Net;
using System.Security.Cryptography;
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
                using (var client = new TcpClient<ITwoWaySyncService>(new IPEndPoint(IPAddress.Parse(_serverAddress), _serverPort)))
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
                var transferId = _serverProxy.StartFileSend(_sessionId, f.RelativePath, f.HashStr);
                if (transferId.HasError)
                {
                    Log?.Invoke($"Unable to start file receive. Server response was '{transferId.ErrorMsg}'");
                    return false;
                }

                const int length = 16 * 1024 * 1024;
                
                using (var fStream = File.OpenRead($"{_baseDir}{f.RelativePath}"))
                {
                    var toRead = fStream.Length;

                    do
                    {
                        var toReadSize = (int) Math.Min(length, toRead);

                        var buffer = new byte[toReadSize];
                        var read = fStream.Read(buffer, 0, toReadSize);
                        toRead -= read;

                        _serverProxy.SendChunk(_sessionId, transferId.Data, "" /*TODO*/, buffer);
                    } while (toRead > 0);
                }

                var finishResponse = _serverProxy.FinishFileSend(_sessionId, transferId.Data);
                if (finishResponse.HasError)
                {
                    Log?.Invoke($"Unable to finish file receive. Server response was '{transferId.ErrorMsg}'");
                    return false;
                }
            }

            return true;
        }

        private bool ReceiveFiles(ServerResponseWithData<SyncInfo> syncList)
        {
            foreach (var f in syncList.Data.ToDownload)
            {
                var transferId = _serverProxy.StartFileReceive(_sessionId, f.RelativePath);
                if (transferId.HasError)
                {
                    Log?.Invoke($"Unable to start file receive. Server response was '{transferId.ErrorMsg}'");
                    return false;
                }

                using (var fStream = File.Create($"{_baseDir}{f.RelativePath}._sync"))
                {
                    ServerResponseWithData<FileChunk> chunk;
                    do
                    {
                        chunk = _serverProxy.ReceiveChunk(_sessionId, transferId.Data.Id);
                        fStream.Write(chunk.Data.Data, 0, chunk.Data.Data.Length);
                    } while (chunk.Data.IsLast);
                }

                var resp = _serverProxy.FinishFileReceive(_sessionId, transferId.Data.Id);
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
                syncDb = SyncDatabase.Initialize(_baseDir);
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
                var localFileIdx = localFiles.IndexOf(stored.RelativePath);
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