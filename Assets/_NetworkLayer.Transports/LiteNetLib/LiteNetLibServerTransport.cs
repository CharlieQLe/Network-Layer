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

        private readonly Message _message;
        private readonly NetManager _netManager;
        private readonly NetDataWriter _dataWriter;
        private readonly Dictionary<ulong, NetPeer> _peers;
        private HandleConnectionAcceptDelegate _handleConnectionAccept;

        public event EventBasedNetListener.OnNetworkError NetworkErrorEvent;
        public event EventBasedNetListener.OnNetworkReceiveUnconnected NetworkReceiveUnconnectedEvent;
        public event EventBasedNetListener.OnNetworkLatencyUpdate NetworkLatencyUpdateEvent;

        public LiteNetLibServerTransport() {
            _message = new Message();
            _netManager = new NetManager(this);
            _dataWriter = new NetDataWriter();
            _peers = new Dictionary<ulong, NetPeer>();
            _handleConnectionAccept = request => request.Accept();
        }

        /// <summary>
        /// Set the delegate to handle accepting connections.
        /// </summary>
        /// <param name="handleConnectionAcceptDelegate"></param>
        public void SetConnectionAcceptHandle(HandleConnectionAcceptDelegate handleConnectionAcceptDelegate) => _handleConnectionAccept = handleConnectionAcceptDelegate;

        /// <summary>
        /// Close the server with disconnect data.
        /// </summary>
        /// <param name="writeToDisconnectData"></param>
        public void Close(WriteDisconnectDataDelegate writeToDisconnectData) {
            // Do nothing if already closed
            if (!IsRunning) return;
            
            // Write to the disconnect data
            _dataWriter.Reset();
            writeToDisconnectData?.Invoke(_dataWriter);
            
            // Disconnect every client
            _netManager.DisconnectAll(_dataWriter.Data, 0, _dataWriter.Length);
            
            // Stop the net manager
            _netManager.Stop();
            
            // Clear all data
            _peers.Clear();
            
            // Raise the close event
            OnClose();
        }

        private void SharedWrite(string messageName, WriteToMessageDelegate writeToMessage) {
            // Write to the message
            _message.Reset();
            _message.AsWriter.PutUInt(Hash32.Generate(messageName));
            writeToMessage(_message.AsWriter);
            
            // Write to the data writer
            _dataWriter.Reset();
            _dataWriter.PutBytesWithLength(_message.Data, 0, _message.Length);
        }
        
        public override bool IsRunning => _netManager.IsRunning;
        
        public override int ClientCount => _peers.Count;

        public override int GetRTT(ulong clientId) {
            if (_peers.TryGetValue(clientId, out NetPeer peer)) return peer.Ping * 2;
            return -1;
        }

        public override void Host(ushort port) {
            if (IsRunning) return;
            _netManager.Start(port);
            OnHost();
        }

        public override void Close() => Close(null); // Close with no data

        public override void Disconnect(ulong clientId) {
            // Do nothing if not running or if the peer was not found
            if (!IsRunning || !_peers.TryGetValue(clientId, out NetPeer peer)) return;
            
            // Disconnect the peer
            _netManager.DisconnectPeer(peer);
        }

        protected override void Update() {
            // Do nothing if not running
            if (!IsRunning) return;
            
            // Poll for all events
            _netManager.PollEvents();
            
            // Raise the update event
            OnUpdate();
        }

        public override void Dispose() => Close(); // Close the server

        public override void Send(string messageName, WriteToMessageDelegate writeToMessage, ESendMode sendMode) {
            // Do nothing if not running
            if (!IsRunning) return;
            
            // Write the data
            SharedWrite(messageName, writeToMessage);

            // Send the data to all peers
            _netManager.SendToAll(_dataWriter, LiteNetLibUtility.ConvertSendMode(sendMode));
        }

        public override void Send(NetworkServer.SendFilterDelegate filter, string messageName, WriteToMessageDelegate writeToMessage, ESendMode sendMode) {
            // Do nothing if not running
            if (!IsRunning) return;
            
            // Write the data
            SharedWrite(messageName, writeToMessage);

            // Get the delivery method
            DeliveryMethod deliveryMethod = LiteNetLibUtility.ConvertSendMode(sendMode);
            
            // Send the data to every peer that satisfies the filter
            foreach (KeyValuePair<ulong, NetPeer> item in _peers)
                if (filter(item.Key))
                    item.Value.Send(_dataWriter, deliveryMethod);
        }

        public override void Send(ulong clientId, string messageName, WriteToMessageDelegate writeToMessage, ESendMode sendMode) {
            // Do nothing if not running or if the peer was not found
            if (!IsRunning || !_peers.TryGetValue(clientId, out NetPeer peer)) return;

            // Write the data
            SharedWrite(messageName, writeToMessage);
            
            // Send the data
            peer.Send(_dataWriter, LiteNetLibUtility.ConvertSendMode(sendMode));
        }

        public override void PopulateClientIds(List<ulong> clientIds) {
            // Clear the list
            clientIds.Clear();
            
            // Add every client id to the list
            foreach (ulong clientId in _peers.Keys) clientIds.Add(clientId);
        }

        void INetEventListener.OnPeerConnected(NetPeer peer) {
            // Get the client id
            ulong clientId = (ulong) peer.Id;
            
            // Add the peer
            _peers[clientId] = peer;
            
            // Raise the connect event
            OnConnect(clientId);
        }

        void INetEventListener.OnPeerDisconnected(NetPeer peer, DisconnectInfo disconnectInfo) {
            // Get the client id
            ulong clientId = (ulong) peer.Id;
            
            // Remove the peer
            _peers.Remove(clientId);
            
            // Raise the disconnect event
            OnDisconnect(clientId);
        }

        void INetEventListener.OnNetworkError(IPEndPoint endPoint, SocketError socketError) => NetworkErrorEvent?.Invoke(endPoint, socketError);

        void INetEventListener.OnNetworkReceive(NetPeer peer, NetPacketReader reader, DeliveryMethod deliveryMethod) {
            // Copy the received data into the message
            _message.Reset();
            _message.Resize(reader.UserDataSize);
            int count = reader.GetInt();
            reader.GetBytes(_message.Data, 0, count);
            
            // Raise the message receive event
            OnReceiveMessage((ulong) peer.Id, _message.AsReader);
        }

        void INetEventListener.OnNetworkReceiveUnconnected(IPEndPoint remoteEndPoint, NetPacketReader reader, UnconnectedMessageType messageType) => NetworkReceiveUnconnectedEvent?.Invoke(remoteEndPoint, reader, messageType);

        void INetEventListener.OnNetworkLatencyUpdate(NetPeer peer, int latency) => NetworkLatencyUpdateEvent?.Invoke(peer, latency);

        void INetEventListener.OnConnectionRequest(ConnectionRequest request) => _handleConnectionAccept?.Invoke(request);
    }
}