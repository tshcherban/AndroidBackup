using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Net.Sockets;
using System.Security.Cryptography;
using System.Threading.Tasks;
using FileSync.Common;

namespace FileSync.Server
{
    internal class Program
    {
        private readonly object _lock = new object(); // sync lock 
        private readonly List<Task> _connections = new List<Task>(); // pending connections

        private bool _stop;

        // The core server task
        private Task StartListener()
        {
            return Task.Run(async () =>
            {
                var tcpListener = TcpListener.Create(9211);
                tcpListener.Start();
                while (!_stop)
                {
                    var tcpClient = await tcpListener.AcceptTcpClientAsync();
                    Console.WriteLine("[Server] Client has connected");
                    var task = StartHandleConnectionAsync(tcpClient);
                    // if already faulted, re-throw any error on the calling context
                    if (task.IsFaulted)
                        task.Wait();
                }
            });
        }

        // Register and handle the connection
        private async Task StartHandleConnectionAsync(TcpClient tcpClient)
        {
            // start the new connection task
            var connectionTask = HandleConnectionAsync(tcpClient);

            // add it to the list of pending task 
            lock (_lock)
                _connections.Add(connectionTask);

            // catch all errors of HandleConnectionAsync
            try
            {
                await connectionTask;
                // we may be on another thread after "await"
            }
            catch (Exception ex)
            {
                // log the error
                Console.WriteLine(ex.ToString());
            }
            finally
            {
                // remove pending task
                lock (_lock)
                    _connections.Remove(connectionTask);
                Console.WriteLine("[Server] Client has disconnected");
            }
        }

        // Handle new connection
        private async Task HandleConnectionAsync(TcpClient tcpClient)
        {
            await Task.Yield();

            using (var networkStream = tcpClient.GetStream())
            {
                var run = true;
                while (run)
                {
                    var cmdHeader = await NetworkHelper.ReadCommandHeader(networkStream);

                    switch (cmdHeader.Command)
                    {
                        case Commands.GetSessionCmd:
                            await ProcessGetSessionCmd(networkStream);
                            break;
                        case Commands.GetSyncListCmd:
                            await ProcessGetSyncListCmd(networkStream, cmdHeader);
                            break;
                        case Commands.GetFileCmd:
                            await ProcessGetFileCmd(networkStream, cmdHeader);
                            break;
                        case Commands.SendFileCmd:
                            await ProcessSendFileCmd(networkStream, cmdHeader);
                            break;
                        case Commands.FinishSessionCmd:
                            await ProcessFinishSessionCmd(networkStream, cmdHeader);
                            break;
                        default:
                            run = false;
                            break;
                    }
                }
            }
        }

        private async Task ProcessFinishSessionCmd(NetworkStream networkStream, CommandHeader cmdHeader)
        {
            var sessionId = await NetworkHelper.Read<Guid>(networkStream, cmdHeader.PayloadLength);

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
            await NetworkHelper.WriteCommandHeader(networkStream, Commands.GetSyncListCmd, length);
            await NetworkHelper.WriteBytes(networkStream, responseBytes);
        }

        private async Task ProcessSendFileCmd(NetworkStream networkStream, CommandHeader cmdHeader)
        {
            var data = await NetworkHelper.Read<SendFileCommandData>(networkStream, cmdHeader.PayloadLength);

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
                await NetworkHelper.ReadToFile(networkStream, filePath, data.FileLength);
            }
        }

        private async Task ProcessGetFileCmd(NetworkStream networkStream, CommandHeader cmdHeader)
        {
            var data = await NetworkHelper.Read<GetFileCommandData>(networkStream, cmdHeader.PayloadLength);

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
                await NetworkHelper.WriteCommandHeader(networkStream, Commands.GetFileCmd, sizeof(long));
                await NetworkHelper.WriteBytes(networkStream, fileLengthBytes);
                await NetworkHelper.WriteFromFile(networkStream, filePath);
            }
        }

        private async Task ProcessGetSyncListCmd(Stream networkStream, CommandHeader cmdHeader)
        {
            var data = await NetworkHelper.Read<GetSyncListCommandData>(networkStream, cmdHeader.PayloadLength);

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
            await NetworkHelper.WriteCommandHeader(networkStream, Commands.GetSyncListCmd, length);
            await NetworkHelper.WriteBytes(networkStream, responseBytes);
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

        private async Task ProcessGetSessionCmd(Stream networkStream)
        {
            var response = new ServerResponseWithData<Guid>();
            try
            {
                var session = SessionStorage.Instance.GetNewSession();
                session.BaseDir = @"G:\SyncTest\Dst"; // TODO get from config/client/etc
                session.ServiceDir = Path.Combine(session.BaseDir, ".sync");
                //Log?.Invoke($"Session {session.Id} started");
                response.Data = session.Id;
            }
            catch (Exception e)
            {
                response.ErrorMsg = e.ToString();
            }
            var responseBytes = Serializer.Serialize(response);
            var length = responseBytes.Length;
            await NetworkHelper.WriteCommandHeader(networkStream, Commands.GetSessionCmd, length);
            await NetworkHelper.WriteBytes(networkStream, responseBytes);
        }

        public static void Main(string[] args)
        {
            if (args?.Length > 0)
            {
                Console.WriteLine("Unknown args, press any key to exit");
                Console.ReadKey();
                return;
            }

            var program = new Program();
            program.StartListener().Wait();

            while (Console.ReadKey().Key != ConsoleKey.Enter) ;

            program._stop = true;
        }

        private static void WriteLine(string obj)
        {
            Console.WriteLine(obj);
            Trace.WriteLine(obj);
        }
    }
}