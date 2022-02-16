using System;
using System.Collections.Generic;
using System.Net;
using System.Net.Sockets;
using System.Threading;
using System.Threading.Tasks;

namespace NetworkLayer.Transports.Default {
    public class DefaultServerTransport : NetworkServer.BaseTransport {
        private class ClientData {
            public EndPoint endpoint;
            public int rtt;
        }
        
        private readonly struct BufferData {
            public readonly int index;
            public readonly int length;

            public BufferData(int index, int length) {
                this.index = index;
                this.length = length;
            }
        }

        private Socket _socket;
        private EndPoint _endpoint;
        private Dictionary<ulong, ClientData> _clientData;
        private ulong _nextClientId;
        private Queue<ulong> _freeClientIds;
        private readonly MessagePool _messagePool;
        private Task _receiveTask;
        private int _receiveIndex;
        private CancellationTokenSource _receiveTokenSource;
        private byte[] _receiveStream;
        private readonly object _receiveLock;
        private readonly Queue<BufferData> _receiveQueue;
        private readonly byte[] _receiveBuffer;
        private readonly ArraySegment<byte> _receiveSegment;
        private readonly Message _receiveMessage;

        public DefaultServerTransport() {
            
        }
        
        public override bool IsRunning => _socket is {IsBound: true};

        public override int ClientCount => _clientData.Count;
        
        public override int GetRTT(ulong clientId) => -1;

        public override void Host(ushort port) {
            if (IsRunning) return;
            if (_socket != null) {
                _socket.Shutdown(SocketShutdown.Both);
                _socket.Close();
            }
            _socket = new Socket(AddressFamily.InterNetwork, SocketType.Dgram, ProtocolType.Udp);
            _endpoint = new IPEndPoint(IPAddress.Any, port);
            _socket.Bind(_endpoint);
            _socket.Listen(2147483647);
            OnHost();
        }

        public override void Close() {
            if (!IsRunning) return;
            _socket.Shutdown(SocketShutdown.Both);
            _socket.Close();
            _clientData.Clear();
            _freeClientIds.Clear();
            _nextClientId = 1;
            _receiveIndex = 0;
            OnClose();
        }

        public override void Disconnect(ulong clientId) {
            
        }

        public override void Send(string messageName, WriteToMessageDelegate writeToMessage, ESendMode sendMode) {
            
        }

        public override void Send(NetworkServer.SendFilterDelegate filter, string messageName, WriteToMessageDelegate writeToMessage, ESendMode sendMode) {
            
        }

        public override void Send(ulong clientId, string messageName, WriteToMessageDelegate writeToMessage, ESendMode sendMode) {
            
        }

        public override void Dispose() {
            
        }

        public override void PopulateClientIds(List<ulong> clientIds) {
            
        }

        protected override void Update() {
            
        }
    }
}