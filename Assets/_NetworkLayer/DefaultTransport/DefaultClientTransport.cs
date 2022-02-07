using System;
using System.Collections.Generic;
using System.Net;
using System.Net.Sockets;
using System.Threading.Tasks;
using NetworkLayer.Utils;

namespace NetworkLayer.Development {
    public class DefaultClientTransport : ClientTransport {
        private readonly struct ReceiveData {
            public readonly int Index;
            public readonly int Length;

            public ReceiveData(int index, int length) {
                Index = index;
                Length = length;
            }
        }
        
        private Socket _socket;
        private byte[] _receiveBuffer;
        private ArraySegment<byte> _receiveBufferSegment;
        private Queue<ReceiveData> _receiveQueue;

        private byte[] _receiveStream;
        private Message _message;
        private EndPoint _endpoint;
        private Task _receiveTask;

        public DefaultClientTransport(uint messageGroupId) : base(messageGroupId) {
            _receiveBuffer = new byte[4096];
            _receiveStream = new byte[4096];
            _receiveBufferSegment = new ArraySegment<byte>(_receiveBuffer);
            _receiveQueue = new Queue<ReceiveData>();
            _socket = new Socket(AddressFamily.InterNetwork, SocketType.Dgram, ProtocolType.Udp);
            _socket.SetSocketOption(SocketOptionLevel.IP, SocketOptionName.PacketInformation, true);
            _message = new Message();
        }
        
        public DefaultClientTransport(string messageGroupName) : this(Hash32.Generate(messageGroupName)) { }

        public override EClientState State {
            get {
                if (_socket.Connected) return EClientState.Connected;
                return _endpoint == null ? EClientState.Disconnected : EClientState.Connecting;
            }
        }

        public override void Connect(string address, ushort port) {
            if (State != EClientState.Disconnected) return;
            if (!IPAddress.TryParse(address, out IPAddress ipAddress)) return;
            
            _endpoint = new IPEndPoint(ipAddress, port);
            
            SocketAsyncEventArgs args = new SocketAsyncEventArgs();
            args.RemoteEndPoint = _endpoint;
            args.Completed += (sender, eventArgs) => {
                OnConnect();
            };
            if (!_socket.ConnectAsync(args)) {
                OnConnect();
            }
        }

        public override void Disconnect() {
            if (State == EClientState.Disconnected) return;
            
            _receiveTask.Wait();
            
            SocketAsyncEventArgs args = new SocketAsyncEventArgs();
            args.RemoteEndPoint = _endpoint;
            args.Completed += (sender, eventArgs) => {
                OnDisconnect();
            };
            if (!_socket.DisconnectAsync(args)) {
                OnDisconnect();
            }
        }

        public override void Update() {
            _receiveTask.Wait();
            
            while (_receiveQueue.Count > 0) {
                ReceiveData receiveData = _receiveQueue.Dequeue();
                _message.Reset();
                _message.Resize(receiveData.Length);
                Buffer.BlockCopy(_receiveStream, receiveData.Index, _message.Data, 0, receiveData.Length);
                OnReceiveMessage(_message.AsReader);
            }
            
            _receiveTask = Task.Run(async () => {
                while (true) {
                    SocketReceiveMessageFromResult result = await _socket.ReceiveMessageFromAsync(_receiveBufferSegment, SocketFlags.None, _endpoint);
                    if (result.ReceivedBytes == 0) break;
                    int index = _receiveStream.Length;
                    if (index + result.ReceivedBytes > _receiveStream.Length) {
                        byte[] newStream = new byte[index + result.ReceivedBytes];
                        Buffer.BlockCopy(_receiveStream, 0, newStream, 0, _receiveStream.Length);
                        _receiveStream = newStream;
                    }
                    Buffer.BlockCopy(_receiveBuffer, 0, _receiveStream, index, result.ReceivedBytes);
                    _receiveQueue.Enqueue(new ReceiveData(index, result.ReceivedBytes));
                }
            });
        }

        public override void Dispose() {
            throw new System.NotImplementedException();
        }

        public override void SendMessage(uint messageId, WriteMessageDelegate writeMessage, ESendMode sendMode) {
            throw new System.NotImplementedException();
        }
    }
}