using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;

namespace FileSync.Common
{
    public interface ISyncFileService
    {
        Guid GetSession();

        SyncInfo GetSyncList(Guid sessionId, List<SyncFileInfo> files);

        void SendFile(Guid sessionId, string relativePath, byte[] hash, byte[] file);

        void CompleteSession(Guid sessionId);
    }


    public sealed class SyncFileService : ISyncFileService
    {
        public Guid GetSession()
        {
            var session = SessionStorage.Instance.GetNewSession();
            session.BaseDir = @"G:\SyncTest\Dst"; // TODO get from config/client/etc
            session.ServiceDir = Path.Combine(session.BaseDir, ".sync");
            return session.Id;
        }

        public SyncInfo GetSyncList(Guid sessionId, List<SyncFileInfo> remoteInfos)
        {
            var session = SessionStorage.Instance.GetSession(sessionId);
            if (session?.Expired ?? true)
                throw new InvalidOperationException();

            var localFiles = Directory.GetFiles(session.BaseDir, "*", SearchOption.AllDirectories);
            var localInfos = localFiles.Select(i =>
            {
                using (HashAlgorithm alg = SHA1.Create())
                using (var inputStream = File.OpenRead(i))
                {
                    alg.ComputeHash(inputStream);

                    return new SyncFileInfo
                    {
                        Hash = alg.Hash,
                        RelativePath = i.Replace(session.BaseDir, string.Empty),
                        AbsolutePath = i,
                    };
                }
            }).ToList();

            var ret = new SyncInfo();

            var filesToRemove = new List<SyncFileInfo>();

            foreach (var remoteInfo in remoteInfos)
            {
                var localInfo = localInfos.FirstOrDefault(i => i.HashStr == remoteInfo.HashStr);
                if (localInfo == null)
                {
                    ret.ToUpload.Add(remoteInfo);
                    continue; // TODO add move detection
                }

                localInfos.Remove(localInfo);

                if (localInfo.RelativePath != remoteInfo.RelativePath)
                {
                    ret.ToUpload.Add(remoteInfo);
                    filesToRemove.Add(localInfo);
                    continue; // TODO remove when move detection done
                }
            }

            if (localInfos.Count > 0)
                filesToRemove.AddRange(localInfos);

            var cnt = 1;
            if (filesToRemove.Count > 0)
            {
                if (!Directory.Exists(session.ServiceDir))
                    Directory.CreateDirectory(session.ServiceDir);

                foreach (var fileToRemove in filesToRemove)
                {
                    var destFileName = Path.Combine(session.ServiceDir, cnt++.ToString());
                    while (File.Exists(destFileName))
                        destFileName = Path.Combine(session.ServiceDir, cnt++.ToString());

                    File.Move(fileToRemove.AbsolutePath, destFileName);
                    session.FilesForDeletion.Add((destFileName, fileToRemove.AbsolutePath));
                }
            }

            return ret;
        }

        public void SendFile(Guid sessionId, string relativePath, byte[] hash, byte[] file)
        {
            var session = SessionStorage.Instance.GetSession(sessionId);
            if (session?.Expired ?? true)
                throw new InvalidOperationException();

            var path = session.BaseDir + relativePath;
            var directory = Path.GetDirectoryName(path);
            if (!Directory.Exists(directory))
                Directory.CreateDirectory(directory);

            var newFile = path + ".new";
            File.WriteAllBytes(newFile, file);
            session.FilesForRename.Add((newFile, path));
        }

        public void CompleteSession(Guid sessionId)
        {
            var session = SessionStorage.Instance.GetSession(sessionId);
            if (session.Expired)
                throw new InvalidOperationException("Session expired");

            foreach (var i in session.FilesForDeletion)
                File.Delete(i.Item1);

            foreach (var i in session.FilesForRename)
                File.Move(i.Item1, i.Item2);

            if (Directory.Exists(session.ServiceDir))
                Directory.Delete(session.ServiceDir);

            SessionStorage.Instance.CloseSession(session);
        }
    }
}