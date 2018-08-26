using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using Newtonsoft.Json;

namespace FileSync.Common
{
    public sealed class SyncDatabase
    {
        private const string DbFileName = "syncdb.json";

        public List<SyncFileInfo> Files { get; set; }

        public static SyncDatabase Get(string baseDir, string folder)
        {
            var fileName = Path.Combine(folder, DbFileName);

            if (!File.Exists(fileName))
                return null;

            try
            {
                var db = JsonConvert.DeserializeObject<SyncDatabase>(File.ReadAllText(fileName));
                foreach (var f in db.Files)
                {
                    f.AbsolutePath = Path.Combine(baseDir, f.RelativePath);
                    f.State = SyncFileState.NotChanged;
                }
                return db;
            }
            catch (Exception e)
            {
                Trace.WriteLine(e);
                return null;
            }
        }

        public static SyncDatabase Initialize(string baseDir, string syncDbDir)
        {
            var localFiles = Directory.GetFiles(baseDir, "*", SearchOption.AllDirectories);
            var inside = syncDbDir.StartsWith(baseDir);
            var localInfos = localFiles.Select(i =>
            {
                if (inside && i.StartsWith(syncDbDir))
                {
                    return null;
                }

                {
                    var hash = NetworkHelperSequential.HashFileAsync(new FileInfo(i)).Result;

                    return new SyncFileInfo
                    {
                        HashStr = hash.ToHashString(),
                        RelativePath = i.Replace(baseDir, string.Empty),
                        AbsolutePath = i,
                        State = SyncFileState.New,
                    };
                }
            }).Where(i => i != null).ToList();

            return new SyncDatabase
            {
                Files = localInfos,
            };
        }

        public void Store(string dbDir)
        {
            if (!Directory.Exists(dbDir))
            {
                var dirInfo = Directory.CreateDirectory(dbDir);
                dirInfo.Attributes = dirInfo.Attributes | FileAttributes.Hidden;
            }

            var path = Path.Combine(dbDir, DbFileName);

            File.WriteAllText(path, JsonConvert.SerializeObject(this, Formatting.Indented));
        }

        internal void AddFile(string baseDir, string relativeFilePath, string hash)
        {
            Files.Add(new SyncFileInfo
            {
                HashStr = hash,
                RelativePath = relativeFilePath,
                AbsolutePath = Path.Combine(baseDir, relativeFilePath),
                State = SyncFileState.NotChanged,
            });
        }
    }
}