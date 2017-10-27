using System;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Net.Sockets;
using System.Security.Cryptography;
using System.Threading.Tasks;

namespace FileSync.Common
{
    public sealed class TwoWaySyncClientHandler : IDisposable
    {
        private readonly TcpClient _tcpClient;
        private NetworkStream _networkStream;

        public TwoWaySyncClientHandler(TcpClient tcpClient)
        {
            _tcpClient = tcpClient;
        }

        public async Task Process()
        {
            _networkStream = _tcpClient.GetStream();
            {
                var run = true;
                while (run)
                {
                    var cmdHeader = await NetworkHelper.ReadCommandHeader(_networkStream);

                    switch (cmdHeader.Command)
                    {
                        case Commands.GetSessionCmd:
                            await ProcessGetSessionCmd();
                            break;
                        case Commands.GetSyncListCmd:
                            await ProcessGetSyncListCmd(cmdHeader);
                            break;
                        case Commands.GetFileCmd:
                            await ProcessGetFileCmd(cmdHeader);
                            break;
                        case Commands.SendFileCmd:
                            await ProcessSendFileCmd(cmdHeader);
                            break;
                        case Commands.FinishSessionCmd:
                            await ProcessFinishSessionCmd(cmdHeader);
                            break;
                        case Commands.DisconnectCmd:
                            run = false;
                            break;
                        default:
                            run = false;
                            break;
                    }
                }
            }
        }

        private async Task ProcessGetSessionCmd()
        {
            var response = new ServerResponseWithData<Guid>();
            try
            {
                var session = SessionStorage.Instance.GetNewSession();
                session.BaseDir = @"G:\SyncTest\Dst"; // TODO get from config/client/etc
                session.ServiceDir = Path.Combine(session.BaseDir, ".sync");
                response.Data = session.Id;
            }
            catch (Exception e)
            {
                response.ErrorMsg = e.ToString();
            }
            var responseBytes = Serializer.Serialize(response);
            var length = responseBytes.Length;
            await NetworkHelper.WriteCommandHeader(_networkStream, Commands.GetSessionCmd, length);
            await NetworkHelper.WriteBytes(_networkStream, responseBytes);
        }

        private async Task ProcessFinishSessionCmd(CommandHeader cmdHeader)
        {
            var sessionId = await NetworkHelper.Read<Guid>(_networkStream, cmdHeader.PayloadLength);

            var response = new ServerResponseWithData<SyncInfo>();

            var session = SessionStorage.Instance.GetSession(sessionId);
            if (session == null)
            {
                response.ErrorMsg = "Session does not exist";
            }
            else if (session.Expired)
            {
                response.ErrorMsg = "Session has expired";
                //Log?.Invoke("Session has expired");
                //return ret;
            }
            else
            {
                session.SyncDb = SyncDatabase.Initialize(session.BaseDir, session.ServiceDir);
                session.SyncDb.Store(session.ServiceDir);
            }

            var responseBytes = Serializer.Serialize(response);
            var length = responseBytes.Length;
            await NetworkHelper.WriteCommandHeader(_networkStream, Commands.GetSyncListCmd, length);
            await NetworkHelper.WriteBytes(_networkStream, responseBytes);
        }

        private async Task ProcessSendFileCmd(CommandHeader cmdHeader)
        {
            var data = await NetworkHelper.Read<SendFileCommandData>(_networkStream, cmdHeader.PayloadLength);

            var ret = new ServerResponse();

            var session = SessionStorage.Instance.GetSession(data.SessionId);
            if (session == null)
            {
                ret.ErrorMsg = "Session does not exist";
            }
            else if (session.Expired)
            {
                ret.ErrorMsg = "Session has expired";
                //Log?.Invoke("Session has expired");
                //return ret;
            }
            else
            {
                var filePath = $"{session.BaseDir}{data.RelativeFilePath}._sync";
                await NetworkHelper.ReadToFile(_networkStream, filePath, data.FileLength);
            }
        }

