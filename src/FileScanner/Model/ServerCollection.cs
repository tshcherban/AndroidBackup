using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using FileSync.Android.Helpers;

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
        private bool _pingRunning;

        public void RunPing(CancellationToken? token = null)
        {
            if (_pingRunning)
                return;

            _pingRunning = true;

            if (token == null)
                token = CancellationToken.None;

            Task.Run(async () =>
            {
                while (_pingRunning && !token.Value.IsCancellationRequested)
                {
                    await Task.Delay(1000, token.Value);
                    if (!_pingRunning || token.Value.IsCancellationRequested)
                        break;

                    if (itemsList.Count == 0)
                        continue;

                    try
                    {
                        var changed = false;

                        for (var index = itemsList.Count - 1; index >= 0; index--)
                        {
                            var server = itemsList[index];
                            changed |= await DoPing(server);
                        }

                        if (changed)
                            OnDataUpdated();
                    }
                    catch (TaskCanceledException)
                    {
                        break;
                    }
                    catch (Exception)
                    {
                        await Task.Delay(5000, token.Value);
                    }
                }

                _pingRunning = false;
            });
        }

        private async Task<bool> DoPing(ServerListDataItem server)
        {
            var comm = new ServerCommunicator();
            var prevState = server.State;
            server.State = await comm.PingServer(server);
            return server.State != prevState;
        }

        public void StopPing()
        {
            _pingRunning = false;
        }
    }
}