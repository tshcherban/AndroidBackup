using System.Linq;
using Newtonsoft.Json;

namespace FileSync.Common
{
    public class SyncFileInfo
    {
        private string _hashStr;

        public byte[] Hash { get; set; }

        [JsonIgnore]
        public string HashStr
        {
            get { return _hashStr ?? (_hashStr = string.Concat(Hash.Select(i => i.ToString("x")))); }
        }
        
        public string RelativePath { get; set; }

        [JsonIgnore]
        public string AbsolutePath { get; set; }
    }
}