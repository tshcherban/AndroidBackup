﻿using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Threading.Tasks;
using FileSync.Common;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace FileSync.Tests
{
    [TestClass]
    public class SyncIntegrationTests
    {
        private const int ServerPort = 9211;

        private string _src;
        private string _dst;
        private string _testsRoot;
        private IServerConfig _testServerConfig;
        private SyncServer _server;
        private ISyncClient _client;

        [TestInitialize]
        public void Init()
        {
            PrepareFolders();
            PrepareServerClient();
        }

        [TestCleanup]
        public void Cleanup()
        {
            _server.Stop();

            Directory.Delete(_testsRoot, true);
        }

        [TestMethod]
        public async Task TwoWaySync_SingleNewFile_Succeeds_Test()
        {
            var sourceFile = Path.Combine(_src, "file1.txt");
            var expectedFile = Path.Combine(_dst, "file1.txt");

            File.WriteAllText(sourceFile, "testcontent1");

            await _client.Sync();

            Assert.IsTrue(File.Exists(expectedFile), "File was not created");
            Assert.IsTrue(FilesEqual(sourceFile, expectedFile), "File's content was not synced");
        }

        [TestMethod]
        public async Task TwoWaySync_EditedFile_Succeeds_Test()
        {
            var sourceFile = Path.Combine(_src, "file1.txt");
            var expectedFile = Path.Combine(_dst, "file1.txt");

            File.WriteAllText(sourceFile, "testcontent1");

            await _client.Sync();

            Assert.IsTrue(File.Exists(expectedFile), "File was not created");
            Assert.IsTrue(FilesEqual(sourceFile, expectedFile), "File's content was not synced");

            File.AppendAllText(sourceFile, "new content");

            await _client.Sync();

            Assert.IsTrue(File.Exists(expectedFile), "File was not created");
            Assert.IsTrue(FilesEqual(sourceFile, expectedFile), "File's content was not synced");
        }

        [TestMethod]
        public async Task TwoWaySync_DeletedFile_Succeeds_Test()
        {
            var sourceFile = Path.Combine(_src, "file1.txt");
            var expectedFile = Path.Combine(_dst, "file1.txt");

            File.WriteAllText(sourceFile, "testcontent1");

            await _client.Sync();

            Assert.IsTrue(File.Exists(expectedFile), "File was not created");
            Assert.IsTrue(FilesEqual(sourceFile, expectedFile), "File's content was not synced");

            File.Delete(sourceFile);

            await _client.Sync();

            Assert.IsTrue(!File.Exists(expectedFile), "File was not deleted");
        }

        [TestMethod]
        public async Task TwoWaySync_RenamedFile_Succeeds_Test()
        {
            var sourceFile = Path.Combine(_src, "file1.txt");
            var movedSourceFile = sourceFile.Replace("file1.txt", "file22.txt");
            var expectedFile = Path.Combine(_dst, "file1.txt");
            var movedExpectedFile = Path.Combine(_dst, "file22.txt");

            File.WriteAllText(sourceFile, "testcontent1");

            await _client.Sync();

            Assert.IsTrue(File.Exists(expectedFile), "File was not created");
            Assert.IsTrue(FilesEqual(sourceFile, expectedFile), "File's content was not synced");

            File.Move(sourceFile, movedSourceFile);

            await _client.Sync();

            Assert.IsFalse(File.Exists(expectedFile), "File was not deleted");
            Assert.IsTrue(File.Exists(movedExpectedFile), "File was not created");
            Assert.IsTrue(FilesEqual(movedSourceFile, movedExpectedFile), "File's content was not synced");
        }

        private void PrepareServerClient()
        {
            _testServerConfig = new TestServerConfig();
            _testServerConfig.Clients.Add(new RegisteredClient
            {
                Id = Guid.Empty,
                FolderEndpoints =
                {
                    new ClientFolderEndpoint
                    {
                        Id = Guid.Empty,
                        DisplayName = "folder1",
                        LocalPath = _dst,
                    }
                }
            });

            SessionStorage.Instance.SetTimeout(0);

            _server = new SyncServer(ServerPort, Guid.Empty, _testServerConfig);
            _server.Msg += ServerWrite;
            _server.Start();

            var serverEp = new IPEndPoint(IPAddress.Parse("127.0.0.1"), ServerPort);

            _client = SyncClientFactory.GetTwoWay(serverEp, _src, null, Guid.Empty, Guid.Empty);
            _client.Log += ClientWrite;
        }

        private void PrepareFolders()
        {
            _testsRoot = Path.GetTempPath();
            _testsRoot = Path.Combine(_testsRoot, "sync_tests");
            if (Directory.Exists(_testsRoot))
            {
                Console.WriteLine("Warning: test folder found, deleting...");
                Directory.Delete(_testsRoot, true);
            }

            Directory.CreateDirectory(_testsRoot);

            _src = Path.Combine(_testsRoot, "src");
            Directory.CreateDirectory(_src);

            _dst = Path.Combine(_testsRoot, "dst");
            Directory.CreateDirectory(_dst);
        }

        private static void ServerWrite(string s) => Console.WriteLine($"server: {s}");

        private static void ClientWrite(string s) => Console.WriteLine($"client: {s}");

        private static bool FilesEqual(string path1, string path2)
        {
            return new FileInfo(path1).Length == new FileInfo(path2).Length &&
                   File.ReadAllBytes(path1).SequenceEqual(File.ReadAllBytes(path2));
        }

        private sealed class TestServerConfig : IServerConfig
        {
            public List<RegisteredClient> Clients { get; } = new List<RegisteredClient>();

            public void Load()
            {
            }

            public void Store()
            {
            }
        }
    }
}