using System;
using System.Diagnostics;
using System.Linq;
using Newtonsoft.Json;

namespace FileSync.Common
{
    [Serializable]
    public sealed class SyncFileInfo
    {
        private string _absolutePath;

        public string HashStr { get; set; }

        public string RelativePath { get; set; }

        [JsonIgnore]
        public string AbsolutePath
        {
            get
            {
                if (_absolutePath != null)
                    return _absolutePath;
                Debugger.Break();
                throw null;
            }
            set => _absolutePath = value;
        }

        public SyncFileState State { get; set; }
    }

    public static class Extensions
    {
        public static string ToHashString(this byte[] array)
        {
            return string.Concat(array.Select(i => i.ToString("x")));
        }
    }

    public enum SyncFileState
    {
        NotChanged,
        New,
        Modified,
        Deleted,
    }
}