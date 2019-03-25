using System;
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

        private string _clientFolder;
        private string _serverFolder;
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
        public async Task TwoWaySync_SingleNewFileClient_Succeeds_Test()
        {
            await TwoWaySync_SingleNewFile_Succeeds(_clientFolder, _serverFolder);
        }

        [TestMethod]
        public async Task TwoWaySync_SingleNewFileServer_Succeeds_Test()
        {
            await TwoWaySync_SingleNewFile_Succeeds(_serverFolder, _clientFolder);
        }

        private async Task TwoWaySync_SingleNewFile_Succeeds(string src, string dst)
        {
            var sourceFile = Path.Combine(src, "file1.txt");
            var expectedFile = Path.Combine(dst, "file1.txt");

            File.WriteAllText(sourceFile, "testcontent1");

            await _client.Sync();

            Assert.IsTrue(File.Exists(expectedFile), "File was not created");
            Assert.IsTrue(FilesEqual(sourceFile, expectedFile), "File's content was not synced");
        }

        [TestMethod]
        public async Task TwoWaySync_EditedFileClient_Succeeds_Test()
        {
            await TwoWaySync_EditedFile_Succeeds(_clientFolder, _serverFolder);
        }

        [TestMethod]
        public async Task TwoWaySync_EditedFileServer_Succeeds_Test()
        {
            await TwoWaySync_EditedFile_Succeeds(_serverFolder, _clientFolder);
        }

        private async Task TwoWaySync_EditedFile_Succeeds(string src, string dst)
        {
            var sourceFile = Path.Combine(src, "file1.txt");
            var expectedFile = Path.Combine(dst, "file1.txt");

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
        public async Task TwoWaySync_DeletedFileClient_Succeeds_Test()
        {
            await TwoWaySync_DeletedFile_Succeeds(_clientFolder, _serverFolder);
        }

        [TestMethod]
        public async Task TwoWaySync_DeletedFileServer_Succeeds_Test()
        {
            await TwoWaySync_DeletedFile_Succeeds(_serverFolder, _clientFolder);
        }

        private async Task TwoWaySync_DeletedFile_Succeeds(string src, string dst)
        {
            var sourceFile = Path.Combine(src, "file1.txt");
            var expectedFile = Path.Combine(dst, "file1.txt");

            File.WriteAllText(sourceFile, "testcontent1");

            await _client.Sync();

            Assert.IsTrue(File.Exists(expectedFile), "File was not created");
            Assert.IsTrue(FilesEqual(sourceFile, expectedFile), "File's content was not synced");

            File.Delete(sourceFile);

            await _client.Sync();

            Assert.IsFalse(File.Exists(expectedFile), "File was not deleted");
        }

        [TestMethod]
        public async Task TwoWaySync_RenamedFileClient_Succeeds_Test()
        {
            await TwoWaySync_RenamedFile_Succeeds(_clientFolder, _serverFolder);
        }

        [TestMethod]
        public async Task TwoWaySync_RenamedFileServer_Succeeds_Test()
        {
            await TwoWaySync_RenamedFile_Succeeds(_serverFolder, _clientFolder);
        }

        private async Task TwoWaySync_RenamedFile_Succeeds(string src, string dst)
        {
            var sourceFile = Path.Combine(src, "file1.txt");
            var movedSourceFile = sourceFile.Replace("file1.txt", "file22.txt");
            var expectedFile = Path.Combine(dst, "file1.txt");
            var movedExpectedFile = expectedFile.Replace("file1.txt", "file22.txt");

            File.WriteAllText(sourceFile, "testcontent1");

            await _client.Sync();

            Assert.IsTrue(File.Exists(expectedFile), "File was not created");
            Assert.IsTrue(FilesEqual(sourceFile, expectedFile), "File's content was not synced");

            File.Move(sourceFile, movedSourceFile);

            await _client.Sync();

            Assert.IsFalse(File.Exists(expectedFile), "Old file was not deleted");
            Assert.IsTrue(File.Exists(movedExpectedFile), "File was not created");
            Assert.IsTrue(FilesEqual(movedSourceFile, movedExpectedFile), "File's content was not synced");
        }

        [TestMethod]
        public async Task TwoWaySync_NewDifferentOnBothClientFirst_Succeeds_Test()
        {
            await TwoWaySync_NewOnBoth_Succeeds(_clientFolder, _serverFolder);
        }

        [TestMethod]
        public async Task TwoWaySync_NewDifferentOnBothServerFirst_Succeeds_Test()
        {
            await TwoWaySync_NewOnBoth_Succeeds(_serverFolder, _clientFolder);
        }

        private async Task TwoWaySync_NewOnBoth_Succeeds(string src, string dst)
        {
            var sourceFile = Path.Combine(src, "file1.txt");
            var sourceFileAfterSync1 = sourceFile.Replace("file1.txt", "file1_FromServer.txt");
            var sourceFileAfterSync2 = sourceFile.Replace("file1.txt", "file1_FromClient.txt");
            var targetFile = Path.Combine(dst, "file1.txt");
            var targetFileAfterSync1 = targetFile.Replace("file1.txt", "file1_FromServer.txt");
            var targetFileAfterSync2 = targetFile.Replace("file1.txt", "file1_FromClient.txt");

            File.WriteAllText(sourceFile, "testcontent1");
            File.WriteAllText(targetFile, "testcontent2");

            await _client.Sync();

            Assert.IsTrue(File.Exists(sourceFileAfterSync1), "File was not created");
            Assert.IsTrue(File.Exists(sourceFileAfterSync2), "File was not created");
            Assert.IsTrue(File.Exists(targetFileAfterSync1), "File was not created");
            Assert.IsTrue(File.Exists(targetFileAfterSync2), "File was not created");

            Assert.IsFalse(File.Exists(sourceFile), "File was not deleted");
            Assert.IsFalse(File.Exists(targetFile), "File was not deleted");

            Assert.IsTrue(FilesEqual(sourceFileAfterSync1, targetFileAfterSync1), "File content mismatch");
            Assert.IsTrue(FilesEqual(sourceFileAfterSync2, targetFileAfterSync2), "File content mismatch");
        }

        [TestMethod]
        public async Task TwoWaySync_NewSameOnBothClientFirst_Succeeds_Test()
        {
            await TwoWaySync_NewSameOnBoth_Succeeds(_clientFolder, _serverFolder);
        }

        [TestMethod]
        public async Task TwoWaySync_NewSameOnBothServerFirst_Succeeds_Test()
        {
            await TwoWaySync_NewSameOnBoth_Succeeds(_serverFolder, _clientFolder);
        }

        private async Task TwoWaySync_NewSameOnBoth_Succeeds(string src, string dst)
        {
            var sourceFile = Path.Combine(src, "file1.txt");
            var targetFile = Path.Combine(dst, "file1.txt");

            File.WriteAllText(sourceFile, "testcontent1");
            File.WriteAllText(targetFile, "testcontent1");

            await _client.Sync();

            Assert.IsTrue(File.Exists(sourceFile), "File was not created");
            Assert.IsTrue(File.Exists(targetFile), "File was not created");

            Assert.IsTrue(FilesEqual(sourceFile, targetFile), "File content mismatch");
        }

        [TestMethod]
        public async Task TwoWaySync_NewEmptyOnBothClientFirst_Succeeds_Test()
        {
            await TwoWaySync_NewEmptyOnBoth_Succeeds(_clientFolder, _serverFolder);
        }

        [TestMethod]
        public async Task TwoWaySync_NewEmptyOnBothServerFirst_Succeeds_Test()
        {
            await TwoWaySync_NewEmptyOnBoth_Succeeds(_serverFolder, _clientFolder);
        }

        private async Task TwoWaySync_NewEmptyOnBoth_Succeeds(string src, string dst)
        {
            var sourceFile = Path.Combine(src, "file1.txt");
            var targetFile = Path.Combine(dst, "file1.txt");

            File.Create(sourceFile).Close();
            File.Create(targetFile).Close();

            await _client.Sync();

            Assert.IsTrue(File.Exists(sourceFile), "File was not created");
            Assert.IsTrue(File.Exists(targetFile), "File was not created");

            Assert.IsTrue(FilesEqual(sourceFile, targetFile), "File content mismatch");
        }

        [TestMethod]
        public async Task TwoWaySync_DeleteOnBothClientFirst_Succeeds_Test()
        {
            await TwoWaySync_DeleteOnBoth_Succeeds(_clientFolder, _serverFolder);
        }

        [TestMethod]
        public async Task TwoWaySync_DeleteOnBothServerFirst_Succeeds_Test()
        {
            await TwoWaySync_DeleteOnBoth_Succeeds(_serverFolder, _clientFolder);
        }

        private async Task TwoWaySync_DeleteOnBoth_Succeeds(string src, string dst)
        {
            var sourceFile = Path.Combine(src, "file1.txt");
            var targetFile = Path.Combine(dst, "file1.txt");

            File.WriteAllText(sourceFile, "c1");

            await _client.Sync();

            Assert.IsTrue(File.Exists(targetFile), "File was not created");
            Assert.IsTrue(FilesEqual(sourceFile, targetFile), "File content mismatch");

            File.Delete(sourceFile);
            File.Delete(targetFile);

            await _client.Sync();
        }

        [TestMethod]
        public async Task TwoWaySync_DeleteOnFirstEditSecondClientFirst_Succeeds_Test()
        {
            await TwoWaySync_DeleteOnFirstEditSecond_Succeeds(_clientFolder, _serverFolder, "_FromServer");
        }

        [TestMethod]
        public async Task TwoWaySync_DeleteOnFirstEditSecondServerFirst_Succeeds_Test()
        {
            await TwoWaySync_DeleteOnFirstEditSecond_Succeeds(_serverFolder, _clientFolder, "_FromClient");
        }

        private async Task TwoWaySync_DeleteOnFirstEditSecond_Succeeds(string src, string dst, string suffix)
        {
            var sourceFile = Path.Combine(src, "file1.txt");
            var targetFile = Path.Combine(dst, "file1.txt");

            File.WriteAllText(sourceFile, "c1");

            await _client.Sync();

            Assert.IsTrue(File.Exists(targetFile), "File was not created");
            Assert.IsTrue(FilesEqual(sourceFile, targetFile), "File content mismatch");

            File.Delete(sourceFile);
            File.AppendAllText(targetFile, "cc12");

            await _client.Sync();

            sourceFile = sourceFile.Replace("file1.txt", $"file1{suffix}.txt");
            targetFile = targetFile.Replace("file1.txt", $"file1{suffix}.txt");

            Assert.IsTrue(File.Exists(sourceFile), "File was not rolled back from other side");
            Assert.IsTrue(File.Exists(targetFile), "File was not renamed");
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
                        LocalPath = _serverFolder,
                    }
                }
            });

            SessionStorage.Instance.SetTimeout(0);

            _server = new SyncServer(ServerPort, Guid.Empty, _testServerConfig);
            _server.Msg += ServerWrite;
            _server.Start();

            var serverEp = new IPEndPoint(IPAddress.Parse("127.0.0.1"), ServerPort);

            _client = SyncClientFactory.GetTwoWay(serverEp, _clientFolder, null, Guid.Empty, Guid.Empty);
            _client.Log += ClientWrite;
            _client.ErrorEvent += ClientOnErrorEvent;
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

            _clientFolder = Path.Combine(_testsRoot, "client");
            Directory.CreateDirectory(_clientFolder);

            _serverFolder = Path.Combine(_testsRoot, "server");
            Directory.CreateDirectory(_serverFolder);
        }

        private static void ServerWrite(string s) => Console.WriteLine($"server: {s}");

        private static void ClientWrite(string s) => Console.WriteLine($"client: {s}");

        private static void ClientOnErrorEvent(string msg) => Assert.Fail($"client error: {msg}");

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