        private async Task ProcessGetFileCmd(CommandHeader cmdHeader)
        {
            var data = await NetworkHelper.Read<GetFileCommandData>(_networkStream, cmdHeader.PayloadLength);

            var ret = new ServerResponse();

            var session = SessionStorage.Instance.GetSession(data.SessionId);
            if (session == null)
            {
                ret.ErrorMsg = "Session does not exist";
            }
            else if (session.Expired)
            {
                ret.ErrorMsg = "Session has expired";
                //Log?.Invoke("Session has expired");
                //return ret;
            }
            else
            {
                var filePath = $"{session.BaseDir}{data.RelativeFilePath}";
                var fileLength = new FileInfo(filePath).Length;
                var fileLengthBytes = BitConverter.GetBytes(fileLength);
                await NetworkHelper.WriteCommandHeader(_networkStream, Commands.GetFileCmd, sizeof(long));
                await NetworkHelper.WriteBytes(_networkStream, fileLengthBytes);
                await NetworkHelper.WriteFromFile(_networkStream, filePath);
            }
        }

        private async Task ProcessGetSyncListCmd(CommandHeader cmdHeader)
        {
            var data = await NetworkHelper.Read<GetSyncListCommandData>(_networkStream, cmdHeader.PayloadLength);

            var ret = new ServerResponseWithData<SyncInfo>();

            var session = SessionStorage.Instance.GetSession(data.SessionId);
            if (session == null)
            {
                ret.ErrorMsg = "Session does not exist";
            }
            else if (session.Expired)
            {
                ret.ErrorMsg = "Session has expired";
                //Log?.Invoke("Session has expired");
                //return ret;
            }
            else
            {
                var syncDb = GetSyncDb(session.BaseDir, session.ServiceDir, out var error);
                if (syncDb == null)
                {
                    ret.ErrorMsg = error;
                    //Log?.Invoke($"Failed to get sync db: {error}");
                    //return ret;
                }
                else
                {
                    var syncInfo = new SyncInfo();

                    foreach (var localFileInfo in syncDb.Files)
                    {
                        var remoteFileInfo = data.Files.FirstOrDefault(remoteFile => remoteFile.RelativePath == localFileInfo.RelativePath);
                        if (remoteFileInfo == null)
                        {
                            syncInfo.ToDownload.Add(localFileInfo);
                        }
                        else
                        {
                            data.Files.Remove(remoteFileInfo);

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

                    foreach (var remoteFileInfo in data.Files)
                    {
                        if (remoteFileInfo.State != SyncFileState.New)
                        {
                            Debugger.Break(); // should not be happened
                        }

                        syncInfo.ToUpload.Add(remoteFileInfo);
                    }

                    ret.Data = syncInfo;

                    session.SyncDb = syncDb;
                }
            }

            var responseBytes = Serializer.Serialize(ret);
            var length = responseBytes.Length;
            await NetworkHelper.WriteCommandHeader(_networkStream, Commands.GetSyncListCmd, length);
            await NetworkHelper.WriteBytes(_networkStream, responseBytes);
        }

        private SyncDatabase GetSyncDb(string baseDir, string syncDbDir, out string error)
        {
            error = null;
            var syncDb = SyncDatabase.Get(baseDir, syncDbDir);
            if (syncDb == null)
            {
                syncDb = SyncDatabase.Initialize(baseDir, syncDbDir);
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
                var localFileIdx = localFiles.IndexOf($"{baseDir}{stored.RelativePath}");
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

        public void Dispose()
        {
            Dispose(true);
            GC.SuppressFinalize(this);
        }

        private void Dispose(bool disposing)
        {
            try
            {
                _networkStream?.Dispose();
            }
            catch (Exception e)
            {
                Trace.WriteLine(e);
            }
        }

        ~TwoWaySyncClientHandler()
        {
            Dispose(false);
        }
    }
}