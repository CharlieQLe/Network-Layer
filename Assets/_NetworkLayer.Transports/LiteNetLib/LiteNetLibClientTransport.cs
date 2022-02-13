using System;
using System.Net;
using System.Net.Sockets;
using LiteNetLib;
using LiteNetLib.Utils;
using NetworkLayer.Utils;
using UnityEngine;

namespace NetworkLayer.Transport.LiteNetLib {
    public class LiteNetLibClientTransport : NetworkClient.BaseTransport, INetEventListener {
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
        
        public LiteNetLibClientTransport() {
            _message = new Message();
            _netManager = new NetManager(this);
            _netManager.Start();
            _dataWriter = new NetDataWriter();
        }
        
        public void SetWriteConnectionData(WriteConnectionDataDelegate writeConnectionDataDelegate) => _writeConnectionData = writeConnectionDataDelegate;

        public void SetWriteDisconnectData(WriteDisconnectDataDelegate writeDisconnectDataDelegate) => _writeDisconnectData = writeDisconnectDataDelegate;
        
        private EClientState ConvertConnectionState(ConnectionState state) => state switch {
            ConnectionState.Connected => EClientState.Connected,
            ConnectionState.Outgoing => EClientState.Connecting,
            _ => EClientState.Disconnected
        };

        public override EClientState State => _serverPeer != null ? ConvertConnectionState(_serverPeer.ConnectionState) : EClientState.Disconnected;
        public override int Rtt => _serverPeer == null ? -1 : _serverPeer.Ping * 2;

        public override void Connect(string address, ushort port) {
            if (State != EClientState.Disconnected) return;
            _dataWriter.Reset();
            _writeConnectionData?.Invoke(_dataWriter);
            _serverPeer = _netManager.Connect(address, port, _dataWriter);
            OnStartConnecting();
        }

        public override void Disconnect() {
            if (State == EClientState.Disconnected) return;
            _dataWriter.Reset();
            _writeDisconnectData?.Invoke(_dataWriter);
            _serverPeer.Disconnect(_dataWriter);
        }

        protected override void Update() {
            if (!_netManager.IsRunning) return;
            _netManager.PollEvents();
            if (State != EClientState.Disconnected) OnUpdate();
        }

        public override void Dispose() {
            _netManager.Stop(true);
            _serverPeer = null;
        }

        public override void Send(string messageName, WriteToMessageDelegate writeToMessage, ESendMode sendMode) {
            if (State != EClientState.Connected) return;
            _message.Reset();
            _message.AsWriter.PutUInt(Hash32.Generate(messageName));
            writeToMessage(_message.AsWriter);
            _dataWriter.Reset();
            _dataWriter.PutBytesWithLength(_message.Data, 0, _message.Length);
            _serverPeer.Send(_dataWriter, sendMode == ESendMode.Reliable ? DeliveryMethod.ReliableOrdered : DeliveryMethod.Unreliable);
        }

        void INetEventListener.OnPeerConnected(NetPeer peer) => OnConnect();

        void INetEventListener.OnPeerDisconnected(NetPeer peer, DisconnectInfo disconnectInfo) {
            _serverPeer = null;
            OnDisconnect();
        }

        void INetEventListener.OnNetworkError(IPEndPoint endPoint, SocketError socketError) => NetworkErrorEvent?.Invoke(endPoint, socketError);

        void INetEventListener.OnNetworkReceive(NetPeer peer, NetPacketReader reader, DeliveryMethod deliveryMethod) {
            _message.Reset();
            _message.Resize(reader.UserDataSize);
            int count = reader.GetInt();
            reader.GetBytes(_message.Data, 0, count);
            OnReceiveMessage(_message.AsReader);
        }

        void INetEventListener.OnNetworkReceiveUnconnected(IPEndPoint remoteEndPoint, NetPacketReader reader, UnconnectedMessageType messageType) => NetworkReceiveUnconnectedEvent?.Invoke(remoteEndPoint, reader, messageType);

        void INetEventListener.OnNetworkLatencyUpdate(NetPeer peer, int latency) => NetworkLatencyUpdateEvent?.Invoke(peer, latency);

        void INetEventListener.OnConnectionRequest(ConnectionRequest request) { }
    }
}