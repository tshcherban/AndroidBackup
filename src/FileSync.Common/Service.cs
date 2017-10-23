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

        SyncInfo GetSyncList(List<SyncFileInfo> files);

        void SendFile(string relativePath, byte[] hash, byte[] file);
    }


    public sealed class SyncSyncFileService : ISyncFileService
    {
        const string baseDir = @"G:\SyncTest\Dst";

        public Guid GetSession()
        {
            return SessionStorage.Instance.GetNewSession();
        }

        public SyncInfo GetSyncList(List<SyncFileInfo> remoteInfos)
        {
            var localFiles = Directory.GetFiles(baseDir, "*", SearchOption.AllDirectories);
            var localInfos = localFiles.Select(i =>
            {
                using (HashAlgorithm alg = SHA1.Create())
                using (var inputStream = File.OpenRead(i))
                {
                    alg.ComputeHash(inputStream);

                    return new SyncFileInfo
                    {
                        Hash = alg.Hash,
                        RelativePath = i.Replace(baseDir, string.Empty),
                        AbsolutePath = i,
                    };
                }
            }).ToList();

            var ret = new SyncInfo();

            var toRemove = new List<SyncFileInfo>();

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
                    toRemove.Add(localInfo);
                    continue; // TODO remove when move detection done
                }
            }

            if (localInfos.Count > 0)
                toRemove.AddRange(localInfos);

            var cnt = 1;
            if (toRemove.Count > 0)
            {
                var combine = Path.Combine(baseDir, ".sync");
                if (!Directory.Exists(combine))
                    Directory.CreateDirectory(combine);

                foreach (var rem in toRemove)
                {
                    var destFileName = Path.Combine(baseDir, ".sync\\", cnt++.ToString());
                    while (File.Exists(destFileName))
                        destFileName = Path.Combine(baseDir, ".sync\\", cnt++.ToString());

                    File.Move(rem.AbsolutePath, destFileName);
                }
            }

            return ret;
        }

        public void SendFile(string relativePath, byte[] hash, byte[] file)
        {
            var path = baseDir + relativePath;
            File.WriteAllBytes(path, file);
        }
    }
}