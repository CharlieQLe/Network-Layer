using System;
using System.Net;
using System.Net.Sockets;
using LiteNetLib;
using LiteNetLib.Utils;
using NetworkLayer.Utils;

namespace NetworkLayer.Transport.LiteNetLib {
    public class LiteNetLibClientTransport : ClientTransport, INetEventListener {
        public delegate void WriteConnectionDataDelegate(NetDataWriter writer);
        public delegate void WriteDisconnectDataDelegate(NetDataWriter writer);
        
        private readonly Message _message;
        private readonly NetManager _netManager;
        private readonly NetDataWriter _dataWriter;
        private NetPeer _serverPeer;
        private WriteConnectionDataDelegate _writeConnectionData;
        private WriteDisconnectDataDelegate _writeDisconnectData;

        public event EventBasedNetListener.OnNetworkError NetworkErrorEvent;
        public event EventBasedNetListener.OnNetworkReceiveUnconnected NetworkReceiveUnconnectedEvent;
        public event EventBasedNetListener.OnNetworkLatencyUpdate NetworkLatencyUpdateEvent;
        
        public LiteNetLibClientTransport(uint messageGroupId) : base(messageGroupId) {
            _message = new Message();
            _netManager = new NetManager(this);
            _netManager.Start();
            _dataWriter = new NetDataWriter();
        }
        
        public LiteNetLibClientTransport(string messageGroupName) : this(Hash32.Generate(messageGroupName)) { }

        public void WriteConnectionData(WriteConnectionDataDelegate writeConnectionDataDelegate) => _writeConnectionData = writeConnectionDataDelegate;

        public void WriteDisconnectData(WriteDisconnectDataDelegate writeDisconnectDataDelegate) => _writeDisconnectData = writeDisconnectDataDelegate;
        
        private EClientState ConvertConnectionState(ConnectionState state) => state switch {
            ConnectionState.Connected => EClientState.Connected,
            ConnectionState.Outgoing => EClientState.Connecting,
            _ => EClientState.Disconnected
        };

        public override EClientState State => _serverPeer != null ? ConvertConnectionState(_serverPeer.ConnectionState) : EClientState.Disconnected;
        public override int Rtt => _serverPeer == null ? -1 : _serverPeer.Ping * 2;

        public override void Connect(string address, ushort port) {
            if (State != EClientState.Disconnected) {
                OnLog("Client - Cannot attempt connection. Reason: already connecting/connected!");
                return;
            }
            _dataWriter.Reset();
            _writeConnectionData?.Invoke(_dataWriter);
            _serverPeer = _netManager.Connect(address, port, _dataWriter);
            OnLog("Client - Attempting to connect to the server...");
            OnAttemptConnection();
        }

        public override void Disconnect() {
            if (State == EClientState.Disconnected) {
                OnLog("Client - Cannot disconnect. Reason: already disconnected!");
                return;
            }
            _dataWriter.Reset();
            _writeDisconnectData?.Invoke(_dataWriter);
            _serverPeer.Disconnect(_dataWriter);
        }

        public override void Update() {
            if (!_netManager.IsRunning) return;
            _netManager.PollEvents();
            if (State != EClientState.Disconnected) OnUpdate();
        }

        public override void Dispose() {
            _netManager.Stop(true);
            _serverPeer = null;
        }

        public override void SendMessage(uint messageId, WriteMessageDelegate writeMessage, ESendMode sendMode) {
            if (State != EClientState.Connected) {
                OnLog($"Client - Cannot send message {messageId} to the server. Reason: not connected!");
                return;
            }
            _message.Reset();
            _message.AsWriter.PutUInt(messageId);
            writeMessage(_message.AsWriter);
            _dataWriter.Reset();
            _dataWriter.PutBytesWithLength(_message.Data, 0, _message.Length);
            _serverPeer.Send(_dataWriter, sendMode == ESendMode.Reliable ? DeliveryMethod.ReliableOrdered : DeliveryMethod.Unreliable);
        }

        void INetEventListener.OnPeerConnected(NetPeer peer) {
            OnLog("Client - Connected to the server!");
            OnConnect();
        }

        void INetEventListener.OnPeerDisconnected(NetPeer peer, DisconnectInfo disconnectInfo) {
            _serverPeer = null;
            OnLog("Client - Disconnected from the server!");
            OnDisconnect();
        }

        void INetEventListener.OnNetworkError(IPEndPoint endPoint, SocketError socketError) => NetworkErrorEvent?.Invoke(endPoint, socketError);

        void INetEventListener.OnNetworkReceive(NetPeer peer, NetPacketReader reader, DeliveryMethod deliveryMethod) {
            _message.Reset();
            _message.Resize(reader.UserDataSize);
            int count = reader.GetInt();
            reader.GetBytes(_message.Data, 0, count);
            try {
                OnReceiveMessage(_message.AsReader);
            } catch (Exception exception) {
                OnLog($"Client - Error trying to receive data! Message: {exception}");
            }
        }

        void INetEventListener.OnNetworkReceiveUnconnected(IPEndPoint remoteEndPoint, NetPacketReader reader, UnconnectedMessageType messageType) => NetworkReceiveUnconnectedEvent?.Invoke(remoteEndPoint, reader, messageType);

        void INetEventListener.OnNetworkLatencyUpdate(NetPeer peer, int latency) => NetworkLatencyUpdateEvent?.Invoke(peer, latency);

        void INetEventListener.OnConnectionRequest(ConnectionRequest request) { }
    }
}