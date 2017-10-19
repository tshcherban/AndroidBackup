using System;
using System.Collections.Generic;

namespace FileSync.Common
{
    public interface IFileService
    {
        List<string> GetFileList();

        void SendFile(byte[] data);
    }


    public sealed class FileService : IFileService
    {
        public List<string> GetFileList()
        {
            return new List<string>
            {
                "a",
                "b",
                "c",
            };
        }

        public void SendFile(byte[] data)
        {
            Console.WriteLine($"Received {data.Length} bytes");
        }
    }

    public interface IFileHelper
    {
        
    }
}