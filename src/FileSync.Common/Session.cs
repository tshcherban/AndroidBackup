using System;

namespace FileSync.Common
{
    public class Session
    {
        public Guid Id { get; set; }

        public DateTime LastAccessTime { get; set; }
    }
}