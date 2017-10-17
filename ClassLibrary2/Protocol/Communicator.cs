using System;
using System.IO;
using System.Net;
using System.Runtime.Serialization.Formatters.Binary;
using NetworkCommsDotNet;
using NetworkCommsDotNet.Connections;
using NetworkCommsDotNet.Connections.TCP;

namespace Common.Protocol
{
    public class Communicator
    {
        private readonly string _ipAddress;
        private readonly Lazy<Connection> _connectionContainer;

        public Communicator(string ipAddress)
        {
            _ipAddress = ipAddress;
            _connectionContainer = new Lazy<Connection>(CreateConnection);
        }

        private Connection CreateConnection()
        {
            var inf = new ConnectionInfo(_ipAddress, 9211);
            var connection = TCPConnection.GetConnection(inf);
            return connection;
        }

        public void AppendHandler<TIn, TOut>(Command<TIn, TOut> cmd, Func<TIn, TOut> handler)
        {
            NetworkComms.PacketHandlerCallBackDelegate<byte[]> delegat = (h, c, o) =>
                HandlePacket(h, c, o, cmd, i => handler(i) as object);
            NetworkComms.AppendGlobalIncomingPacketHandler(cmd.Query, delegat);
            Connection.StartListening(ConnectionType.TCP, new IPEndPoint(IPAddress.Parse(_ipAddress), 9211));
        }

        private void HandlePacket<T1, T2>(PacketHeader packetHeader, Connection connection, byte[] incomingObject, Command<T1, T2> cmd, Func<T1, object> handler)
        {
            var input = Deserialize<T1>(incomingObject);
            var ret = handler(input);
            var data = Serialize(ret);
            connection.SendObject(cmd.Response, data);
        }


        private static byte[] Serialize<T>(T data)
        {
            using (var ms = new MemoryStream())
            {
                var formatter = new BinaryFormatter();
                formatter.Serialize(ms, data);
                return ms.ToArray();
            }
        }

        private static T Deserialize<T>(byte[] data)
        {
            using (var ms = new MemoryStream())
            {
                ms.Write(data, 0, data.Length);
                ms.Seek(0, SeekOrigin.Begin);
                var formatter = new BinaryFormatter();
                var obj = (T) formatter.Deserialize(ms);
                return obj;
            }
        }

        public TOut SendReceiveCommand<TIn, TOut>(Command<TIn, TOut> cmd, TIn arg)
            where TIn : class
        {
            var argArray = Serialize(arg);
            var dataArray = _connectionContainer.Value.SendReceiveObject<byte[], byte[]>(cmd.Query, cmd.Response, 1000, argArray);
            var ret = Deserialize<TOut>(dataArray);
            return ret;
        }

        public void Shutdown()
        {
            NetworkComms.Shutdown();
            if (_connectionContainer.IsValueCreated)
            {
                _connectionContainer.Value.CloseConnection(false);
                _connectionContainer.Value.Dispose();
            }
        }
    }
}