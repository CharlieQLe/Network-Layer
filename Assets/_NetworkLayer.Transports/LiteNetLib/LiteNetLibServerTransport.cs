using System;
using System.Collections.Generic;
using System.Net;
using System.Net.Sockets;
using LiteNetLib;
using LiteNetLib.Utils;
using NetworkLayer.Utils;

namespace NetworkLayer.Transport.LiteNetLib {
    public class LiteNetLibServerTransport : ServerTransport, INetEventListener {
        public delegate void HandleConnectionAcceptDelegate(ConnectionRequest request);
        public delegate void WriteDisconnectDataDelegate(Message.Writer writer);

        private readonly Message _message;
        private readonly NetManager _netManager;
        private readonly NetDataWriter _dataWriter;
        private readonly Dictionary<ulong, NetPeer> _peers;
        private HandleConnectionAcceptDelegate _handleConnectionAccept;
        private WriteDisconnectDataDelegate _writeDisconnectData;

        public event EventBasedNetListener.OnNetworkError NetworkErrorEvent;
        public event EventBasedNetListener.OnNetworkReceiveUnconnected NetworkReceiveUnconnectedEvent;
        public event EventBasedNetListener.OnNetworkLatencyUpdate NetworkLatencyUpdateEvent;

        public LiteNetLibServerTransport(uint messageGroupId) : base(messageGroupId) {
            _message = new Message();
            _netManager = new NetManager(this);
            _dataWriter = new NetDataWriter();
            _peers = new Dictionary<ulong, NetPeer>();
            _handleConnectionAccept = request => request.Accept();
        }

        public LiteNetLibServerTransport(string messageGroupName) : this(Hash32.Generate(messageGroupName)) { }

        public override bool IsRunning => _netManager.IsRunning;

        public override void Host(ushort port) {
            if (IsRunning) {
                OnLog("Server - Cannot host. Reason: already hosting!");
                return;
            }
            _netManager.Start(port);
            OnHost();
        }

        public override void Close() {
            if (!IsRunning) {
                OnLog("Server - Cannot close server. Reason: server already closed!");
                return;
            }
            _message.Reset();
            _writeDisconnectData?.Invoke(_message.AsWriter);
            _netManager.DisconnectAll(_message.Data, 0, _message.Length);
            _netManager.Stop();
            _peers.Clear();
            OnClose();
        }

        public override void Disconnect(ulong client) {
            if (!IsRunning) {
                OnLog($"Server - Cannot disconnect client {client}. Reason: server is closed!");
                return;
            }
            if (!_peers.TryGetValue(client, out NetPeer peer)) {
                OnLog($"Server - Cannot disconnect client {client}. Reason: client not found!");
                return;
            }
            _netManager.DisconnectPeer(peer);
        }

        public override void Update() {
            if (!IsRunning) return;
            _netManager.PollEvents();
            OnUpdate();
        }

        public override void Dispose() {
            Close();
        }

        public override void SendMessageToAll(uint messageId, WriteMessageDelegate writeMessage, ESendMode sendMode) {
            if (!IsRunning) {
                OnLog($"Server - Cannot send message {messageId} to all clients. Reason: server not running!");
                return;
            }
            _message.Reset();
            _message.AsWriter.PutUInt(messageId);
            writeMessage(_message.AsWriter);
            _dataWriter.Reset();
            _dataWriter.PutBytesWithLength(_message.Data, 0, _message.Length);
            _netManager.SendToAll(_dataWriter, sendMode == ESendMode.Reliable ? DeliveryMethod.ReliableSequenced : DeliveryMethod.Unreliable);
        }

        public override void SendMessageToFilter(FilterClientDelegate filter, uint messageId, WriteMessageDelegate writeMessage, ESendMode sendMode) {
            if (!IsRunning) {
                OnLog($"Server - Cannot send message {messageId} to filtered clients. Reason: server not running!");
                return;
            }
            _message.Reset();
            _message.AsWriter.PutUInt(messageId);
            writeMessage(_message.AsWriter);
            _dataWriter.Reset();
            _dataWriter.PutBytesWithLength(_message.Data, 0, _message.Length);
            DeliveryMethod deliveryMethod = sendMode == ESendMode.Reliable ? DeliveryMethod.ReliableSequenced : DeliveryMethod.Unreliable;
            foreach (KeyValuePair<ulong, NetPeer> item in _peers)
                if (filter(item.Key))
                    item.Value.Send(_dataWriter, deliveryMethod);
        }

        public override void SendMessageToClient(ulong client, uint messageId, WriteMessageDelegate writeMessage, ESendMode sendMode) {
            if (!IsRunning) {
                OnLog($"Server - Cannot send message {messageId} to client. Reason: server not running!");
                return;
            }
            if (!_peers.TryGetValue(client, out NetPeer peer)) {
                OnLog($"Server - Cannot send message {messageId} to client. Reason: client {client} not found!");
                return;
            }
            _message.Reset();
            _message.AsWriter.PutUInt(messageId);
            writeMessage(_message.AsWriter);
            _dataWriter.Reset();
            _dataWriter.PutBytesWithLength(_message.Data, 0, _message.Length);
            peer.Send(_dataWriter, sendMode == ESendMode.Reliable ? DeliveryMethod.ReliableSequenced : DeliveryMethod.Unreliable);
        }

        void INetEventListener.OnPeerConnected(NetPeer peer) {
            _peers[(ulong) peer.Id] = peer;
            OnLog($"Server - Client {(ulong) peer.Id} connected!");
            OnConnect((ulong) peer.Id);
        }

        void INetEventListener.OnPeerDisconnected(NetPeer peer, DisconnectInfo disconnectInfo) {
            _peers.Remove((ulong) peer.Id);
            OnLog($"Server - Client {(ulong) peer.Id} disconnected!");
            OnDisconnect((ulong) peer.Id);
        }

        void INetEventListener.OnNetworkError(IPEndPoint endPoint, SocketError socketError) => NetworkErrorEvent?.Invoke(endPoint, socketError);

        void INetEventListener.OnNetworkReceive(NetPeer peer, NetPacketReader reader, DeliveryMethod deliveryMethod) {
            _message.Reset();
            _message.Resize(reader.UserDataSize);
            int count = reader.GetInt();
            reader.GetBytes(_message.Data, 0, count);
            try {
                OnReceiveMessage((ulong) peer.Id, _message.AsReader);
            } catch (Exception exception) {
                OnLog($"Server - Error trying to receive data from client {(ulong) peer.Id}! Message: {exception}");
            }
        }

        void INetEventListener.OnNetworkReceiveUnconnected(IPEndPoint remoteEndPoint, NetPacketReader reader, UnconnectedMessageType messageType) => NetworkReceiveUnconnectedEvent?.Invoke(remoteEndPoint, reader, messageType);

        void INetEventListener.OnNetworkLatencyUpdate(NetPeer peer, int latency) => NetworkLatencyUpdateEvent?.Invoke(peer, latency);


        void INetEventListener.OnConnectionRequest(ConnectionRequest request) => _handleConnectionAccept?.Invoke(request);
    }
}