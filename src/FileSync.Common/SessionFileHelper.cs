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
        private readonly List<string> _newFiles;
        private readonly List<string> _removeFiles;
        private readonly List<(string oldPath, string newPath)> _renameFiles;

        public SessionFileHelper(string newDir, string toRemoveDir, string baseDir, StringBuilder log)
        {
            _newDir = newDir;
            _toRemoveDir = toRemoveDir;
            _baseDir = baseDir;
            _log = log;
            _newFiles = new List<string>();
            _removeFiles = new List<string>();
            _renameFiles = new List<(string, string)>();
        }

        public void AddNew(string relativePath)
        {
            _newFiles.Add(relativePath);
        }

        public void AddRemove(string relativePath)
        {
            _removeFiles.Add(relativePath);
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

            _removeFiles.Add(relativePath);
        }

        public void FinishSession()
        {
            foreach (var f in _newFiles)
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

            foreach (var f in _removeFiles)
            {
                var oldFilePath = Path.Combine(_toRemoveDir, f);

                _log.AppendFormat("Removing {0}", oldFilePath);
                _log.AppendLine();

                File.Delete(oldFilePath);
            }

            foreach (var (oldPath, newPath) in _renameFiles)
            {
                _log.AppendFormat("Renaming {0} to {1}", oldPath, newPath);
                var o = Path.Combine(_baseDir, oldPath);
                var n = Path.Combine(_baseDir, newPath);
                File.Move(o, n);
            }
        }

        public void AddRename(string oldPath, string newPath)
        {
            _renameFiles.Add((oldPath, newPath));
        }
    }
}