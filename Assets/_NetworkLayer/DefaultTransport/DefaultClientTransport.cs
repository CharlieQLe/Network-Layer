using System;
using System.Collections.Concurrent;
using System.Net;
using System.Net.Sockets;
using System.Threading;
using System.Threading.Tasks;
using NetworkLayer.Utils;

namespace NetworkLayer.Development {
    public class DefaultClientTransport : ClientTransport {
        private class ReceiveStream {
            public byte[] data;
            public int index;

            public ReceiveStream() {
                data = new byte[1024];
                index = 0;
            }
        }

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
        private ArraySegment<byte> _receiveSegment;
        private ConcurrentQueue<ReceiveData> _receiveQueue;
        private ReceiveStream _receiveStream;
        private Message _message;
        private EndPoint _endpoint;
        private Task _task;
        private CancellationTokenSource _tokenSource;

        public DefaultClientTransport(uint messageGroupId) : base(messageGroupId) {
            _receiveBuffer = new byte[1024];
            _receiveSegment = new ArraySegment<byte>(_receiveBuffer);
            _receiveQueue = new ConcurrentQueue<ReceiveData>();
            _receiveStream = new ReceiveStream();
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

        public override int Rtt { get; }

        public override void Connect(string address, ushort port) {
            if (State != EClientState.Disconnected) return;
            if (!IPAddress.TryParse(address, out IPAddress ipAddress)) return;
            _endpoint = new IPEndPoint(ipAddress, port);

            _task = Task.Run(async () => {
                while (!_tokenSource.IsCancellationRequested) {
                    SocketReceiveMessageFromResult receiveResult = await _socket.ReceiveMessageFromAsync(_receiveSegment, SocketFlags.None, _endpoint);
                    if (receiveResult.ReceivedBytes > 0) {
                        lock (_receiveStream) {
                            int index = _receiveStream.index;
                            if (index + receiveResult.ReceivedBytes > _receiveStream.data.Length) {
                                byte[] newStream = new byte[index + receiveResult.ReceivedBytes];
                                Buffer.BlockCopy(_receiveStream.data, 0, newStream, 0, _receiveStream.data.Length);
                                _receiveStream.data = newStream;
                            }
                            Buffer.BlockCopy(_receiveBuffer, 0, _receiveStream.data, index, receiveResult.ReceivedBytes);
                            _receiveStream.index += receiveResult.ReceivedBytes;
                            _receiveQueue.Enqueue(new ReceiveData(index, receiveResult.ReceivedBytes));
                        }
                    }

                    await Task.Delay(TimeSpan.FromSeconds(1f / 20), _tokenSource.Token);
                }
            }, _tokenSource.Token);
        }

        public override void Disconnect() {
            if (State == EClientState.Disconnected) return;
            _tokenSource.Cancel();
        }

        public override void Update() {
            if (State == EClientState.Disconnected) return;
            lock (_receiveStream) {
                while (_receiveQueue.TryDequeue(out ReceiveData receiveData)) {
                    _message.Reset();
                    _message.Resize(receiveData.Length);
                    Buffer.BlockCopy(_receiveStream.data, receiveData.Index, _message.Data, 0, receiveData.Length);
                    OnReceiveMessage(_message.AsReader);
                }

                _receiveStream.index = 0;
            }

            OnUpdate();
        }

        public override void Dispose() {
            Disconnect();
        }

        public override void SendMessage(uint messageId, WriteMessageDelegate writeMessage, ESendMode sendMode) { }
    }
}