using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Net.Sockets;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Newtonsoft.Json;

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

        public long FileLength { get; set; }

        public List<string> Errors { get; set; } = new List<string>();
    }

    public interface ITwoWaySyncService
    {
        ServerResponseWithData<Guid> GetSession();

        ServerResponseWithData<SyncInfo> GetSyncList(Guid sessionId, List<SyncFileInfo> files);


        ServerResponseWithData<FileDTO> StartSendToServer(Guid sessionId, string relativePath, string fileHash, long fileLength);

        ServerResponseWithData<FileDTO> EndSendToServer(Guid sessionId, Guid fileId);

        ServerResponseWithData<FileDTO> StartSendToClient(Guid sessionId, string relativePath);

        ServerResponse EndSendToClient(Guid sessionId, Guid fileId);


        ServerResponse SyncDirectories(Guid sessionId, List<string> remoteFolders);

        ServerResponse CompleteSession(Guid sessionId);
    }

    public sealed class TwoWaySyncService : ITwoWaySyncService
    {
        private readonly TcpListener _tcpListener;

        public TwoWaySyncService(TcpListener tcpListener)
        {
            _tcpListener = tcpListener;
        }

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

        private static void CheckState(string baseDir, string syncDbDir, SyncDatabase syncDb)
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

        public ServerResponseWithData<FileDTO> StartSendToServer(Guid sessionId, string relativePath, string fileHash, long fileLength)
        {
            var ret = new ServerResponseWithData<FileDTO>();

            var session = SessionStorage.Instance.GetSession(sessionId);
            if (session?.Expired ?? true)
            {
                ret.ErrorMsg = "Session has expired";
                Log?.Invoke("Session has expired");
                return ret;
            }

            if (session.FileTransferSession != null)
                throw null;

            ret.Data = new FileDTO
            {
                Id = Guid.NewGuid(),
                RelativePath = relativePath,
                FileLength = fileLength,
            };

            session.FileTransferSession = ret.Data;

            session.SendTask = _tcpListener.AcceptTcpClientAsync().ContinueWith(ProcessSendToServer);
            return ret;
        }

        private void ProcessSendToServer(Task<TcpClient> task)
        {
            var tcpClient = task.Result;

            using (var stream = tcpClient.GetStream())
            {
                var buffer = new byte[16];
                var read =  stream.Read(buffer, 0, 16);
                var sessionId = new Guid(buffer);
                read =  stream.Read(buffer, 0, 16);
                var fileId = new Guid(buffer);

                var session = SessionStorage.Instance.GetSession(sessionId);
                if (session == null)
                {
                    Log?.Invoke($"Session {sessionId} does not exist");
                    return;
                }
                if (session.Expired)
                {
                    Log?.Invoke("Session has expired");
                    return;
                }

                var fileTransferSession = session.FileTransferSession;
                if (fileTransferSession == null)
                {
                    Log?.Invoke("File transfer session null");
                    return;
                }
                if (fileTransferSession.Id != fileId)
                {
                    Log?.Invoke("File id incorrect");
                    fileTransferSession.Errors.Add("File id incorrect");
                    return;
                }

                const int chunkLength = 16 * 1024 * 1024;

                var bytesLeft = fileTransferSession.FileLength;
                buffer = new byte[Math.Min(bytesLeft, chunkLength)];

                var formattableString = $"{session.BaseDir}{fileTransferSession.RelativePath}._sync";
                var dir = Path.GetDirectoryName(formattableString);
                if (!Directory.Exists(dir))
                    Directory.CreateDirectory(dir);

                using (var fStream = File.Create(formattableString))
                {
                    do
                    {
                        var readSize = (int)Math.Min(chunkLength, bytesLeft);
                        read =  stream.Read(buffer, 0, readSize);
                         fStream.Write(buffer, 0, readSize);
                         fStream.Flush();
                        bytesLeft -= read;
                    } while (bytesLeft > 0);
                    
                }
            }
            tcpClient.Dispose();
        }

        public ServerResponseWithData<FileDTO> EndSendToServer(Guid sessionId, Guid fileId)
        {
            var ret = new ServerResponseWithData<FileDTO>();

            var session = SessionStorage.Instance.GetSession(sessionId);
            if (session?.Expired ?? true)
            {
                ret.ErrorMsg = "Session has expired";
                Log?.Invoke("Session has expired");
                return ret;
            }

            session.SendTask.Wait();
            ret.Data = session.FileTransferSession;
            session.FileTransferSession = null;

            return ret;
        }

        public ServerResponseWithData<FileDTO> StartSendToClient(Guid sessionId, string relativePath)
        {
            var ret = new ServerResponseWithData<FileDTO>();
            
            var session = SessionStorage.Instance.GetSession(sessionId);
            if (session?.Expired ?? true)
            {
                ret.ErrorMsg = "Session has expired";
                Log?.Invoke("Session has expired");
                return ret;
            }

            ret.Data = new FileDTO
            {
                Id = Guid.NewGuid(),
                RelativePath = relativePath,
                FileLength = new FileInfo($"{session.BaseDir}{relativePath}").Length,
            };

            session.FileTransferSession = ret.Data;

            session.SendTask = Task.Run(() =>
            {
                var client = _tcpListener.AcceptTcpClient();
                ProcessSendToClient(Task.FromResult(client));
            });

            return ret;
        }

        private void ProcessSendToClient(Task<TcpClient> task)
        {
            var tcpClient = task.Result;

            using (var stream = tcpClient.GetStream())
            {
                var buffer = new byte[16];
                var read =  stream.Read(buffer, 0, 16);
                var sessionId = new Guid(buffer);
                read =  stream.Read(buffer, 0, 16);
                var fileId = new Guid(buffer);

                var session = SessionStorage.Instance.GetSession(sessionId);
                if (session?.Expired ?? true)
                {
                    Log?.Invoke("Session has expired");
                    return;
                }

                var fileTransferSession = session.FileTransferSession;
                if (fileTransferSession == null)
                {
                    Log?.Invoke("File transfer session null");
                    return;
                }
                if (fileTransferSession.Id != fileId)
                {
                    Log?.Invoke("File id incorrect");
                    return;
                }

                const int chunkLength = 16*1024*1024;

                var bytesLeft = fileTransferSession.FileLength;
                buffer = new byte[Math.Min(bytesLeft, chunkLength)];

                using (var fStream = File.OpenRead($"{session.BaseDir}{fileTransferSession.RelativePath}"))
                {
                    do
                    {
                        var readSize = (int)Math.Min(chunkLength, bytesLeft);
                        read =  fStream.Read(buffer, 0, readSize);
                        stream.Write(buffer, 0, readSize);

                        bytesLeft -= read;
                    } while (bytesLeft > 0);
                }
            }
            tcpClient.Dispose();
        }

        public ServerResponse EndSendToClient(Guid sessionId, Guid fileId)
        {
            var ret = new ServerResponse();

            var session = SessionStorage.Instance.GetSession(sessionId);
            if (session?.Expired ?? true)
            {
                ret.ErrorMsg = "Session has expired";
                Log?.Invoke("Session has expired");
                return ret;
            }

            //session.FileTransferSession.Processing.WaitOne();
            session.FileTransferSession = null;

            return ret;
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
