using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Threading.Tasks;

namespace FileSync.Common
{
    internal sealed class TwoWaySyncClientImpl : ISyncClient
    {
        private readonly string _serverAddress;
        private readonly int _serverPort;
        private readonly string _baseDir;
        private readonly string _syncDbDir;
        private readonly string _toRemoveDir;
        private readonly string _newDir;
        private readonly StringBuilder _log = new StringBuilder();

        private Guid _sessionId;

        public event Action<string> Log;

        public TwoWaySyncClientImpl(string serverAddress, int serverPort, string baseDir, string syncDbDir)
        {
            _serverAddress = serverAddress;
            _serverPort = serverPort;
            _baseDir = baseDir;
            _syncDbDir = syncDbDir;
            _toRemoveDir = Path.Combine(syncDbDir, "ToRemove");
            _newDir = Path.Combine(syncDbDir, "New");
        }

        public async Task Sync()
        {
            try
            {
                using (var client = new TcpClient())
                {
                    await client.ConnectAsync(IPAddress.Parse(_serverAddress), _serverPort);

                    using (var networkStream = client.GetStream())
                    {
                        var sessionId = await GetSession(networkStream);
                        if (sessionId.HasError)
                        {
                            Log?.Invoke($"Unable to create sync session. Server response was '{sessionId.ErrorMsg}'");
                            return;
                        }

                        _sessionId = sessionId.Data;

                        if (!Directory.Exists(_syncDbDir))
                        {
                            var dirInfo = Directory.CreateDirectory(_syncDbDir);
                            dirInfo.Attributes = dirInfo.Attributes | FileAttributes.Hidden;
                        }

                        var syncDb = GetLocalSyncDb(out var error);
                        if (syncDb == null)
                        {
                            Log?.Invoke(error);
                            return;
                        }

                        var syncList = await GetSyncList(networkStream, _sessionId, syncDb.Files);
                        if (syncList.HasError)
                        {
                            Log?.Invoke($"Unable to get sync list. Server response was '{syncList.ErrorMsg}'");
                            return;
                        }

                        foreach (var f in syncList.Data.ToDownload.Concat(syncList.Data.ToUpload).Concat(syncList.Data.ToRemove))
                        {
                            f.RelativePath = PathHelpers.Normalize(f.RelativePath).TrimStart(Path.DirectorySeparatorChar);
                        }

                        if (!Directory.Exists(_toRemoveDir))
                        {
                            Directory.CreateDirectory(_toRemoveDir);
                        }

                        if (!Directory.Exists(_newDir))
                        {
                            Directory.CreateDirectory(_newDir);
                        }

                        foreach (var fileInfo in syncList.Data.ToRemove)
                        {
                            var filePath = Path.Combine(_baseDir, fileInfo.RelativePath);
                            var movedFilePath = Path.Combine(_toRemoveDir, fileInfo.RelativePath);

                            var movedFilePathDir = Path.GetDirectoryName(movedFilePath);
                            if (movedFilePathDir == null)
                            {
                                throw new InvalidOperationException($"Unable to get '{movedFilePath}'s dir");
                            }

                            if (!Directory.Exists(movedFilePath))
                            {
                                Directory.CreateDirectory(movedFilePathDir);
                            }

                            File.Move(filePath, movedFilePath);

                            var fi = syncDb.Files.First(x => x.RelativePath == fileInfo.RelativePath);
                            syncDb.Files.Remove(fi);
                        }

                        if (syncList.Data.Conflicts.Count > 0)
                        {
                            Debugger.Break();
                        }

                        if (!await ReceiveFiles(networkStream, syncList.Data.ToDownload, syncDb))
                        {
                            return;
                        }

                        if (!await SendFiles(networkStream, syncList.Data.ToUpload, syncDb))
                        {
                            return;
                        }

                        var response = await FinishSession(networkStream, _sessionId);
                        if (response.HasError)
                        {
                            Log?.Invoke($"Error finishing session. Server response was '{response.ErrorMsg}'");

                            return;
                        }

                        FinishLocalSession();

                        syncDb.Store(_syncDbDir);

                        File.WriteAllText(Path.Combine(_syncDbDir, $"sync-{DateTime.Now:dd-MM-yyyy_hh-mm-ss}.log"), _log.ToString());

                        await NetworkHelperSequential.WriteCommandHeader(networkStream, Commands.DisconnectCmd);
                    }
                }
            }
            catch (Exception e)
            {
                Log?.Invoke($"Error during sync {e}");
            }
        }

