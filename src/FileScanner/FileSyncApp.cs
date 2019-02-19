using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using Android.App;
using Android.Runtime;
using Newtonsoft.Json;

namespace FileSync.Android
{
    internal interface IServiceLocator
    {
        IConfig Config { get; }
        
        void Stop();
    }

    [Application]
    public sealed class FileSyncApp : Application, IServiceLocator
    {
        internal static IServiceLocator Instance { get; private set; }

        private readonly CancellationTokenSource _appShutdownTokenSrc;

        public FileSyncApp(IntPtr javaReference, JniHandleOwnership transfer) : base(javaReference, transfer)
        {
            if (Instance != null)
                throw new InvalidOperationException("App instance already assigned");

            Instance = this;

            var folderPath = Environment.GetFolderPath(Environment.SpecialFolder.Personal);
            var filePath = System.IO.Path.Combine(folderPath, "config.json");

            Config = new Config(filePath);

            _appShutdownTokenSrc = new CancellationTokenSource();
        }

        // must be overriden to call ctor(IntPtr, JniHandleOwnership)
        public override void OnCreate()
        {
            base.OnCreate();

            if (Instance == null)
                throw new InvalidOperationException("App instance was not assigned");

            Config.Load();
        }

        protected override void Dispose(bool disposing)
        {
            Stop();

            base.Dispose(disposing);
        }

        public IConfig Config { get; }
        
        public void Stop()
        {
            _appShutdownTokenSrc.Cancel(false);
        }
    }

    public interface IConfig
    {
        IReadOnlyCollection<ServerConfigItem> Servers { get; }

        ServerConfigItem AddServer(Guid id, string url);

        void Load();

        void Store();
        
        void RemoveServer(string serverAddress);
    }

    public class ServerConfigItem
    {
        public Guid Id { get; }

        public string Url { get; }

        public ServerConfigItem(string url, Guid id)
        {
            Url = url;
            Id = id;
        }
    }

    public sealed class Config : IConfig
    {
        private readonly string _filePath;

        [JsonProperty("Servers")]
        private readonly List<ServerConfigItem> _servers;

        public Config(string filePath)
        {
            _filePath = filePath;
            _servers = new List<ServerConfigItem>();
        }

        [JsonConstructor]
        private Config()
        {
        }

        [JsonIgnore]
        public IReadOnlyCollection<ServerConfigItem> Servers => _servers;

        public ServerConfigItem AddServer(Guid id, string url)
        {
            var serverConfigItem = new ServerConfigItem(url, id);
            _servers.Add(serverConfigItem);

            return serverConfigItem;
        }

        public void Store()
        {
            var json = JsonConvert.SerializeObject(this, Formatting.Indented);
            System.IO.File.WriteAllText(_filePath, json);
        }

        public void RemoveServer(string serverAddress)
        {
            var s = _servers.First(x => x.Url == serverAddress);
            _servers.Remove(s);
        }

        public void Load()
        {
            try
            {
                _servers.Clear();
                var obj = JsonConvert.DeserializeObject<Config>(System.IO.File.ReadAllText(_filePath));
                if (obj.Servers != null)
                    _servers.AddRange(obj.Servers);
            }
            catch (Exception e)
            {
                Console.WriteLine(e);
            }
        }
    }
}