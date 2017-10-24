using System;
using System.Collections.Generic;

namespace FileSync.Common
{
    public class Session
    {
        public Guid Id { get; set; }

        public DateTime LastAccessTime { get; set; }

        public bool Expired => (DateTime.Now - LastAccessTime).TotalMinutes > SessionStorage.SessionTimeoutMinutes;

        public string BaseDir { get; set; }

        public string ServiceDir { get; set; }

        public List<(string, string)> FilesForDeletion { get; } = new List<(string, string)>();

        public List<(string, string)> FilesForRename { get; } = new List<(string, string)>();
    }
}