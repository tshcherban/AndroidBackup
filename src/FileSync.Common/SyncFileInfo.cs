using System;
using Newtonsoft.Json;
using Newtonsoft.Json.Converters;

namespace FileSync.Common
{
    [Serializable]
    public sealed class SyncFileInfo
    {
        public string HashStr { get; set; }

        public string RelativePath { get; set; }

        public string NewRelativePath { get; set; }

        [JsonConverter(typeof(StringEnumConverter))]
        public SyncFileState State { get; set; }
    }

    public enum SyncFileState
    {
        NotChanged,
        New,
        Modified,
        Deleted,
    }
}