using System;
using System.Text;
using Newtonsoft.Json;
using Newtonsoft.Json.Converters;

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
                {
                    return _absolutePath;
                }
                
                throw new InvalidOperationException("Absolute path was not set");
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