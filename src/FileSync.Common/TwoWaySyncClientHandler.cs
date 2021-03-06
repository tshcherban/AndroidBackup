﻿using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Net.Sockets;
using System.Reflection;
using System.Text;
using System.Threading.Tasks;

namespace FileSync.Common
{
    public sealed class TwoWaySyncClientHandler : IDisposable
    {
        private readonly TcpClient _tcpClient;
        private readonly Guid _serverId;
        private readonly IServerConfig _config;
        private NetworkStream _networkStream;
        private bool _connected;

        public event Action<string> Msg;

        private readonly StringBuilder _log;

        public TwoWaySyncClientHandler(TcpClient tcpClient, Guid serverId, IServerConfig config)
        {
            _tcpClient = tcpClient;
            _serverId = serverId;
            _config = config;
            _log = new StringBuilder();
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

        private static readonly Dictionary<byte, string> ByteToCmd;

        static TwoWaySyncClientHandler()
        {
            ByteToCmd = typeof(Commands).GetFields(BindingFlags.Public | BindingFlags.Static)
                .Where(x => x.Name.EndsWith("Cmd") && x.FieldType == typeof(byte))
                .ToDictionary(x => (byte) x.GetValue(null), x => x.Name);
        }

        private async Task ProcessCommands()
        {
            try
            {
                var cmdHeader = await NetworkHelper.ReadCommandHeader(_networkStream);

                if (!ByteToCmd.TryGetValue(cmdHeader.Command, out var cmdName))
                    cmdName = "Unknown command";

                var msg = $"Received {cmdName}";
                if (cmdHeader.PayloadLength > 0)
                    msg += $" with {cmdHeader.PayloadLength} bytes of payload";

                Msg?.Invoke(msg);

                switch (cmdHeader.Command)
                {
                    case Commands.GetSessionCmd:
                        await ProcessGetSessionCmd(cmdHeader);
                        break;
                    case Commands.GetSyncListCmd:
                        await ReturnSyncList(cmdHeader);
                        break;
                    case Commands.GetFileCmd:
                        await SendFileToClient(cmdHeader);
                        break;
                    case Commands.SendFileCmd:
                        await ReceiveFileFromClient(cmdHeader);
                        break;
                    case Commands.FinishSessionCmd:
                        await FinishSession(cmdHeader);
                        break;
                    case Commands.DisconnectCmd:
                        _connected = false;
                        break;
                    case Commands.GetIdCmd:
                        await ProcessGetIdCmd();
                        break;
                    case Commands.RegisterClientCmd:
                        await ProcessRegisterClientCmd(cmdHeader);
                        break;
                    case Commands.GetClientEndpointsCmd:
                        await ProcessGetClientEndpointsCmd(cmdHeader);
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

        private async Task ProcessGetClientEndpointsCmd(CommandHeader cmdHeader)
        {
            var clientIdBytes = await NetworkHelper.ReadBytes(_networkStream, cmdHeader.PayloadLength);
            var clientId = new Guid(clientIdBytes);

            var ret = new ServerResponseWithData<List<ClientFolderEndpoint>>();
            var cl = _config.Clients.FirstOrDefault(x => x.Id == clientId);
            if (cl == null)
                ret.ErrorMsg = $"Client {clientId} is not registered";
            else
                ret.Data = cl.FolderEndpoints
                    .Select(x => new ClientFolderEndpoint
                    {
                        DisplayName = x.DisplayName,
                        Id = x.Id
                    })
                    .ToList();

            await CommandHelper.WriteCommandResponse(_networkStream, Commands.GetClientEndpointsCmd, ret);
        }

        private async Task ProcessRegisterClientCmd(CommandHeader cmdHeader)
        {
            var clientIdBytes = await NetworkHelper.ReadBytes(_networkStream, cmdHeader.PayloadLength);
            var clientId = new Guid(clientIdBytes);

            var ret = new ServerResponseWithData<bool>();

            var cl = _config.Clients.FirstOrDefault(x => x.Id == clientId);
            if (cl != null)
            {
                ret.Data = true;
            }
            else
            {
                _config.Clients.Add(new RegisteredClient
                {
                    Id = clientId,
                });
                _config.Store();

                ret.Data = true;
            }

            await CommandHelper.WriteCommandResponse(_networkStream, Commands.RegisterClientCmd, ret);
        }

        private async Task ProcessGetIdCmd()
        {
            var response = new ServerResponseWithData<Guid>();
            try
            {
                response.Data = _serverId;
            }
            catch (Exception e)
            {
                response.ErrorMsg = e.ToString();
            }

            await CommandHelper.WriteCommandResponse(_networkStream, Commands.GetIdCmd, response);
        }

        private async Task ProcessGetSessionCmd(CommandHeader header)
        {
            var response = new ServerResponseWithData<Guid>();
            try
            {
                var req = await NetworkHelper.Read<GetSessionRequest>(_networkStream, header.PayloadLength);
                var client = _config.Clients.Single(x => x.Id == req.ClientId);
                var folder = client.FolderEndpoints.Single(x => x.Id == req.EndpointId);
                var session = SessionStorage.Instance.GetNewSession();
                session.BaseDir = folder.LocalPath;
                session.SyncDbDir = Path.Combine(folder.LocalPath, ".sync");
                if (!Directory.Exists(session.SyncDbDir))
                {
                    var dirInfo = Directory.CreateDirectory(session.SyncDbDir);
                    dirInfo.Attributes = dirInfo.Attributes | FileAttributes.Hidden;
                }

                session.RemovedDir = Path.Combine(session.SyncDbDir, "rem");
                PathHelpers.EnsureDirExists(session.RemovedDir);

                session.NewDir = Path.Combine(session.SyncDbDir, "new");
                PathHelpers.EnsureDirExists(session.NewDir);

                var helper = new SessionFileHelper(session.NewDir, session.RemovedDir, session.BaseDir, new StringBuilder());

                session.FileHelper = helper;

                response.Data = session.Id;
            }
            catch (Exception e)
            {
                response.ErrorMsg = e.ToString();
            }

            await CommandHelper.WriteCommandResponse(_networkStream, Commands.GetSessionCmd, response);
        }

        private async Task FinishSession(CommandHeader cmdHeader)
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
                try
                {
                    session.FileHelper.FinishSession();

                    session.SyncDb.Files.RemoveAll(x => x.State == SyncFileState.Deleted);

                    foreach (var f in session.SyncDb.Files.Where(x => !string.IsNullOrEmpty(x.NewRelativePath)))
                    {
                        f.RelativePath = f.NewRelativePath;
                        f.NewRelativePath = null;
                    }

                    foreach (var f in session.SyncDb.Files)
                    {
                        f.State = SyncFileState.NotChanged;
                    }
                }
                catch (Exception e)
                {
                    response.ErrorMsg = e.ToString();
                }

                session.SyncDb.Store(session.SyncDbDir);
            }

            await CommandHelper.WriteCommandResponse(_networkStream, Commands.FinishSessionCmd, response);
        }

        private async Task ReceiveFileFromClient(CommandHeader cmdHeader)
        {
            var request = await NetworkHelper.Read<SendFileRequest>(_networkStream, cmdHeader.PayloadLength);

            var ret = new ServerResponse();

            var session = SessionStorage.Instance.GetSession(request.SessionId);
            if (session == null)
            {
                ret.ErrorMsg = "Session does not exist";
            }
            else if (session.Expired)
            {
                ret.ErrorMsg = "Session has expired";
                Msg?.Invoke("Session has expired");
            }

            await CommandHelper.WriteCommandResponse(_networkStream, request.Command, ret);

            if (!ret.HasError && session != null)
            {
                request.RelativeFilePath = PathHelpers.NormalizeRelative(request.RelativeFilePath);

                var filePath = Path.Combine(session.NewDir, request.RelativeFilePath);

                var fileDir = Path.GetDirectoryName(filePath);
                PathHelpers.EnsureDirExists(fileDir);

                Msg?.Invoke($"Receiving file '{request.RelativeFilePath}'");

                var newHash = await NetworkHelper.ReadToFileAndHashAsync(_networkStream, filePath, request.FileLength);
                var hashString = newHash.ToHashString();

                if (hashString != request.HashStr)
                    Msg?.Invoke("Receive failed, hash mismatch");

                var fileInfo = session.SyncDb.Files.FirstOrDefault(i => i.RelativePath == request.RelativeFilePath);
                
                if (fileInfo != null)
                {
                    fileInfo.HashStr = hashString;
                    fileInfo.State = SyncFileState.NotChanged;
                }
                else
                {
                    session.SyncDb.AddFile(session.BaseDir, request.RelativeFilePath, hashString);
                }

                session.FileHelper.AddNew(request.RelativeFilePath);

                session.LastAccessTime = DateTime.Now;
            }
        }

        private async Task SendFileToClient(CommandHeader cmdHeader)
        {
            var data = await NetworkHelper.Read<GetFileRequest>(_networkStream, cmdHeader.PayloadLength);

            var ret = new ServerResponseWithData<long>();

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
                ret.Data = fileLength;

                await CommandHelper.WriteCommandResponse(_networkStream, data.Command, ret);
                await NetworkHelper.WriteFromFileAndHashAsync(_networkStream, filePath, (int) fileLength);

                session.LastAccessTime = DateTime.Now;
            }
        }

        private async Task ReturnSyncList(CommandHeader cmdHeader)
        {
            var remoteData = await NetworkHelper.Read<GetSyncListRequest>(_networkStream, cmdHeader.PayloadLength);

            var ret = new ServerResponseWithData<SyncInfo>();

            var session = SessionStorage.Instance.GetSession(remoteData.SessionId);
            if (session == null)
            {
                ret.ErrorMsg = "Session does not exist";
            }
            else if (session.Expired)
            {
                ret.ErrorMsg = "Session has expired";
                _log.AppendLine("Session has expired");
                //return ret;
            }
            else
            {
                try
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

                    PathHelpers.NormalizeRelative(remoteData.Files);

                    Msg?.Invoke("Preparing sync list...");

                    foreach (var localFileInfo in syncDb.Files)
                    {
                        var remoteFileInfo = remoteData.Files.FirstOrDefault(remoteFile =>
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
                            remoteData.Files.Remove(remoteFileInfo);

                            switch (remoteFileInfo.State)
                            {
                                case SyncFileState.Deleted:
                                    if (localFileInfo.State == SyncFileState.NotChanged ||
                                        localFileInfo.State == SyncFileState.Deleted)
                                    {
                                        session.FileHelper.PrepareForRemove(localFileInfo.RelativePath);
                                    }
                                    else if (localFileInfo.State == SyncFileState.New)
                                    {
                                        syncInfo.ToDownload.Add(localFileInfo);
                                    }
                                    else
                                    {
                                        var fileExt = Path.GetExtension(localFileInfo.RelativePath);
                                        var newPath = Path.GetFileNameWithoutExtension(localFileInfo.RelativePath) + "_FromServer" + fileExt;
                                        localFileInfo.NewRelativePath = newPath;
                                        session.FileHelper.AddRename(localFileInfo.RelativePath, newPath);
                                        syncInfo.ToDownload.Add(localFileInfo);
                                    }

                                    break;
                                case SyncFileState.New:
                                    if (localFileInfo.HashStr != remoteFileInfo.HashStr)
                                    {
                                        var fileExt = Path.GetExtension(remoteFileInfo.RelativePath);
                                        var newPath = Path.GetFileNameWithoutExtension(remoteFileInfo.RelativePath) + "_FromClient" + fileExt;
                                        remoteFileInfo.NewRelativePath = newPath;
                                        syncInfo.ToUpload.Add(remoteFileInfo);

                                        fileExt = Path.GetExtension(localFileInfo.RelativePath);
                                        newPath = Path.GetFileNameWithoutExtension(localFileInfo.RelativePath) + "_FromServer" + fileExt;
                                        localFileInfo.NewRelativePath = newPath;
                                        session.FileHelper.AddRename(localFileInfo.RelativePath, newPath);
                                        syncInfo.ToDownload.Add(localFileInfo);
                                    }

                                    break;
                                case SyncFileState.Modified:
                                    if (localFileInfo.State == SyncFileState.NotChanged)
                                    {
                                        syncInfo.ToUpload.Add(localFileInfo);
                                    }
                                    else if (localFileInfo.State == SyncFileState.Modified ||
                                             localFileInfo.State == SyncFileState.New)
                                    {
                                        var fileExt = Path.GetExtension(remoteFileInfo.RelativePath);
                                        var newPath = Path.GetFileNameWithoutExtension(remoteFileInfo.RelativePath) + "_FromClient" + fileExt;
                                        remoteFileInfo.NewRelativePath = newPath;
                                        syncInfo.ToUpload.Add(remoteFileInfo);

                                        fileExt = Path.GetExtension(localFileInfo.RelativePath);
                                        newPath = Path.GetFileNameWithoutExtension(localFileInfo.RelativePath) + "_FromServer" + fileExt;
                                        localFileInfo.NewRelativePath = newPath;
                                        session.FileHelper.AddRename(localFileInfo.RelativePath, newPath);
                                        syncInfo.ToDownload.Add(localFileInfo);
                                    }
                                    else if (localFileInfo.State == SyncFileState.Deleted)
                                    {
                                        var fileExt = Path.GetExtension(remoteFileInfo.RelativePath);
                                        var newPath = Path.GetFileNameWithoutExtension(remoteFileInfo.RelativePath) + "_FromClient" + fileExt;
                                        remoteFileInfo.NewRelativePath = newPath;
                                        syncInfo.ToUpload.Add(remoteFileInfo);
                                    }

                                    break;
                                case SyncFileState.NotChanged:
                                    if (localFileInfo.State == SyncFileState.Modified)
                                    {
                                        syncInfo.ToDownload.Add(localFileInfo);
                                    }
                                    else if (localFileInfo.State == SyncFileState.Deleted)
                                    {
                                        syncInfo.ToRemove.Add(remoteFileInfo);
                                    }
                                    else if (localFileInfo.State == SyncFileState.New)
                                    {
                                        if (localFileInfo.HashStr == remoteFileInfo.HashStr)
                                        {
                                            Msg?.Invoke("Skip identical file, client already has one");
                                            localFileInfo.State = SyncFileState.NotChanged;
                                        }
                                        else
                                        {
                                            Debugger.Break(); // not possible
                                            throw new InvalidOperationException("Invalid server state");
                                        }
                                    }

                                    break;
                            }
                        }
                    }

                    foreach (var remoteFileInfo in remoteData.Files.Where(x => x.State != SyncFileState.Deleted))
                    {
                        syncInfo.ToUpload.Add(remoteFileInfo);
                    }

                    ret.Data = syncInfo;

                    session.SyncDb = syncDb;
                    session.LastAccessTime = DateTime.Now;
                }
                }
                catch (Exception e)
                {
                    ret.ErrorMsg = e.Message;
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
                        var hash = NetworkHelper.HashFileAsync(new FileInfo(localFile)).Result;

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
                    var hash = NetworkHelper.HashFileAsync(new FileInfo(localFile)).Result;

                    return new SyncFileInfo
                    {
                        HashStr = hash.ToHashString(),
                        RelativePath = localFileRelativePath.TrimStart(Path.DirectorySeparatorChar),
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