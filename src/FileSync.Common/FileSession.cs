using System;
using System.Collections.Generic;

namespace FileSync.Common
{
    public sealed class FileSession
    {
        public Guid Id { get; set; }

        public string RelativePath { get; set; }

        public string Hash { get; set; }

        public long FileLength { get; set; }

        public List<string> Errors { get; set; } = new List<string>();
    }
}