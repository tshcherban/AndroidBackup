using System.IO;
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
    }
}
