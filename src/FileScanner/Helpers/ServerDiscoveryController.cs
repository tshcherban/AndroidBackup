using System;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Threading.Tasks;
using FileSync.Android.Activities;
using FileSync.Android.Model;
using FileSync.Common;

namespace FileSync.Android.Helpers
{
    internal sealed class ServerDiscoveryController
    {
        private const int DiscoveryTimeout = 4000;

        public event Action<string> Log;

        public async Task<ServerListDataItem> Discover()
        {
            var ipAddress = IPAddress.Broadcast;
            //var ipAddress = IPAddress.Parse("10.0.2.0"); // for android emulator
            try
            {
                Log?.Invoke("Discovering...");

                using (var client = new UdpClient())
                {
                    var requestData = Encoding.ASCII.GetBytes("sync-service");

                    client.EnableBroadcast = true;
                    
                    var sendResult = await client.SendAsync(requestData, requestData.Length, new IPEndPoint(ipAddress, 8888)).WhenOrTimeout(DiscoveryTimeout);
                    if (!sendResult.Item1)
                    {
                        Log?.Invoke("Discovery request timeout");
                        return null;
                    }
                    var serverResponseData = await client.ReceiveAsync().WhenOrTimeout(DiscoveryTimeout);
                    if (!serverResponseData.Item1)
                    {
                        Log?.Invoke("Discovery response wait timeout");
                        return null;
                    }

                    var serverResponse = Encoding.ASCII.GetString(serverResponseData.Item2.Buffer);
                    var parts = serverResponse.Split('|');
                    var port = int.Parse(parts[0].Replace("port:", null));
                    var id = Guid.ParseExact(parts[1].Replace("id:", null).Trim(), "D");

                    var ss = $"Discovered on {serverResponseData.Item2.RemoteEndPoint.Address}:{port}";
                    Log?.Invoke(ss);

                    var ep = new IPEndPoint(serverResponseData.Item2.RemoteEndPoint.Address, port);
                    return new ServerListDataItem
                    {
                        Address = ep.ToString(),
                        Id = id
                    };
                }
            }
            catch (Exception e)
            {
                Log?.Invoke($"Error while discovering\r\n{e}");

                return null;
            }
        }
    }
}