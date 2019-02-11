﻿using System;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Net.Sockets;
using System.Threading.Tasks;

namespace FileSync.Common
{
    public sealed class TwoWaySyncClientHandler : IDisposable
    {
        private readonly TcpClient _tcpClient;
        private readonly string _rootFolder;
        private NetworkStream _networkStream;
        private bool _connected;

        public event Action<string> Msg;

        public TwoWaySyncClientHandler(TcpClient tcpClient, string folder)
        {
            _tcpClient = tcpClient;
            _rootFolder = folder;
        }

        public async Task Process()
        {
            _networkStream = _tcpClient.GetStream();
            {
                _connected = true;
                while (_connected)
                {
                    await ProcessCommands();
                }
            }
        }

        private async Task ProcessCommands()
        {
            try
            {
                var cmdHeader = await NetworkHelperSequential.ReadCommandHeader(_networkStream);

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
                        _connected = false;
                        break;
                    default:
                        _connected = false;
                        break;
                }
            }
            catch (Exception e)
            {
                if (_connected)
                {
                    Debugger.Break();

                    _connected = false;

                    Console.WriteLine($"Unexpected error:\r\n{e}");
                }
                else
                {
                    // client disconnected
                    Console.WriteLine("Unexpected error but client already disconnected, ignoring...");
                }
            }
        }

        private async Task ProcessGetSessionCmd()
        {
            var response = new ServerResponseWithData<Guid>();
            try
            {
                var session = SessionStorage.Instance.GetNewSession();
                session.BaseDir = _rootFolder;
                session.SyncDbDir = Path.Combine(session.BaseDir, ".sync");
                if (!Directory.Exists(session.SyncDbDir))
                {
                    var dirInfo = Directory.CreateDirectory(session.SyncDbDir);
                    dirInfo.Attributes = dirInfo.Attributes | FileAttributes.Hidden;
                }

                session.RemovedDir = Path.Combine(session.SyncDbDir, "rem");
                PathHelpers.EnsureDirExists(session.RemovedDir);

                session.NewDir = Path.Combine(session.SyncDbDir, "new");
                PathHelpers.EnsureDirExists(session.NewDir);

                var helper = new SessionFileHelper(session.NewDir, session.RemovedDir, session.BaseDir, new System.Text.StringBuilder());

                session.FileHelper = helper;

                response.Data = session.Id;
            }
            catch (Exception e)
            {
                response.ErrorMsg = e.ToString();
            }

            await CommandHelper.WriteCommandResponse(_networkStream, Commands.GetSessionCmd, response);
        }

        private async Task ProcessFinishSessionCmd(CommandHeader cmdHeader)
        {
            var sessionId = await NetworkHelperSequential.Read<Guid>(_networkStream, cmdHeader.PayloadLength);

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
                try
                {
                    FinishSession(session);
                }
                catch (Exception e)
                {
                    response.ErrorMsg = e.ToString();
                }

                //session.SyncDb = SyncDatabase.Initialize(session.BaseDir, session.SyncDbDir);
                session.SyncDb.Store(session.SyncDbDir);
            }