        private void FinishLocalSession()
        {
            foreach (var filePath in Directory.EnumerateFiles(_toRemoveDir, "*", SearchOption.AllDirectories))
            {
                _log.AppendFormat("Removing '{0}'", filePath);
                _log.AppendLine();

                File.Delete(filePath);
            }

            foreach (var filePath in Directory.EnumerateFiles(_newDir, "*", SearchOption.AllDirectories))
            {
                var destinationFilePath = filePath.Replace(_newDir, _baseDir);
                destinationFilePath = PathHelpers.Normalize(destinationFilePath);

                _log.AppendFormat("Copying '{0}' to '{1}'", filePath, destinationFilePath);

                if (File.Exists(destinationFilePath))
                {
                    File.Delete(destinationFilePath);

                    _log.Append(" (with replace)");
                }

                _log.AppendLine();

                File.Move(filePath, destinationFilePath);
            }
        }

        private async Task<ServerResponseWithData<Guid>> GetSession(Stream networkStream)
        {
            await NetworkHelperSequential.WriteCommandHeader(networkStream, Commands.GetSessionCmd);

            var cmdHeader = await NetworkHelperSequential.ReadCommandHeader(networkStream);
            if (cmdHeader.Command != Commands.GetSessionCmd)
            {
                return new ServerResponseWithData<Guid> { ErrorMsg = "Wrong command received" };
            }

            if (cmdHeader.PayloadLength == 0)
            {
                return new ServerResponseWithData<Guid> { ErrorMsg = "No data received" };
            }

            var responseBytes = await NetworkHelperSequential.ReadBytes(networkStream, cmdHeader.PayloadLength);
            var response = Serializer.Deserialize<ServerResponseWithData<Guid>>(responseBytes);

            return response;
        }

        private async Task<ServerResponse> FinishSession(Stream networkStream, Guid sessionId)
        {
            var cmdDataBytes = Serializer.Serialize(sessionId);

            await NetworkHelperSequential.WriteCommandHeader(networkStream, Commands.FinishSessionCmd, cmdDataBytes.Length);
            await NetworkHelperSequential.WriteBytes(networkStream, cmdDataBytes);

            var cmdHeader = await NetworkHelperSequential.ReadCommandHeader(networkStream);
            if (cmdHeader.Command != Commands.FinishSessionCmd)
                return new ServerResponseWithData<SyncInfo> { ErrorMsg = "Wrong command received" };

            if (cmdHeader.PayloadLength == 0)
                return new ServerResponseWithData<SyncInfo> { ErrorMsg = "No data received" };

            var responseBytes = await NetworkHelperSequential.ReadBytes(networkStream, cmdHeader.PayloadLength);
            var response = Serializer.Deserialize<ServerResponse>(responseBytes);

            return response;
        }

        private async Task<bool> SendFiles(NetworkStream networkStream, List<SyncFileInfo> dataToUpload, SyncDatabase syncDb)
        {
            foreach (var fileInfo in dataToUpload)
            {
                var filePath = Path.Combine(_baseDir, fileInfo.RelativePath);
                var fileLength = new FileInfo(filePath).Length;

                var data = new SendFileCommandData
                {
                    FileLength = fileLength,
                    SessionId = _sessionId,
                    RelativeFilePath = fileInfo.RelativePath,
                    HashStr = syncDb.Files.First(x => x.RelativePath == fileInfo.RelativePath).HashStr,
                };
                var dataBytes = Serializer.Serialize(data);
                await NetworkHelperSequential.WriteCommandHeader(networkStream, Commands.SendFileCmd, dataBytes.Length);
                await NetworkHelperSequential.WriteBytes(networkStream, dataBytes);
                await NetworkHelperSequential.WriteFromFileAndHashAsync(networkStream, filePath, (int)fileLength);
            }

            return true;
        }

