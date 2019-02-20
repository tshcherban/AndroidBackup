using System;
using System.Collections.Generic;
using System.Linq;
using Newtonsoft.Json;

namespace FileSync.Android.Model
{
    public sealed class Config : IConfig
    {
        private readonly string _filePath;

        [JsonProperty("Servers")]
        private readonly List<ServerConfigItem> _servers;

        [JsonProperty("ClientId")]
        public Guid ClientId { get; private set; }

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

                ClientId = obj.ClientId == Guid.Empty ? Guid.NewGuid() : obj.ClientId;
            }
            catch (Exception e)
            {
                Console.WriteLine(e);
            }

            if (ClientId == Guid.Empty)
                ClientId = Guid.NewGuid();
        }
    }
}