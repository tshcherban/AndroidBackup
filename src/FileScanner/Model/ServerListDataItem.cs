using System;
using System.Collections.Generic;
using System.Linq;
using FileSync.Common;

namespace FileSync.Android.Model
{
    public class ServerListDataItem
    {
        public string Address { get; set; }

        public Guid Id { get; set; }
        
        public bool State { get; set; }
    }

    public class FolderListDataItem
    {
        public Guid Id { get; set; }

        public string LocalPath { get; set; }

        public string DisplayName { get; set; }
    }

    public class FolderCollection
    {
        private readonly List<FolderListDataItem> _items = new List<FolderListDataItem>();

        public IReadOnlyList<FolderListDataItem> Items => _items;

        public void SetServerListFromConfig(IEnumerable<FolderConfigItem> configServers)
        {
            _items.Clear();
            _items.AddRange(configServers.Select(x => new FolderListDataItem
            {
                DisplayName = x.DisplayName,
                LocalPath = x.LocalPath,
                Id = x.Id,
            }));

            DataUpdated?.Invoke();
        }

        public event Action DataUpdated;

        public void SetList(List<ClientFolderEndpoint> folders)
        {
            _items.Clear();
            _items.AddRange(folders.Select(x => new FolderListDataItem
            {
                DisplayName = x.DisplayName,
                Id = x.Id,
            }));

            DataUpdated?.Invoke();
        }
    }
}