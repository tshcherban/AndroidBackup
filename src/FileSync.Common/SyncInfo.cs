using System.Collections.Generic;

namespace FileSync.Common
{
    public class SyncInfo
    {
        public List<SyncFileInfo> ToUpload { get; set; } = new List<SyncFileInfo>();

        public List<SyncFileInfo> Conflicts { get; set; } = new List<SyncFileInfo>();
    }
}