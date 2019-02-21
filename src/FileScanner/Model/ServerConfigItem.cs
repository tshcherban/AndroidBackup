using System;
using System.Collections.Generic;

namespace FileSync.Android.Model
{
    public class ServerConfigItem
    {
        public Guid Id { get; }

        public string Url { get; }

        public List<FolderConfigItem> Folders { get; }

        public ServerConfigItem(string url, Guid id, List<FolderConfigItem> folders = null)
        {
            Url = url;
            Id = id;
            Folders = folders ?? new List<FolderConfigItem>();
        }
    }
}