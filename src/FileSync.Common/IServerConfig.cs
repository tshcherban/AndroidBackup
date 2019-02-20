using System;
using System.Collections.Generic;
using System.IO;
using Newtonsoft.Json;

namespace FileSync.Common
{
    public interface IServerConfig
    {
        List<RegisteredClient> Clients { get; }

        void Load();

        void Store();
    }

    public sealed class JsonFileServerConfig : IServerConfig
    {
        [JsonIgnore]
        private readonly string _filePath;

        [JsonProperty("Clients")]
        public List<RegisteredClient> Clients { get; private set; }

        public void Load()
        {
            try
            {
                var json = File.ReadAllText(_filePath);
                var obj = JsonConvert.DeserializeObject<JsonFileServerConfig>(json);
                if (obj.Clients != null && obj.Clients.Count > 0)
                {
                    Clients.Clear();
                    Clients.AddRange(obj.Clients);
                }
            }
            catch (Exception e)
            {
                Console.WriteLine(e);
            }
        }

        public void Store()
        {
            var json = JsonConvert.SerializeObject(this, Formatting.Indented);
            File.WriteAllText(_filePath, json);
        }

        public JsonFileServerConfig(string filePath)
        {
            _filePath = filePath;
            Clients = new List<RegisteredClient>();
        }

        [JsonConstructor]
        private JsonFileServerConfig()
        {
        }
    }

    public class RegisteredClient
    {
        public Guid Id { get; set; }

        public List<ClientFolderEndpoint> FolderEndpoints { get; } = new List<ClientFolderEndpoint>();
    }

    [Serializable]
    public class ClientFolderEndpoint
    {
        public Guid Id { get; set; }

        public string DisplayName { get; set; }

        public string LocalPath { get; set; }
    }
}