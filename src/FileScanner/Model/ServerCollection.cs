using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace FileSync.Android.Model
{
    public abstract class ServerCollection
    {
        protected readonly List<ServerListDataItem> itemsList;

        public IReadOnlyList<ServerListDataItem> Items => itemsList;

        public event Action DataUpdated;

        protected ServerCollection()
        {
            itemsList = new List<ServerListDataItem>();
        }

        public void SetServerListFromConfig(IReadOnlyCollection<ServerConfigItem> configServers)
        {
            itemsList.Clear();
            itemsList.AddRange(configServers.Select(x => new ServerListDataItem
            {
                Address = x.Url,
                Id = x.Id,
            }));

            DataUpdated?.Invoke();
        }

        protected void OnDataUpdated()
        {
            DataUpdated?.Invoke();
        }
    }

    public class ServerCollectionDiscovery : ServerCollection
    {
        public void AddServer(ServerListDataItem server)
        {
            if (itemsList.All(x => x.Id != server.Id))
            {
                itemsList.Add(server);
                OnDataUpdated();
            }
        }
    }

    public class ServerCollectionPing : ServerCollection
    {
        private bool _running;

        public void RunPing(CancellationToken? token = null)
        {
            if (_running)
                return;

            _running = true;

            if (token == null)
                token = CancellationToken.None;

            Task.Run(async () =>
            {
                try
                {
                    while (!token.Value.IsCancellationRequested)
                    {
                        await Task.Delay(1000, token.Value);
                        if (token.Value.IsCancellationRequested)
                            break;

                        if (itemsList.Count == 0)
                            continue;
                        
                        var changed = false;

                        foreach (var server in itemsList)
                            changed |= await DoPing(server);

                        if (changed)
                            OnDataUpdated();
                    }
                }
                catch (TaskCanceledException)
                {
                }
            });
        }

        private async Task<bool> DoPing(ServerListDataItem server)
        {
            var comm = new ServerCommunicator();
            var prevState = server.State;
            server.State = await comm.PingServer(server);
            return server.State != prevState;
        }
    }
}