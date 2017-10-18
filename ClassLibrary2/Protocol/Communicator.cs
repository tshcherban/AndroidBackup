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

        private bool _isListening;

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

        public void AppendReceiveSendHandler<TIn, TOut>(Command<TIn, TOut> cmd, Func<TIn, TOut> handler)
            where TIn : class
            where TOut : class
        {
            void HandlePacket(PacketHeader header, Connection connection, byte[] input) =>
                ReceiveSendResponse(connection, input, cmd, handler);

            NetworkComms.AppendGlobalIncomingPacketHandler(cmd.Query, (NetworkComms.PacketHandlerCallBackDelegate<byte[]>) HandlePacket);

            if (!_isListening)
            {
                Connection.StartListening(ConnectionType.TCP, new IPEndPoint(IPAddress.Parse(_ipAddress), 9211));
                _isListening = true;
            }
        }

        private static void ReceiveSendResponse<TIn, TOut>(Connection connection, byte[] incomingObject, Command<TIn, TOut> cmd, Func<TIn, object> handler)
            where TIn : class
            where TOut : class
        {
            var input = Deserialize<TIn>(incomingObject);
            var ret = handler(input);
            var data = Serialize(ret);
            connection.SendObject(cmd.Response, data);
        }

        public TOut SendReceiveCommand<TIn, TOut>(Command<TIn, TOut> cmd, TIn arg)
            where TIn : class
            where TOut : class
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

        private static readonly byte[] Empty = new byte[0];

        private static byte[] Serialize<T>(T data)
            where T : class
        {
            if (data == null)
                return Empty;

            using (var ms = new MemoryStream())
            {
                var formatter = new BinaryFormatter();
                formatter.Serialize(ms, data);
                return ms.ToArray();
            }
        }

        private static T Deserialize<T>(byte[] data)
            where T : class
        {
            if (data == null || data.Length == 0)
                return null;

            using (var ms = new MemoryStream())
            {
                ms.Write(data, 0, data.Length);
                ms.Seek(0, SeekOrigin.Begin);
                var formatter = new BinaryFormatter();
                var obj = (T) formatter.Deserialize(ms);
                return obj;
            }
        }
    }
}