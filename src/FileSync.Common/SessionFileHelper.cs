using System;
using System.Collections.Generic;
using System.IO;
using System.Text;

namespace FileSync.Common
{
    public sealed class SessionFileHelper
    {
        private readonly string _newDir;
        private readonly string _toRemoveDir;
        private readonly string _baseDir;
        private readonly StringBuilder _log;
        private readonly List<string> NewFiles;
        private readonly List<string> RemoveFiles;

        public SessionFileHelper(string newDir, string toRemoveDir, string baseDir, StringBuilder log)
        {
            _newDir = newDir;
            _toRemoveDir = toRemoveDir;
            _baseDir = baseDir;
            _log = log;
            NewFiles = new List<string>();
            RemoveFiles = new List<string>();
        }

        public void AddNew(string relativePath)
        {
            NewFiles.Add(relativePath);
        }

        public void AddRemove(string relativePath)
        {
            RemoveFiles.Add(relativePath);
        }

        public void PrepareForRemove(string relativePath)
        {
            var filePath = Path.Combine(_baseDir, relativePath);
            var movedFilePath = Path.Combine(_toRemoveDir, relativePath);

            var movedFilePathDir = Path.GetDirectoryName(movedFilePath);
            if (movedFilePathDir == null)
            {
                throw new InvalidOperationException($"Unable to get '{movedFilePath}'s dir");
            }

            PathHelpers.EnsureDirExists(movedFilePathDir);

            File.Move(filePath, movedFilePath);

            RemoveFiles.Add(relativePath);
        }

        public void FinishSession()
        {
            foreach (var f in NewFiles)
            {
                var oldFilePath = Path.Combine(_newDir, f);
                var newFilePath = Path.Combine(_baseDir, f);
                var newFileDir = Path.GetDirectoryName(newFilePath);

                _log.AppendFormat("Moving {0} to {1}", oldFilePath, newFilePath);

                if (File.Exists(newFilePath))
                {
                    File.Delete(newFilePath);

                    _log.Append(" (with replace)");
                }

                _log.AppendLine();

                PathHelpers.EnsureDirExists(newFileDir);
                File.Move(oldFilePath, newFilePath);
            }

            foreach (var f in RemoveFiles)
            {
                var oldFilePath = Path.Combine(_toRemoveDir, f);

                _log.AppendFormat("Removing {0}", oldFilePath);
                _log.AppendLine();

                File.Delete(oldFilePath);
            }
        }
    }
}