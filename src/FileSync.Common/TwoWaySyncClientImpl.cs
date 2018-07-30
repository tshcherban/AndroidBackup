using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Sockets;
using System.Security.Cryptography;
using System.Threading.Tasks;

namespace FileSync.Common
{
    internal sealed class TwoWaySyncClientImpl : ISyncClient
    {
        private const string FileListToRemove = "toremove.txt";
        private const string FileListToAddorUpdate = "newfiles.txt";

        private readonly string _serverAddress;
        private readonly int _serverPort;
        private readonly string _baseDir;
        private readonly string _syncDbDir;

        private Guid _sessionId;

        public event Action<string> Log;

        public TwoWaySyncClientImpl(string serverAddress, int serverPort, string baseDir, string syncDbDir)
        {
            _serverAddress = serverAddress;
            _serverPort = serverPort;
            _baseDir = baseDir;
            _syncDbDir = syncDbDir;
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

                        foreach (var fileInfo in syncList.Data.ToRemove)
                        {
                            var filePath = Path.Combine(_baseDir, fileInfo.RelativePath);
                            var movedFilePath = filePath + "._sr";
                            File.Move(filePath, movedFilePath);
                            var path = Path.Combine(_syncDbDir, FileListToRemove);
                            File.AppendAllText(path, $"{movedFilePath}{filePath}");
                        }

                        if (syncList.Data.Conflicts.Count > 0)
                        {
                            Debugger.Break();
                        }

                        if (!await ReceiveFiles(networkStream, syncList.Data.ToDownload))
                        {
                            return;
                        }

                        if (!await SendFiles(networkStream, syncList.Data.ToUpload))
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

                        await NetworkHelper.WriteCommandHeader(networkStream, Commands.DisconnectCmd);
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
            var filesToRemove = Path.Combine(_syncDbDir, FileListToRemove);
            if (File.Exists(filesToRemove))
            {
                var removeLines = File.ReadAllLines(filesToRemove);
                if (removeLines.Length % 2 != 0)
                {
                    throw new InvalidOperationException("Service file structure corrupt");
                }

                for (var i = 0; i < removeLines.Length - 1; i += 2)
                {
                    File.Delete(removeLines[i]);
                }

                File.Delete(filesToRemove);
            }

            var newFiles = Path.Combine(_syncDbDir, FileListToAddorUpdate);
            if (File.Exists(newFiles))
            {
                var newLines = File.ReadAllLines(newFiles);
                if (newLines.Length % 3 != 0)
                {
                    throw new InvalidOperationException("Service file structure corrupt");
                }

                for (var i = 0; i < newLines.Length - 1; i += 3)
                {
                    var destinationFile = newLines[i + 1];
                    if (File.Exists(destinationFile))
                    {
                        File.Delete(destinationFile);
                    }

                    File.Move(newLines[i], destinationFile);
                }

                File.Delete(newFiles);
            }
        }

        private async Task<ServerResponseWithData<Guid>> GetSession(Stream networkStream)
        {
            await NetworkHelper.WriteCommandHeader(networkStream, Commands.GetSessionCmd);

            var cmdHeader = await NetworkHelper.ReadCommandHeader(networkStream);
            if (cmdHeader.Command != Commands.GetSessionCmd)
                return new ServerResponseWithData<Guid> { ErrorMsg = "Wrong command received" };

            if (cmdHeader.PayloadLength == 0)
                return new ServerResponseWithData<Guid> { ErrorMsg = "No data received" };

            var responseBytes = await NetworkHelper.ReadBytes(networkStream, cmdHeader.PayloadLength);
            var response = Serializer.Deserialize<ServerResponseWithData<Guid>>(responseBytes);

            return response;
        }

        private async Task<ServerResponse> FinishSession(Stream networkStream, Guid sessionId)
        {
            var cmdDataBytes = Serializer.Serialize(sessionId);

            await NetworkHelper.WriteCommandHeader(networkStream, Commands.FinishSessionCmd, cmdDataBytes.Length);
            await NetworkHelper.WriteBytes(networkStream, cmdDataBytes);

            var cmdHeader = await NetworkHelper.ReadCommandHeader(networkStream);
            if (cmdHeader.Command != Commands.FinishSessionCmd)
                return new ServerResponseWithData<SyncInfo> { ErrorMsg = "Wrong command received" };

            if (cmdHeader.PayloadLength == 0)
                return new ServerResponseWithData<SyncInfo> { ErrorMsg = "No data received" };

            var responseBytes = await NetworkHelper.ReadBytes(networkStream, cmdHeader.PayloadLength);
            var response = Serializer.Deserialize<ServerResponse>(responseBytes);

            return response;
        }

        private async Task<bool> SendFiles(NetworkStream networkStream, List<SyncFileInfo> dataToUpload)
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
                };
                var dataBytes = Serializer.Serialize(data);
                await NetworkHelper.WriteCommandHeader(networkStream, Commands.SendFileCmd, dataBytes.Length);
                await NetworkHelper.WriteBytes(networkStream, dataBytes);
                await NetworkHelper.WriteFromFile(networkStream, filePath);
            }

            return true;
        }

        private async Task<bool> ReceiveFiles(Stream networkStream, IEnumerable<SyncFileInfo> dataToDownload)
        {
            foreach (var fileInfo in dataToDownload)
            {
                var data = new GetFileCommandData
                {
                    SessionId = _sessionId,
                    RelativeFilePath = fileInfo.RelativePath,
                };
                var dataBytes = Serializer.Serialize(data);

                await NetworkHelper.WriteCommandHeader(networkStream, Commands.GetFileCmd, dataBytes.Length);
                await NetworkHelper.WriteBytes(networkStream, dataBytes);

                var cmdHeader = await NetworkHelper.ReadCommandHeader(networkStream);
                if (cmdHeader.Command != Commands.GetFileCmd)
                    return false;

                if (cmdHeader.PayloadLength == 0)
                    return false;

                var fileLengthBytes = await NetworkHelper.ReadBytes(networkStream, cmdHeader.PayloadLength);
                var fileLength = BitConverter.ToInt64(fileLengthBytes, 0);

                var tmpFilePath = Path.Combine(_baseDir, fileInfo.RelativePath)+"._sn";
                var filePath = Path.Combine(_baseDir, fileInfo.RelativePath);
                var newHash = await NetworkHelper.ReadToFile(networkStream, tmpFilePath, fileLength);

                var newFiles = Path.Combine(_syncDbDir, FileListToAddorUpdate);
                File.AppendAllText(newFiles, $"{tmpFilePath}\r\n{filePath}\r\n\r\n");
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

            await NetworkHelper.WriteCommandHeader(networkStream, Commands.GetSyncListCmd, cmdDataBytes.Length);
            await NetworkHelper.WriteBytes(networkStream, cmdDataBytes);

            var cmdHeader = await NetworkHelper.ReadCommandHeader(networkStream);
            if (cmdHeader.Command != Commands.GetSyncListCmd)
                return new ServerResponseWithData<SyncInfo> { ErrorMsg = "Wrong command received" };

            if (cmdHeader.PayloadLength == 0)
                return new ServerResponseWithData<SyncInfo> { ErrorMsg = "No data received" };

            var responseBytes = await NetworkHelper.ReadBytes(networkStream, cmdHeader.PayloadLength);
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
                    using (HashAlgorithm alg = SHA1.Create())
                    {
                        using (var fileStream = File.OpenRead(localFile))
                        {
                            alg.ComputeHash(fileStream);
                        }

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