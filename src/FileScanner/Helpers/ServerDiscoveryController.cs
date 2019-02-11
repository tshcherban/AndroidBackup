using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Threading.Tasks;
using Android.App;
using Android.Content;
using Android.OS;
using Android.Runtime;
using Android.Views;
using Android.Widget;

namespace FileSync.Android.Helpers
{
    internal sealed class ServerDiscoveryController
    {
        public event Action<string> Log;

        public async Task<IPEndPoint> ClientDiscover()
        {
            try
            {
                Log?.Invoke("Discovering...");

                using (var client = new UdpClient())
                {
                    var requestData = Encoding.ASCII.GetBytes("SomeRequestData");

                    client.EnableBroadcast = true;
                    var sendResult = await client.SendAsync(requestData, requestData.Length, new IPEndPoint(IPAddress.Broadcast, 8888)).WhenOrTimeout(10000);
                    if (!sendResult.Item1)
                    {
                        Log?.Invoke("Discovery request timeout");
                        return null;
                    }
                    var serverResponseData = await client.ReceiveAsync().WhenOrTimeout(10000);
                    if (!serverResponseData.Item1)
                    {
                        Log?.Invoke("Discovery response wait timeout");
                        return null;
                    }

                    var serverResponse = Encoding.ASCII.GetString(serverResponseData.Item2.Buffer);
                    var port = int.Parse(serverResponse.Replace("port:", null));

                    var ss = $"Discovered on {serverResponseData.Item2.RemoteEndPoint.Address}:{port}";
                    Log?.Invoke(ss);

                    return new IPEndPoint(serverResponseData.Item2.RemoteEndPoint.Address, port);
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