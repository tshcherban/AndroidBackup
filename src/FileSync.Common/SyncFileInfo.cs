using System;
using System.Diagnostics;
using System.Linq;
using System.Text;
using Newtonsoft.Json;
using Newtonsoft.Json.Converters;

namespace FileSync.Common
{
    [Serializable]
    public sealed class SyncFileInfo
    {
        private string _absolutePath;
        private string _relativePath;

        public string HashStr { get; set; }

        public string RelativePath
        {
            get => _relativePath;
            set => _relativePath = value?.Replace("\\", null);
        }

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

        [JsonConverter(typeof(StringEnumConverter))]
        public SyncFileState State { get; set; }
    }

    public static class Extensions
    {
        public static string ToHashString(this byte[] array)
        {
            var sb = new StringBuilder();
            for (var i = 0; i < array.Length; ++i)
            {
                sb.Append(array[i].ToString("x2"));
            }

            return sb.ToString();
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