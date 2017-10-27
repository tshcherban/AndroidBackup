using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Net.Sockets;
using System.Security.Cryptography;
using System.Threading;
using System.Threading.Tasks;

namespace FileSync.Common
{
    public sealed class TwoWaySyncService
    {
        private readonly TcpListener _tcpListener;

        public TwoWaySyncService(TcpListener tcpListener)
        {
            _tcpListener = tcpListener;
        }


        public event Action<string> Log;


        public ServerResponseWithData<FileSession> StartSendToServer(Guid sessionId, string relativePath, string fileHash, long fileLength)
        {
            var ret = new ServerResponseWithData<FileSession>();

            var session = SessionStorage.Instance.GetSession(sessionId);
            if (session == null)
            {
                ret.ErrorMsg = "Session does not exist";
                Log?.Invoke("Session does not exist");
                return ret;
            }
            if (session.Expired)
            {
                ret.ErrorMsg = "Session has expired";
                Log?.Invoke("Session has expired");
                return ret;
            }

            if (session.FileTransferSession != null)
            {
                ret.ErrorMsg = "File transfer session already in progress";
                Log?.Invoke("File transfer session already in progress");
                return ret;
            }

            ret.Data = new FileSession
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

        public ServerResponseWithData<FileSession> EndSendToServer(Guid sessionId, Guid fileId)
        {
            var ret = new ServerResponseWithData<FileSession>();

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

        public ServerResponseWithData<FileSession> StartSendToClient(Guid sessionId, string relativePath)
        {
            var ret = new ServerResponseWithData<FileSession>();
            
            var session = SessionStorage.Instance.GetSession(sessionId);
            if (session?.Expired ?? true)
            {
                ret.ErrorMsg = "Session has expired";
                Log?.Invoke("Session has expired");
                return ret;
            }

            ret.Data = new FileSession
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

        public ServerResponse FinishSession(Guid sessionId)
        {
            var ret = new ServerResponse();

            var session = SessionStorage.Instance.GetSession(sessionId);
            if (session?.Expired ?? true)
            {
                ret.ErrorMsg = "Session has expired";
                Log?.Invoke("Session has expired");
                return ret;
            }

            if (session.FileTransferSession != null)
            {
                ret.ErrorMsg = "A file transfer session is not completed";
            }
            else
            {
                
            }

            return ret;
        }
    }
}