        private async Task<bool> ReceiveFiles(Stream networkStream, IEnumerable<SyncFileInfo> dataToDownload, SyncDatabase syncDb)
        {
            foreach (var fileInfo in dataToDownload)
            {
                var data = new GetFileCommandData
                {
                    SessionId = _sessionId,
                    RelativeFilePath = fileInfo.RelativePath,
                };
                var dataBytes = Serializer.Serialize(data);

                await NetworkHelperSequential.WriteCommandHeader(networkStream, Commands.GetFileCmd, dataBytes.Length);
                await NetworkHelperSequential.WriteBytes(networkStream, dataBytes);

                var cmdHeader = await NetworkHelperSequential.ReadCommandHeader(networkStream);
                if (cmdHeader.Command != Commands.GetFileCmd)
                {
                    return false;
                }

                if (cmdHeader.PayloadLength == 0)
                {
                    return false;
                }

                var fileLengthBytes = await NetworkHelperSequential.ReadBytes(networkStream, cmdHeader.PayloadLength);
                var fileLength = BitConverter.ToInt64(fileLengthBytes, 0);

                var tmpFilePath = Path.Combine(_newDir, fileInfo.RelativePath);
                var newHash = await NetworkHelperSequential.ReadToFileAndHashAsync(networkStream, tmpFilePath, (int)fileLength);

                if (!string.Equals(newHash.ToHashString(), fileInfo.HashStr, StringComparison.OrdinalIgnoreCase))
                {
                    throw new InvalidOperationException("File copy error: hash mismatch");
                }

                var fi = syncDb.Files.FirstOrDefault(x => x.RelativePath == fileInfo.RelativePath);
                if (fi == null)
                {
                    fi = new SyncFileInfo
                    {
                        RelativePath = fileInfo.RelativePath.TrimStart(Path.DirectorySeparatorChar),
                    };

                    syncDb.Files.Add(fi);
                }

                fi.HashStr = newHash.ToHashString();
                fi.State = SyncFileState.NotChanged;
            }

            return true;
        }

        private async Task<ServerResponseWithData<SyncInfo>> GetSyncList(Stream networkStream, Guid sessionId, List<SyncFileInfo> syncDbFiles)
        {
            var cmdData = new GetSyncListCommandData
            {
                SessionId = sessionId,
                Files = syncDbFiles,
            };

            var cmdDataBytes = Serializer.Serialize(cmdData);

            await NetworkHelperSequential.WriteCommandHeader(networkStream, Commands.GetSyncListCmd, cmdDataBytes.Length);
            await NetworkHelperSequential.WriteBytes(networkStream, cmdDataBytes);

            var cmdHeader = await NetworkHelperSequential.ReadCommandHeader(networkStream);
            if (cmdHeader.Command != Commands.GetSyncListCmd)
                return new ServerResponseWithData<SyncInfo> { ErrorMsg = "Wrong command received" };

            if (cmdHeader.PayloadLength == 0)
                return new ServerResponseWithData<SyncInfo> { ErrorMsg = "No data received" };

            var responseBytes = await NetworkHelperSequential.ReadBytes(networkStream, cmdHeader.PayloadLength);
            var response = Serializer.Deserialize<ServerResponseWithData<SyncInfo>>(responseBytes);

            return response;
        }

        private SyncDatabase GetLocalSyncDb(out string error)
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
                var localFilePath = Path.Combine(_baseDir, stored.RelativePath);
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

                        var hashString = hash.ToHashString();
                        if (hashString != stored.HashStr)
                        {
                            stored.HashStr = hashString;
                            stored.State = SyncFileState.Modified;
                        }
                    }
                }
            }

            var localInfos = localFiles.Select(localFile =>
            {
                if (dbDirInBase && localFile.StartsWith(_syncDbDir))
                {
                    return null;
                }

                var localFileRelativePath = localFile.Replace(_baseDir, string.Empty);


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
    }
}