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
            var fileName = $"{folder}\\{DbFileName}";

            if (!File.Exists(fileName))
                return null;

            try
            {
                var db = JsonConvert.DeserializeObject<SyncDatabase>(File.ReadAllText(fileName));
                foreach (var f in db.Files)
                    f.AbsolutePath = $"{baseDir}{f.RelativePath}";
                return db;
            }
            catch (Exception e)
            {
                Trace.WriteLine(e);
                return null;
            }
        }

        public static SyncDatabase Initialize(string baseDir)
        {
            var localFiles = Directory.GetFiles(baseDir, "*", SearchOption.AllDirectories);
            var localInfos = localFiles.Select(i =>
            {
                HashAlgorithm alg = SHA1.Create();
                alg.ComputeHash(File.OpenRead(i));
                return new SyncFileInfo
                {
                    HashStr = alg.Hash.ToHashString(),
                    RelativePath = i.Replace(baseDir, string.Empty),
                    AbsolutePath = i,
                    State = SyncFileState.New,
                };
            }).ToList();

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

            File.WriteAllText($"{dbDir}\\{DbFileName}", JsonConvert.SerializeObject(this, Formatting.Indented));
        }
    }
}