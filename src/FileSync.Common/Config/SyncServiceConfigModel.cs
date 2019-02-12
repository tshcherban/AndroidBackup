using System;
using System.Collections.Generic;
using System.IO;
using Newtonsoft.Json;
using Newtonsoft.Json.Converters;

namespace FileSync.Common.Config
{
    public sealed class SyncServiceConfigStore
    {
        private const int MaxConfigSize = 1024 * 1024;

        private readonly string _filePath;

        public SyncServiceConfigStore(string filePath)
        {
            _filePath = filePath;
        }

        public void Save(SyncServiceConfigModel configModel)
        {
            var json = JsonConvert.SerializeObject(configModel, Formatting.Indented);

            File.WriteAllText(_filePath, json);
        }

        public void Save(SyncClientConfigModel configModel)
        {
            var json = JsonConvert.SerializeObject(configModel, Formatting.Indented);

            File.WriteAllText(_filePath, json);
        }

        private T ReadFromFile<T>(Func<T> defaultFactory)
        {
            if (!File.Exists(_filePath))
            {
                return defaultFactory();
            }

            var fi = new FileInfo(_filePath);
            if (fi.Length == 0 || fi.Length > MaxConfigSize)
            {
                return defaultFactory();
            }

            var text = File.ReadAllText(_filePath);

            try
            {
                return JsonConvert.DeserializeObject<T>(text);
            }
            catch
            {
                return defaultFactory();
            }
        }

        public SyncServiceConfigModel ReadServiceOrDefault()
        {
            return ReadFromFile(CreateDefaultForServer);
        }

        public SyncClientConfigModel ReadClientOrDefault()
        {
            return ReadFromFile(CreateDefaultForClient);
        }

        private static SyncClientConfigModel CreateDefaultForClient()
        {
            return new SyncClientConfigModel
            {
                Pairs = new List<SyncPairConfigModel>()
            };
        }

        private static SyncServiceConfigModel CreateDefaultForServer()
        {
            return new SyncServiceConfigModel
            {
                Endpoints = new List<SyncEndpointConfigModel>(),
                ServerConfig = new SyncServerConfigModel(),
            };
        }
    }

    public sealed class SyncClientConfigModel
    {
        public List<SyncPairConfigModel> Pairs { get; set; }
    }

    public sealed class SyncPairConfigModel
    {
        public string ServerAddress { get; set; }

        public string BaseDir { get; set; }

        public string DbDir { get; set; }

        [JsonConverter(typeof(StringEnumConverter))]
        public SyncMode SyncMode { get; set; }
    }

    public sealed class SyncServiceConfigModel
    {
        public SyncServerConfigModel ServerConfig { get; set; }


        public List<SyncEndpointConfigModel> Endpoints { get; set; }
    }

    public sealed class SyncServerConfigModel
    {
        public int Port { get; set; }
    }

    public sealed class SyncEndpointConfigModel
    {
        public string BaseDir { get; set; }

        public string DbDir { get; set; }

        [JsonConverter(typeof(StringEnumConverter))]
        public SyncMode SyncMode { get; set; }
    }

    public enum SyncMode
    {
        TwoWay,
    }
}