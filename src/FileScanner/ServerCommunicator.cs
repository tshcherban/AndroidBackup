using System;
using System.IO;
using System.Net;
using System.Net.Sockets;
using System.Threading.Tasks;
using FileSync.Android.Model;
using FileSync.Common;

namespace FileSync.Android
{
    public class ServerCommunicator
    {
        private const int OperationsTimeout = 5000;

        private async Task<ServerResponseWithData<Guid>> GetId(Stream networkStream)
        {
            await NetworkHelperSequential.WriteCommandHeader(networkStream, Commands.GetIdCmd);

            var cmdHeader = await NetworkHelperSequential.ReadCommandHeader(networkStream);
            if (cmdHeader.Command != Commands.GetIdCmd)
            {
                return new ServerResponseWithData<Guid> { ErrorMsg = "Wrong command received" };
            }

            if (cmdHeader.PayloadLength == 0)
            {
                return new ServerResponseWithData<Guid> { ErrorMsg = "No data received" };
            }

            var responseBytes = await NetworkHelperSequential.ReadBytes(networkStream, cmdHeader.PayloadLength);
            var response = Serializer.Deserialize<ServerResponseWithData<Guid>>(responseBytes);

            return response;
        }

        public async Task<bool> PingServer(ServerListDataItem server)
        {
            var serverEp = Extensions.ParseEndpoint(server.Address);
            var id = await GetServerId(serverEp.Address, serverEp.Port);
            return id == server.Id;
        }

        public async Task<Guid?> GetServerId(IPAddress address, int port)
        {
            try
            {
                using (var client = new TcpClient{ReceiveTimeout = OperationsTimeout, SendTimeout = OperationsTimeout})
                {
                    var success = await client.ConnectAsync(address, port).WhenOrTimeout(OperationsTimeout);
                    if (!success)
                    {
                        //Log?.Invoke("Connecting to server timed out");
                        return null;
                    }

                    using (var networkStream = client.GetStream())
                    {

                        var sessionId = await GetId(networkStream);
                        if (sessionId.HasError)
                        {
                            //Log?.Invoke($"Unable to create sync session. Server response was '{sessionId.ErrorMsg}'");
                            return null;
                        }

                        await NetworkHelperSequential.WriteCommandHeader(networkStream, Commands.DisconnectCmd);

                        return sessionId.Data;
                    }
                }
            }
            catch (Exception e)
            {
                //Log?.Invoke($"Error during sync {e}");
                return null;
            }
        }
    }
}