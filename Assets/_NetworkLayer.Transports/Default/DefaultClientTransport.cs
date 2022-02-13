using System;
using System.Collections.Generic;
using System.Net;
using System.Net.Sockets;
using System.Threading;
using System.Threading.Tasks;
using NetworkLayer.Utils;

namespace NetworkLayer.Transports.Default {
    public class DefaultClientTransport : NetworkClient.BaseTransport {
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
        private EClientState _state;
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

        public override EClientState State => _state;

        public override int Rtt => -1;

        public DefaultClientTransport() {
            _messagePool = new MessagePool();
            _receiveLock = new object();
            _receiveQueue = new Queue<BufferData>();
            _receiveBuffer = new byte[1024];
            _receiveSegment = new ArraySegment<byte>(_receiveBuffer);
            _receiveStream = new byte[1024];
            _receiveMessage = new Message();
        }

        private void FinishConnect(IAsyncResult result) {
            _receiveTokenSource = new CancellationTokenSource();
            _receiveTask = Task.Run(ReceiveTask, _receiveTokenSource.Token);
            _state = EClientState.Connected;
            OnConnect();
        }

        private void FinishSend(IAsyncResult result) {
            Message message = (Message) result.AsyncState;
            _messagePool.PoolMessage(message);
        }

        private async void ReceiveTask() {
            while (!_receiveTokenSource.IsCancellationRequested) {
                int bytesRead = await _socket.ReceiveAsync(_receiveSegment, SocketFlags.None);
                if (bytesRead == 0) continue;
                    
                // todo: handle header
                
                lock (_receiveLock) {
                    if (_receiveIndex + bytesRead > _receiveStream.Length) {
                        byte[] stream = new byte[_receiveIndex + bytesRead];
                        Buffer.BlockCopy(_receiveStream, 0, stream, 0, _receiveStream.Length);
                        _receiveStream = stream;
                    }
                    Buffer.BlockCopy(_receiveBuffer, 0, _receiveStream, _receiveIndex, bytesRead);
                    _receiveQueue.Enqueue(new BufferData(_receiveIndex, bytesRead));
                    _receiveIndex += bytesRead;
                }
            }
        }
        
        public override void Connect(string address, ushort port) {
            if (State != EClientState.Disconnected || !IPAddress.TryParse("address", out IPAddress ipAddress)) return;
            _endpoint = new IPEndPoint(ipAddress, port);
            if (_socket != null) {
                _socket.Shutdown(SocketShutdown.Both);
                _socket.Close();
            }
            _socket = new Socket(AddressFamily.InterNetwork, SocketType.Dgram, ProtocolType.Udp);
            _state = EClientState.Connecting;
            OnStartConnecting();
            _socket.BeginConnect(_endpoint, FinishConnect, null);
        }

        public override void Disconnect() {
            if (State == EClientState.Disconnected) return;
            _socket.Shutdown(SocketShutdown.Both);
            _socket.Close();
            _receiveTokenSource.Cancel();
            _receiveTask.Wait();
            _receiveQueue.Clear();
            _receiveIndex = 0;
            _state = EClientState.Disconnected;
            OnDisconnect();
        }

        public override void Send(string messageName, WriteToMessageDelegate writeToMessage, ESendMode sendMode) {
            if (State != EClientState.Connected) return;
            Message message = _messagePool.RetrieveMessage();
            
            // todo: handle header
            
            message.AsWriter.PutUInt(Hash32.Generate(messageName));
            writeToMessage(message.AsWriter);
            _socket.BeginSend(message.Data, 0, message.Length, SocketFlags.None, FinishSend, message);
        }

        public override void Dispose() {
            Disconnect();
        }

        protected override void Update() {
            if (State != EClientState.Connected) return;
            lock (_receiveLock) {
                while (_receiveQueue.Count > 0) {
                    BufferData receiveData = _receiveQueue.Dequeue();
                    _receiveMessage.Reset();
                    _receiveMessage.Resize(receiveData.length);
                    Buffer.BlockCopy(_receiveStream, receiveData.index, _receiveMessage.Data, 0, receiveData.length);
                    OnReceiveMessage(_receiveMessage.AsReader);
                }
                _receiveIndex = 0;
            }
            OnUpdate();
        }
    }
}