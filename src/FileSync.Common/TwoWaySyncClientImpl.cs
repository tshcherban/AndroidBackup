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
        private const int OperationsTimeout = 5000;

        private readonly IPEndPoint _endpoint;
        private readonly string _baseDir;
        private readonly string _syncDbDir;
        private readonly Guid _clientId;
        private readonly Guid _folderId;
        private readonly string _toRemoveDir;
        private readonly string _newDir;
        private readonly StringBuilder _log = new StringBuilder();
        private readonly SessionFileHelper _sessionFileHelper;

        private Guid _sessionId;
        private NetworkStream _networkStream;

        public event Action<string> Log;

        public event Action End;

        public TwoWaySyncClientImpl(IPEndPoint endpoint, string baseDir, string syncDbDir, Guid clientId, Guid folderId)
        {
            _endpoint = endpoint;
            _baseDir = baseDir;
            _syncDbDir = syncDbDir;
            _clientId = clientId;
            _folderId = folderId;
            _toRemoveDir = Path.Combine(syncDbDir, "ToRemove");
            _newDir = Path.Combine(syncDbDir, "New");
            _sessionFileHelper = new SessionFileHelper(_newDir, _toRemoveDir, _baseDir, _log);
        }

        public async Task Sync()
        {
            try
            {
                using (var client = new TcpClient {ReceiveTimeout = OperationsTimeout, SendTimeout = OperationsTimeout})
                {
                    var success = await client.ConnectAsync(_endpoint.Address, _endpoint.Port).WhenOrTimeout(OperationsTimeout);
                    if (!success)
                    {
                        Log?.Invoke("Connecting to server timed out");
                        return;
                    }

                    _networkStream = client.GetStream();
                    using (_networkStream)
                    {
                        await DoSync();
                    }
                }
            }
            catch (Exception e)
            {
                Log?.Invoke($"Error during sync {e}");
            }

            End?.Invoke();
        }

        private async Task DoSync()
        {
            var request = new GetSessionRequest
            {
                ClientId = _clientId,
                EndpointId = _folderId,
            };

            var sessionId = await request.Send(_networkStream);
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

            Log?.Invoke("Scanning directory");

            var syncDb = GetLocalSyncDb(out var error);
            if (syncDb == null)
            {
                Log?.Invoke(error);
                return;
            }

            Log?.Invoke("Waiting server sync list");

            var syncList = await GetSyncList(_sessionId, syncDb.Files);
            if (syncList.HasError)
            {
                Log?.Invoke($"Unable to get sync list. Server response was '{syncList.ErrorMsg}'");
                return;
            }

            PathHelpers.NormalizeRelative(syncList.Data.ToDownload, syncList.Data.ToUpload, syncList.Data.ToRemove);

            PathHelpers.EnsureDirExists(_toRemoveDir);
            PathHelpers.EnsureDirExists(_newDir);

            PrepareRemoveFiles(syncList, syncDb);

            if (!await ReceiveFiles(syncList.Data.ToDownload, syncDb))
                return;

            if (!await SendFiles(syncList.Data.ToUpload, syncDb))
                return;

            Log?.Invoke("Finishing server session");

            var response = await FinishServerSession(_sessionId);
            if (response.HasError)
            {
                Log?.Invoke($"Error finishing session. Server response was '{response.ErrorMsg}'");

                return;
            }

            Log?.Invoke("Committing local changes");
            _sessionFileHelper.FinishSession();

            syncDb.Files.RemoveAll(x => x.State == SyncFileState.Deleted);

            foreach (var tu in syncList.Data.ToUpload.Where(x => !string.IsNullOrEmpty(x.NewRelativePath)))
            {
                var fi = syncDb.Files.FirstOrDefault(x => x.RelativePath == tu.RelativePath);
                if (fi != null)
                {
                    fi.RelativePath = tu.NewRelativePath;
                }
                else
                    Debugger.Break();
            }

            foreach (var f in syncDb.Files)
            {
                f.State = SyncFileState.NotChanged;
            }

            syncDb.Store(_syncDbDir);

            File.WriteAllText(Path.Combine(_syncDbDir, $"sync-{DateTime.Now:dd-MM-yyyy_hh-mm-ss}.log"), _log.ToString());

            if (new DirectoryInfo(_newDir).EnumerateFiles("*", SearchOption.AllDirectories).Any())
            {
                Debugger.Break(); // all files should be removed by now
            }

            if (new DirectoryInfo(_toRemoveDir).EnumerateFiles("*", SearchOption.AllDirectories).Any())
            {
                Debugger.Break(); // all files should be removed by now
            }

            Directory.Delete(_newDir, true);

            Directory.Delete(_toRemoveDir, true);

            await NetworkHelper.WriteCommandHeader(_networkStream, Commands.DisconnectCmd);

            Log?.Invoke("Done");
        }

        private void PrepareRemoveFiles(ServerResponseWithData<SyncInfo> syncList, SyncDatabase syncDb)
        {
            if (syncList.Data.ToRemove.Count > 0)
                Log?.Invoke($"Marking for remove {syncList.Data.ToRemove.Count} files");

            foreach (var fileInfo in syncList.Data.ToRemove)
            {
                Log?.Invoke($"Remove {fileInfo.RelativePath}");

                _sessionFileHelper.PrepareForRemove(fileInfo.RelativePath);

                var fi = syncDb.Files.First(x => x.RelativePath == fileInfo.RelativePath);
                syncDb.Files.Remove(fi);
            }
        }

        private async Task<ServerResponse> FinishServerSession(Guid sessionId)
        {
            var cmdDataBytes = Serializer.Serialize(sessionId);

            await NetworkHelper.WriteCommandHeader(_networkStream, Commands.FinishSessionCmd, cmdDataBytes.Length);
            await NetworkHelper.WriteBytes(_networkStream, cmdDataBytes);

            var cmdHeader = await NetworkHelper.ReadCommandHeader(_networkStream);
            if (cmdHeader.Command != Commands.FinishSessionCmd)
                return new ServerResponseWithData<SyncInfo> { ErrorMsg = "Wrong command received" };

            if (cmdHeader.PayloadLength == 0)
                return new ServerResponseWithData<SyncInfo> { ErrorMsg = "No data received" };

            var responseBytes = await NetworkHelper.ReadBytes(_networkStream, cmdHeader.PayloadLength);
            var response = Serializer.Deserialize<ServerResponse>(responseBytes);

            return response;
        }

        private async Task<bool> SendFiles(IReadOnlyCollection<SyncFileInfo> dataToUpload, SyncDatabase syncDb)
        {
            if (dataToUpload.Count > 0)
                Log?.Invoke($"About to upload {dataToUpload.Count} files");

            var done = 1;
            foreach (var fileInfo in dataToUpload)
            {
                Log?.Invoke($"Uploading {done++}");

                var filePath = Path.Combine(_baseDir, fileInfo.RelativePath);
                var fileLength = new FileInfo(filePath).Length;

                if (!string.IsNullOrEmpty(fileInfo.NewRelativePath))
                    _sessionFileHelper.AddRename(fileInfo.RelativePath, fileInfo.NewRelativePath);

                var request = new SendFileRequest
                {
                    FileLength = fileLength,
                    SessionId = _sessionId,
                    RelativeFilePath = fileInfo.NewRelativePath ?? fileInfo.RelativePath,
                    HashStr = syncDb.Files.First(x => x.RelativePath == fileInfo.RelativePath).HashStr,
                };

                var response = await request.Send(_networkStream);
                if (!response.HasError)
                    await NetworkHelper.WriteFromFileAndHashAsync(_networkStream, filePath, (int) fileLength);
            }

            return true;
        }

        private async Task<bool> ReceiveFiles(IReadOnlyCollection<SyncFileInfo> dataToDownload, SyncDatabase syncDb)
        {
            if (dataToDownload.Count > 0)
                Log?.Invoke($"About to receive {dataToDownload.Count} files from server");

            foreach (var fileInfo in dataToDownload)
            {
                Log?.Invoke($"Receiving {fileInfo.RelativePath}");

                var getFileRequest = new GetFileRequest
                {
                    SessionId = _sessionId,
                    RelativeFilePath = fileInfo.RelativePath,
                };

                var resp = await getFileRequest.Send(_networkStream);
                if (resp.HasError)
                    return false;

                var fileLength = resp.Data;

                var downloadedFileRelativePath = fileInfo.NewRelativePath ?? fileInfo.RelativePath;
                var tmpFilePath = Path.Combine(_newDir, downloadedFileRelativePath);
                var newHash = await NetworkHelper.ReadToFileAndHashAsync(_networkStream, tmpFilePath, (int) fileLength); // TODO remove int cast
                var newHashStr = newHash.ToHashString();

                if (!string.Equals(newHashStr, fileInfo.HashStr, StringComparison.OrdinalIgnoreCase))
                    throw new InvalidOperationException("File copy error: hash mismatch");

                _sessionFileHelper.AddNew(downloadedFileRelativePath);

                var fi = syncDb.Files.FirstOrDefault(x => x.RelativePath == fileInfo.RelativePath);
                if (fi == null || !string.IsNullOrEmpty(fileInfo.NewRelativePath))
                {
                    fi = new SyncFileInfo
                    {
                        RelativePath = downloadedFileRelativePath,
                    };

                    syncDb.Files.Add(fi);
                }
                else
                {
                    fi.RelativePath = downloadedFileRelativePath;
                    fi.NewRelativePath = null;
                }

                fi.HashStr = newHashStr;
                fi.State = SyncFileState.NotChanged;
            }

            return true;
        }

        private async Task<ServerResponseWithData<SyncInfo>> GetSyncList(Guid sessionId, List<SyncFileInfo> syncDbFiles)
        {
            var request = new GetSyncListRequest
            {
                SessionId = sessionId,
                Files = syncDbFiles,
            };

            var response = await request.Send(_networkStream);

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
                        var hash = NetworkHelper.HashFileAsync(new FileInfo(localFile)).Result;

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
    }
}