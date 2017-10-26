using System.Linq;
using Newtonsoft.Json;

namespace FileSync.Common
{
    public sealed class SyncFileInfo
    {
        private string _hashStr;

        public string HashStr { get; set; }
        
        public string RelativePath { get; set; }

        [JsonIgnore]
        public string AbsolutePath { get; set; }

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