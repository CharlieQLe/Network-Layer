using System.Net;
using System.Net.Sockets;
using LiteNetLib;
using LiteNetLib.Utils;
using NetworkLayer.Utils;

namespace NetworkLayer.Transport.LiteNetLib {
    public class LiteNetLibClientTransport : NetworkClient.BaseTransport, INetEventListener {
        public delegate void WriteConnectionDataDelegate(NetDataWriter writer);
        
        private readonly Message _message;
        private readonly NetManager _netManager;
        private readonly NetDataWriter _dataWriter;

        /// <summary>
        /// Invoked on a network error.
        /// </summary>
        public event EventBasedNetListener.OnNetworkError NetworkErrorEvent;
        
        /// <summary>
        /// Invoked when receiving data from an unconnected endpoint.
        /// </summary>
        public event EventBasedNetListener.OnNetworkReceiveUnconnected NetworkReceiveUnconnectedEvent;
        
        /// <summary>
        /// Invoked when the latency is updated.
        /// </summary>
        public event EventBasedNetListener.OnNetworkLatencyUpdate NetworkLatencyUpdateEvent;
        
        public LiteNetLibClientTransport() {
            _message = new Message();
            _netManager = new NetManager(this);
            _netManager.Start();
            _dataWriter = new NetDataWriter();
        }
        
        /// <summary>
        /// Try to connect to the server with connection data.
        /// </summary>
        /// <param name="address"></param>
        /// <param name="port"></param>
        /// <param name="writeToConnectionData"></param>
        public void Connect(string address, ushort port, WriteConnectionDataDelegate writeToConnectionData) {
            // Do nothing if already connected or connecting
            if (State != EClientState.Disconnected) return;
            
            // Write the connection data
            _dataWriter.Reset();
            writeToConnectionData?.Invoke(_dataWriter);
            
            // Connect
            _netManager.Connect(address, port, _dataWriter);
            
            // Raise the start connecting event
            OnStartConnecting();
        }

        /// <summary>
        /// Disconnect from the server with disconnect data.
        /// </summary>
        /// <param name="writeToDisconnectData"></param>
        public void Disconnect(WriteDisconnectDataDelegate writeToDisconnectData) {
            // Do nothing if already disconnected
            if (State == EClientState.Disconnected) return;
            
            // Write the disconnection data
            _dataWriter.Reset();
            writeToDisconnectData?.Invoke(_dataWriter);
            
            // Disconnect the peer
            _netManager.FirstPeer.Disconnect(_dataWriter);
        }
        
        public override EClientState State => _netManager.FirstPeer != null ? LiteNetLibUtility.ConvertConnectionState(_netManager.FirstPeer.ConnectionState) : EClientState.Disconnected;
        
        public override int Rtt => _netManager.FirstPeer == null ? -1 : _netManager.FirstPeer.Ping * 2;

        public override void Connect(string address, ushort port) => Connect(address, port, null);

        public override void Disconnect() => Disconnect(null);

        protected override void Update() {
            // Do nothing if the manager is not running
            if (!_netManager.IsRunning) return;
            
            // Poll for events
            _netManager.PollEvents();
            
            // Invoke the update event
            if (State != EClientState.Connected) OnUpdate();
        }

        public override void Dispose() => _netManager.Stop(true); // Stop the net manager

        public override void Send(string messageName, WriteToMessageDelegate writeToMessage, ESendMode sendMode) {
            // Do nothing if not connected
            if (State != EClientState.Connected) return;
            
            // Write to the message
            _message.Reset();
            _message.AsWriter.PutUInt(Hash32.Generate(messageName));
            writeToMessage(_message.AsWriter);
            
            // Write to the data writer
            _dataWriter.Reset();
            _dataWriter.PutBytesWithLength(_message.Data, 0, _message.Length);
            
            // Send message
            _netManager.FirstPeer.Send(_dataWriter, LiteNetLibUtility.ConvertSendMode(sendMode));
        }

        void INetEventListener.OnPeerConnected(NetPeer peer) => OnConnect();

        void INetEventListener.OnPeerDisconnected(NetPeer peer, DisconnectInfo disconnectInfo) => OnDisconnect();

        void INetEventListener.OnNetworkError(IPEndPoint endPoint, SocketError socketError) => NetworkErrorEvent?.Invoke(endPoint, socketError);

        void INetEventListener.OnNetworkReceive(NetPeer peer, NetPacketReader reader, DeliveryMethod deliveryMethod) {
            // Copy data to the message
            _message.Reset();
            _message.Resize(reader.UserDataSize);
            int count = reader.GetInt();
            reader.GetBytes(_message.Data, 0, count);
            
            // Raise the message receive event
            OnReceiveMessage(_message.AsReader);
        }

        void INetEventListener.OnNetworkReceiveUnconnected(IPEndPoint remoteEndPoint, NetPacketReader reader, UnconnectedMessageType messageType) => NetworkReceiveUnconnectedEvent?.Invoke(remoteEndPoint, reader, messageType);

        void INetEventListener.OnNetworkLatencyUpdate(NetPeer peer, int latency) => NetworkLatencyUpdateEvent?.Invoke(peer, latency);

        void INetEventListener.OnConnectionRequest(ConnectionRequest request) { }
    }
}