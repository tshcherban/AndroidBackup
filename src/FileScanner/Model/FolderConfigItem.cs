using System;

namespace FileSync.Android
{
    public class FolderConfigItem
    {
        public Guid Id { get; }

        public string DisplayName { get; }

        public string LocalPath { get; }

        public FolderConfigItem(Guid id, string displayName, string localPath)
        {
            Id = id;
            DisplayName = displayName;
            LocalPath = localPath;
        }
    }
}