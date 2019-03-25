using System;

namespace FileSync.Common
{
    public class Session
    {
        public Guid Id { get; set; }

        public DateTime LastAccessTime { get; set; }

        public DateTime CreateTime { get; set; }

        public bool Expired => (DateTime.Now - LastAccessTime).TotalMinutes > SessionStorage.SessionTimeoutMinutes;

        public string BaseDir { get; set; }

        public string SyncDbDir { get; set; }

        public FileSession FileTransferSession { get; set; }

        public SyncDatabase SyncDb { get; set; }

        public string RemovedDir { get; internal set; }

        public string NewDir { get; internal set; }

        public SessionFileHelper FileHelper { get; set; }
    }
}