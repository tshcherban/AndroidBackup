using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Text.RegularExpressions;

namespace FileSync.Common
{
    public static class PathHelpers
    {
        private static readonly Regex Slashes = new Regex(@"[\\\/]+", RegexOptions.Compiled);

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static string Normalize(string path)
        {
            return Slashes.Replace(path, Path.DirectorySeparatorChar.ToString());
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static string NormalizeRelative(string path)
        {
            return Slashes.Replace(path, Path.DirectorySeparatorChar.ToString()).TrimStart(Path.DirectorySeparatorChar);
        }

        public static void NormalizeRelative(params IEnumerable<SyncFileInfo>[] fileLists)
        {
            if (fileLists.Length == 0)
            {
                return;
            }

            foreach (var fi in fileLists.SelectMany(x => x))
            {
                fi.RelativePath = NormalizeRelative(fi.RelativePath);
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
