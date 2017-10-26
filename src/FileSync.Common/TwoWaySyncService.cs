using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;

namespace FileSync.Common
{
    public sealed class FileChunk
    {
        public byte[] Data { get; set; }

        public string Hash { get; set; }

        public bool IsLast { get; set; }
    }

    public sealed class FileDTO
    {
        public Guid Id { get; set; }

        public string RelativePath { get; set; }

        public string Hash { get; set; }
    }

    public interface ITwoWaySyncService
    {
        ServerResponseWithData<Guid> GetSession();

        ServerResponseWithData<SyncInfo> GetSyncList(Guid sessionId, List<SyncFileInfo> files);


        ServerResponseWithData<Guid> StartFileSend(Guid sessionId, string relativePath, string fileHash);

        ServerResponse SendChunk(Guid sessionId, Guid fileId, string chunkHash, byte[] chunk);

        ServerResponse FinishFileSend(Guid sessionId, Guid fileId);

        ServerResponseWithData<FileDTO> StartFileReceive(Guid sessionId, string relativePath);

        ServerResponseWithData<FileChunk> ReceiveChunk(Guid sessionId, Guid fileId);

        ServerResponse FinishFileReceive(Guid sessionId, Guid fileId);


        ServerResponse SyncDirectories(Guid sessionId, List<string> remoteFolders);

        ServerResponse CompleteSession(Guid sessionId);
    }

    public sealed class TwoWaySyncService : ITwoWaySyncService
    {
        public event Action<string> Log;
        public ServerResponseWithData<Guid> GetSession()
        {
            var ret = new ServerResponseWithData<Guid>();

            try
            {
                var session = SessionStorage.Instance.GetNewSession();
                session.BaseDir = @"G:\SyncTest\Dst"; // TODO get from config/client/etc
                session.ServiceDir = Path.Combine(session.BaseDir, ".sync");
                Log?.Invoke($"Session {session.Id} started");
                ret.Data = session.Id;
            }
            catch (Exception e)
            {
                ret.ErrorMsg = e.ToString();
            }
            return ret;
        }

        public ServerResponseWithData<SyncInfo> GetSyncList(Guid sessionId, List<SyncFileInfo> remoteFiles)
        {
            var ret = new ServerResponseWithData<SyncInfo>();

            var session = SessionStorage.Instance.GetSession(sessionId);
            if (session?.Expired ?? true)
            {
                ret.ErrorMsg = "Session has expired";
                Log?.Invoke("Session has expired");
                return ret;
            }

            var syncDb = GetSyncDb(session.BaseDir, session.ServiceDir, out var error);
            if (syncDb == null)
            {
                ret.ErrorMsg = error;
                Log?.Invoke($"Failed to get sync db: {error}");
                return ret;
            }

            var syncInfo = new SyncInfo();

            foreach (var localFileInfo in syncDb.Files)
            {
                var remoteFileInfo = remoteFiles.FirstOrDefault(i => i.RelativePath == localFileInfo.RelativePath);
                if (remoteFileInfo == null)
                {
                    syncInfo.ToDownload.Add(localFileInfo);
                }
                else
                {
                    remoteFiles.Remove(remoteFileInfo);

                    switch (remoteFileInfo.State)
                    {
                        case SyncFileState.Deleted:
                            if (localFileInfo.State == SyncFileState.NotChanged || localFileInfo.State == SyncFileState.Deleted)
                            {
                                ; // TODO delete file locally
                            }                                
                            else if (localFileInfo.State == SyncFileState.New)
                                syncInfo.ToDownload.Add(localFileInfo);
                            else
                                syncInfo.Conflicts.Add(localFileInfo);
                            break;
                        case SyncFileState.New:
                            syncInfo.Conflicts.Add(localFileInfo);
                            break;
                        case SyncFileState.Modified:
                            if (localFileInfo.State == SyncFileState.NotChanged)
                                syncInfo.ToUpload.Add(localFileInfo);
                            else
                                syncInfo.Conflicts.Add(remoteFileInfo);
                            break;
                        case SyncFileState.NotChanged:
                            if (localFileInfo.State == SyncFileState.Modified)
                                syncInfo.ToDownload.Add(localFileInfo);
                            else if (localFileInfo.State == SyncFileState.Deleted)
                                syncInfo.ToRemove.Add(remoteFileInfo);
                            else if (localFileInfo.State == SyncFileState.New)
                            {
                                Debugger.Break(); // not possible
                            }
                            break;
                    }
                }
            }

            foreach (var remoteFileInfo in remoteFiles)
            {
                if (remoteFileInfo.State != SyncFileState.New)
                {
                    Debugger.Break(); // should not be happened
                }

                syncInfo.ToUpload.Add(remoteFileInfo);
            }

            ret.Data = syncInfo;

            session.SyncDb = syncDb;

            return ret;
        }

        private SyncDatabase GetSyncDb(string baseDir, string syncDbDir, out string error)
        {
            error = null;
            var syncDb = SyncDatabase.Get(baseDir, syncDbDir);
            if (syncDb == null)
            {
                syncDb = SyncDatabase.Initialize(baseDir);
                if (syncDb != null)
                    return syncDb;

                error = "Unable to create sync database.";
                return null;
            }

            CheckState(baseDir, syncDbDir, syncDb);

            return syncDb;
        }

        private void CheckState(string baseDir, string syncDbDir, SyncDatabase syncDb)
        {
            var localFiles = Directory.GetFiles(baseDir, "*", SearchOption.AllDirectories).ToList();
            var dbDirInBase = syncDbDir.StartsWith(baseDir);

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
                if (dbDirInBase && localFile.StartsWith(syncDbDir))
                    return null;

                var localFileRelativePath = localFile.Replace(baseDir, string.Empty);

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

        public ServerResponseWithData<Guid> StartFileSend(Guid sessionId, string relativePath, string fileHash)
        {
            throw new NotImplementedException();
        }

        public ServerResponse SendChunk(Guid sessionId, Guid fileId, string chunkHash, byte[] chunk)
        {
            throw new NotImplementedException();
        }

        public ServerResponse FinishFileSend(Guid sessionId, Guid fileId)
        {
            throw new NotImplementedException();
        }

        public ServerResponseWithData<FileDTO> StartFileReceive(Guid sessionId, string relativePath)
        {
            var ret = new ServerResponseWithData<FileDTO>();
            
            var session = SessionStorage.Instance.GetSession(sessionId);
            if (session?.Expired ?? true)
            {
                ret.ErrorMsg = "Session has expired";
                Log?.Invoke("Session has expired");
                return ret;
            }

            session.FileTransferSession = Guid.NewGuid();

            ret.Data = new FileDTO
            {
                Id = session.FileTransferSession.Value,
            };

            return ret;
        }

        public ServerResponseWithData<FileChunk> ReceiveChunk(Guid sessionId, Guid fileId)
        {
            throw new NotImplementedException();
        }

        public ServerResponse FinishFileReceive(Guid sessionId, Guid fileId)
        {
            throw new NotImplementedException();
        }

        public ServerResponse SyncDirectories(Guid sessionId, List<string> remoteFolders)
        {
            throw new NotImplementedException();
        }

        public ServerResponse CompleteSession(Guid sessionId)
        {
            throw new NotImplementedException();
        }
    }

    public class ServerResponse
    {
        public string ErrorMsg { get; set; }

        public bool HasError => !string.IsNullOrEmpty(ErrorMsg);
    }

    public sealed class ServerResponseWithData<T> : ServerResponse
    {
        public T Data { get; set; }
    }
}