            await CommandHelper.WriteCommandResponse(_networkStream, Commands.FinishSessionCmd, response);
        }

        private void FinishSession(Session session)
        {
            var filesToRemove = Directory.GetFiles(session.RemovedDir, "*", SearchOption.AllDirectories);
            foreach (var f in filesToRemove)
            {
                File.Delete(f);
                var ff = f.Replace(session.RemovedDir, null).TrimStart(Path.DirectorySeparatorChar);
                var fl = session.SyncDb.Files.FirstOrDefault(x => x.RelativePath == ff);
                if (fl != null)
                {
                    session.SyncDb.Files.Remove(fl);
                }
            }

            var newFiles = Directory.GetFiles(session.NewDir, "*", SearchOption.AllDirectories);
            foreach (var f in newFiles)
            {
                var target = f.Replace(session.NewDir, session.BaseDir);
                if (File.Exists(target))
                {
                    File.Delete(target);
                }

                var targetDir = Path.GetDirectoryName(target);
                PathHelpers.EnsureDirExists(targetDir);

                File.Move(f, target);

                var relative = f.Replace(session.NewDir, null).TrimStart(Path.DirectorySeparatorChar);
                var fl = session.SyncDb.Files.FirstOrDefault(x => x.RelativePath == relative);
                if (fl == null)
                {
                    Debugger.Break();
                }
            }

            if (new DirectoryInfo(session.NewDir).EnumerateFiles("*", SearchOption.AllDirectories).Any())
            {
                Debugger.Break(); // all files should be removed by now
            }

            if (new DirectoryInfo(session.RemovedDir).EnumerateFiles("*", SearchOption.AllDirectories).Any())
            {
                Debugger.Break(); // all files should be removed by now
            }

            Directory.Delete(session.NewDir, true);

            Directory.Delete(session.RemovedDir, true);
        }

        private async Task ProcessSendFileCmd(CommandHeader cmdHeader)
        {
            var data = await NetworkHelperSequential.Read<SendFileCommandData>(_networkStream, cmdHeader.PayloadLength);

            var ret = new ServerResponse();

            var session = SessionStorage.Instance.GetSession(data.SessionId);
            if (session == null)
            {
                ret.ErrorMsg = "Session does not exist";
            }
            else if (session.Expired)
            {
                ret.ErrorMsg = "Session has expired";
                Msg?.Invoke("Session has expired");
            }
            else
            {
                data.RelativeFilePath = PathHelpers.NormalizeRelative(data.RelativeFilePath);

                var filePath = Path.Combine(session.NewDir, data.RelativeFilePath);

                var fileDir = Path.GetDirectoryName(filePath);
                PathHelpers.EnsureDirExists(fileDir);

                Msg?.Invoke($"Receiving file '{data.RelativeFilePath}'");

                var newHash = await NetworkHelperSequential.ReadToFileAndHashAsync(_networkStream, filePath, data.FileLength);

                var fileInfo = session.SyncDb.Files.FirstOrDefault(i => i.RelativePath == data.RelativeFilePath);
                if (fileInfo != null)
                {
                    fileInfo.HashStr = newHash.ToHashString();
                    fileInfo.State = SyncFileState.NotChanged;
                }
                else
                {
                    session.SyncDb.AddFile(session.BaseDir, data.RelativeFilePath, newHash.ToHashString());
                }
                session.LastAccessTime = DateTime.Now;
            }
        }

        private async Task ProcessGetFileCmd(CommandHeader cmdHeader)
        {
            var data = await NetworkHelperSequential.Read<GetFileCommandData>(_networkStream, cmdHeader.PayloadLength);

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
                data.RelativeFilePath = PathHelpers.NormalizeRelative(data.RelativeFilePath);

                Msg?.Invoke($"Sending '{data.RelativeFilePath}'");

                var filePath = Path.Combine(session.BaseDir, data.RelativeFilePath);
                var fileLength = new FileInfo(filePath).Length;
                var fileLengthBytes = BitConverter.GetBytes(fileLength);
                await NetworkHelperSequential.WriteCommandHeader(_networkStream, Commands.GetFileCmd, sizeof(long));
                await NetworkHelperSequential.WriteBytes(_networkStream, fileLengthBytes);
                await NetworkHelperSequential.WriteFromFileAndHashAsync(_networkStream, filePath, (int)fileLength);
                session.LastAccessTime = DateTime.Now;
            }
        }

        private async Task ProcessGetSyncListCmd(CommandHeader cmdHeader)
        {
            var data = await NetworkHelperSequential.Read<GetSyncListCommandData>(_networkStream, cmdHeader.PayloadLength);

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
                Msg?.Invoke("Scanning local folder...");

                var syncDb = GetSyncDb(session.BaseDir, session.SyncDbDir, out var error);
                if (syncDb == null)
                {
                    ret.ErrorMsg = error;
                    Msg?.Invoke($"Failed to get sync db: {error}");
                }
                else
                {
                    var syncInfo = new SyncInfo();

                    PathHelpers.NormalizeRelative(data.Files);

                    Msg?.Invoke("Preparing sync list...");

                    foreach (var localFileInfo in syncDb.Files)
                    {
                        var remoteFileInfo = data.Files.FirstOrDefault(remoteFile =>
                            remoteFile.RelativePath == localFileInfo.RelativePath);
                        if (remoteFileInfo == null)
                        {
                            if (localFileInfo.State != SyncFileState.Deleted)
                            {
                                syncInfo.ToDownload.Add(localFileInfo);
                            }
                        }
                        else
                        {
                            data.Files.Remove(remoteFileInfo);

                            switch (remoteFileInfo.State)
                            {
                                case SyncFileState.Deleted:
                                    if (localFileInfo.State == SyncFileState.NotChanged ||
                                        localFileInfo.State == SyncFileState.Deleted)
                                    {
                                        var filePath = Path.Combine(session.BaseDir, localFileInfo.RelativePath);
                                        if (File.Exists(filePath))
                                        {
                                            var movedFilePath = Path.Combine(session.RemovedDir, localFileInfo.RelativePath);
                                            var movedFileDir = Path.GetDirectoryName(movedFilePath);
                                            if (movedFileDir == null)
                                            {
                                                throw new InvalidOperationException($"Unable to get '{movedFilePath}'s dir");
                                            }
                                            if (!Directory.Exists(movedFileDir))
                                            {
                                                Directory.CreateDirectory(movedFileDir);
                                            }

                                            File.Move(filePath, movedFilePath);
                                        }
                                    }
                                    else if (localFileInfo.State == SyncFileState.New)
                                    {
                                        syncInfo.ToDownload.Add(localFileInfo);
                                    }
                                    else
                                    {
                                        syncInfo.Conflicts.Add(localFileInfo);
                                    }

                                    break;
                                case SyncFileState.New:
                                    syncInfo.Conflicts.Add(localFileInfo);
                                    break;
                                case SyncFileState.Modified:
                                    if (localFileInfo.State == SyncFileState.NotChanged)
                                    {
                                        syncInfo.ToUpload.Add(localFileInfo);
                                    }
                                    else
                                    {
                                        syncInfo.Conflicts.Add(remoteFileInfo);
                                    }
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

                    foreach (var remoteFileInfo in data.Files.Where(x => x.State != SyncFileState.Deleted))
                    {
                        syncInfo.ToUpload.Add(remoteFileInfo);
                    }

                    ret.Data = syncInfo;

                    session.SyncDb = syncDb;
                }
            }

            await CommandHelper.WriteCommandResponse(_networkStream, Commands.GetSyncListCmd, ret);
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
                var localFilePath = Path.Combine(baseDir, stored.RelativePath);
                var localFileIdx = localFiles.IndexOf(localFilePath);
                if (localFileIdx < 0)
                {
                    stored.State = SyncFileState.Deleted;
                }
                else
                {
                    var localFile = localFiles[localFileIdx];
                    localFiles.RemoveAt(localFileIdx);

                    {
                        var hash = NetworkHelperSequential.HashFileAsync(new FileInfo(localFile)).Result;

                        var localFileHash = hash.ToHashString();
                        if (localFileHash != stored.HashStr)
                        {
                            stored.State = SyncFileState.Modified;
                            stored.HashStr = localFileHash;
                        }
                    }
                }
            }

            var localInfos = localFiles.Select(localFile =>
            {
                if (dbDirInBase && localFile.StartsWith(syncDbDir))
                    return null;

                var localFileRelativePath = localFile.Replace(baseDir, string.Empty);

                {
                    var hash = NetworkHelperSequential.HashFileAsync(new FileInfo(localFile)).Result;

                    return new SyncFileInfo
                    {
                        HashStr = hash.ToHashString(),
                        RelativePath = localFileRelativePath.TrimStart(Path.DirectorySeparatorChar),
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
                Trace.WriteLine($"Error while disposing handler (from finalizer {disposing}):\r\n{e}");
            }
        }

        ~TwoWaySyncClientHandler()
        {
            Dispose(false);
        }
    }
}