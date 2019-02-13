using System;
using System.Collections.Generic;

namespace FileSync.Common
{
    [Serializable]
    public class SyncInfo
    {
        public List<SyncFileInfo> ToUpload { get; } = new List<SyncFileInfo>();

        public List<SyncFileInfo> ToDownload { get; } = new List<SyncFileInfo>();

        public List<SyncFileInfo> ToRemove { get; } = new List<SyncFileInfo>();
    }
}