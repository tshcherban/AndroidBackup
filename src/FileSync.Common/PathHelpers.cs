using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;

namespace FileSync.Common
{
    public static class PathHelpers
    {
        private static readonly Regex Slashes = new Regex(@"[\\\/]+", RegexOptions.Compiled);

        public static string Normalize(string path)
        {
            return Slashes.Replace(path, Path.DirectorySeparatorChar.ToString());
        }

        public static void NormalizeRelative(params IEnumerable<SyncFileInfo>[] filelists)
        {
            if (filelists.Length == 0)
            {
                return;
            }

            foreach (var fi in filelists.SelectMany(x => x))
            {
                fi.RelativePath = Normalize(fi.RelativePath).TrimStart(Path.DirectorySeparatorChar);
            }
        }

        public static void EnsureDirExists(string path)
        {
            if (!Directory.Exists(path))
            {
                Directory.CreateDirectory(path);
            }
        }
    }
}
