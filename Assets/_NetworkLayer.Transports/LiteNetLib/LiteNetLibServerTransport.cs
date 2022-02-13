using System;
using System.Collections.Generic;
using System.Net;
using System.Net.Sockets;
using LiteNetLib;
using LiteNetLib.Utils;
using NetworkLayer.Utils;

namespace NetworkLayer.Transport.LiteNetLib {
    public class LiteNetLibServerTransport : NetworkServer.BaseTransport, INetEventListener {
        public delegate void HandleConnectionAcceptDelegate(ConnectionRequest request);
        public delegate void WriteDisconnectDataDelegate(Message.Writer writer);

        private readonly Message _message;
        private readonly NetManager _netManager;
        private readonly NetDataWriter _dataWriter;
        private readonly Dictionary<ulong, NetPeer> _peers;
        private readonly List<ulong> _clientIds;
        private HandleConnectionAcceptDelegate _handleConnectionAccept;
        private WriteDisconnectDataDelegate _writeDisconnectData;

        public event EventBasedNetListener.OnNetworkError NetworkErrorEvent;
        public event EventBasedNetListener.OnNetworkReceiveUnconnected NetworkReceiveUnconnectedEvent;
        public event EventBasedNetListener.OnNetworkLatencyUpdate NetworkLatencyUpdateEvent;

        public LiteNetLibServerTransport() {
            _message = new Message();
            _netManager = new NetManager(this);
            _dataWriter = new NetDataWriter();
            _peers = new Dictionary<ulong, NetPeer>();
            _clientIds = new List<ulong>();
            _handleConnectionAccept = request => request.Accept();
        }

        public override bool IsRunning => _netManager.IsRunning;
        public override int ClientCount => _clientIds.Count;

        public override void Host(ushort port) {
            if (IsRunning) return;
            _netManager.Start(port);
            OnHost();
        }

        public override void Close() {
            if (!IsRunning) return;
            _message.Reset();
            _writeDisconnectData?.Invoke(_message.AsWriter);
            _netManager.DisconnectAll(_message.Data, 0, _message.Length);
            _netManager.Stop();
            _peers.Clear();
            _clientIds.Clear();
            OnClose();
        }

        public override void Disconnect(ulong client) {
            if (!IsRunning || !_peers.TryGetValue(client, out NetPeer peer)) return;
            _netManager.DisconnectPeer(peer);
        }

        protected override void Update() {
            if (!IsRunning) return;
            _netManager.PollEvents();
            OnUpdate();
        }

        public override void Dispose() => Close();

        public override void Send(string messageName, WriteToMessageDelegate writeToMessage, ESendMode sendMode) {
            if (!IsRunning) return;
            _message.Reset();
            _message.AsWriter.PutUInt(Hash32.Generate(messageName));
            writeToMessage(_message.AsWriter);
            _dataWriter.Reset();
            _dataWriter.PutBytesWithLength(_message.Data, 0, _message.Length);
            _netManager.SendToAll(_dataWriter, sendMode == ESendMode.Reliable ? DeliveryMethod.ReliableSequenced : DeliveryMethod.Unreliable);
        }

        public override void Send(NetworkServer.SendFilterDelegate filter, string messageName, WriteToMessageDelegate writeToMessage, ESendMode sendMode) {
            if (!IsRunning) return;
            _message.Reset();
            _message.AsWriter.PutUInt(Hash32.Generate(messageName));
            writeToMessage(_message.AsWriter);
            _dataWriter.Reset();
            _dataWriter.PutBytesWithLength(_message.Data, 0, _message.Length);
            DeliveryMethod deliveryMethod = sendMode == ESendMode.Reliable ? DeliveryMethod.ReliableSequenced : DeliveryMethod.Unreliable;
            foreach (KeyValuePair<ulong, NetPeer> item in _peers)
                if (filter(item.Key))
                    item.Value.Send(_dataWriter, deliveryMethod);
        }

        public override void Send(ulong client, string messageName, WriteToMessageDelegate writeToMessage, ESendMode sendMode) {
            if (!IsRunning || !_peers.TryGetValue(client, out NetPeer peer)) return;
            _message.Reset();
            _message.AsWriter.PutUInt(Hash32.Generate(messageName));
            writeToMessage(_message.AsWriter);
            _dataWriter.Reset();
            _dataWriter.PutBytesWithLength(_message.Data, 0, _message.Length);
            peer.Send(_dataWriter, sendMode == ESendMode.Reliable ? DeliveryMethod.ReliableSequenced : DeliveryMethod.Unreliable);
        }

        public override void PopulateClientIds(List<ulong> clientIds) {
            clientIds.Clear();
            for (int i = 0; i < _clientIds.Count; i++) clientIds.Add(_clientIds[i]);
        }

        void INetEventListener.OnPeerConnected(NetPeer peer) {
            _peers[(ulong) peer.Id] = peer;
            _clientIds.Add((ulong) peer.Id);
            OnConnect((ulong) peer.Id);
        }

        void INetEventListener.OnPeerDisconnected(NetPeer peer, DisconnectInfo disconnectInfo) {
            _peers.Remove((ulong) peer.Id);
            _clientIds.Remove((ulong) peer.Id);
            OnDisconnect((ulong) peer.Id);
        }

        void INetEventListener.OnNetworkError(IPEndPoint endPoint, SocketError socketError) => NetworkErrorEvent?.Invoke(endPoint, socketError);

        void INetEventListener.OnNetworkReceive(NetPeer peer, NetPacketReader reader, DeliveryMethod deliveryMethod) {
            _message.Reset();
            _message.Resize(reader.UserDataSize);
            int count = reader.GetInt();
            reader.GetBytes(_message.Data, 0, count);
            OnReceiveMessage((ulong) peer.Id, _message.AsReader);
        }

        void INetEventListener.OnNetworkReceiveUnconnected(IPEndPoint remoteEndPoint, NetPacketReader reader, UnconnectedMessageType messageType) => NetworkReceiveUnconnectedEvent?.Invoke(remoteEndPoint, reader, messageType);

        void INetEventListener.OnNetworkLatencyUpdate(NetPeer peer, int latency) => NetworkLatencyUpdateEvent?.Invoke(peer, latency);


        void INetEventListener.OnConnectionRequest(ConnectionRequest request) => _handleConnectionAccept?.Invoke(request);
    }
}