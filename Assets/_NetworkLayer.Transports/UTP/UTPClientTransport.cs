using System;
using System.Collections.Generic;
using NetworkLayer.Utils;
using Unity.Burst;
using Unity.Collections;
using Unity.Jobs;
using Unity.Networking.Transport;

namespace NetworkLayer.Transports.UTP {
    public class UTPClientTransport : ClientTransport {
        private readonly struct PendingSend {
            public readonly int Index;
            public readonly int Length;
            public readonly ESendMode SendMode;

            public PendingSend(int index, int length, ESendMode sendMode) {
                Index = index;
                Length = length;
                SendMode = sendMode;
            }
        }

        private readonly struct ProcessedEvent {
            public readonly NetworkEvent.Type Type;
            public readonly int Index;
            public readonly int Length;

            public ProcessedEvent(NetworkEvent.Type type, int index, int length) {
                Type = type;
                Index = index;
                Length = length;
            }
        }
        
        [BurstCompile]
        private struct ProcessJob : IJob {
            public NetworkDriver Driver;
            public NativeList<byte> ReceiveBuffer;
            [WriteOnly] public NativeArray<NetworkConnection> Connection;
            [WriteOnly] public NativeQueue<ProcessedEvent> EventQueue;

            public void Execute() {
                NetworkEvent.Type type;
                while ((type = Driver.PopEvent(out NetworkConnection _, out DataStreamReader reader)) != NetworkEvent.Type.Empty) {
                    if (type == NetworkEvent.Type.Data) {
                        int index = ReceiveBuffer.Length;
                        ReceiveBuffer.ResizeUninitialized(index + reader.Length);
                        reader.ReadBytes(ReceiveBuffer.AsArray().GetSubArray(index, reader.Length));
                        EventQueue.Enqueue(new ProcessedEvent(type, index, reader.Length));
                    } else {
                        if (type == NetworkEvent.Type.Disconnect) Connection[0] = default;
                        EventQueue.Enqueue(new ProcessedEvent(type, 0, 0));
                    }
                }
            }
        }

        [BurstCompile]
        private struct SendDataJob : IJobFor {
            public NetworkDriver.Concurrent Driver;
            public NetworkPipeline ReliablePipeline;
            [ReadOnly] public NativeArray<NetworkConnection> Connection;
            [ReadOnly] public NativeList<PendingSend> PendingSends;
            [ReadOnly] public NativeArray<byte> SendBuffer;

            public void Execute(int index) {
                PendingSend pendingSend = PendingSends[index];
                if (0 != Driver.BeginSend(pendingSend.SendMode == ESendMode.Reliable ? ReliablePipeline : NetworkPipeline.Null, Connection[0], out DataStreamWriter writer)) return;
                writer.WriteBytes(SendBuffer.GetSubArray(pendingSend.Index, pendingSend.Length));
                Driver.EndSend(writer);
            }
        }

        private delegate void SendDelegate();

        private NativeArray<NetworkConnection> _connection;
        private NativeList<byte> _sendBuffer;
        private NativeList<byte> _receiveBuffer;
        private NativeList<PendingSend> _pendingSends;
        private NativeQueue<ProcessedEvent> _eventQueue;
        private NetworkDriver _driver;
        private NetworkPipeline _reliablePipeline;
        private JobHandle _job;

        private EClientState _state;
        private readonly MessageWriter _writer;
        private readonly MessageReader _reader;
        private readonly Queue<SendDelegate> _sendQueue;

        public UTPClientTransport(uint messageGroupId) : base(messageGroupId) {
            _connection = new NativeArray<NetworkConnection>(1, Allocator.Persistent);
            _driver = NetworkDriver.Create();
            _reliablePipeline = _driver.CreatePipeline(typeof(ReliableSequencedPipelineStage));
            _sendBuffer = new NativeList<byte>(1024, Allocator.Persistent);
            _receiveBuffer = new NativeList<byte>(1024, Allocator.Persistent);
            _pendingSends = new NativeList<PendingSend>(1, Allocator.Persistent);
            _eventQueue = new NativeQueue<ProcessedEvent>(Allocator.Persistent);
            _writer = new MessageWriter();
            _reader = new MessageReader();
            _sendQueue = new Queue<SendDelegate>();
        }

        public UTPClientTransport(string messageGroupName) : this(Hash32.Generate(messageGroupName)) { }

        public override EClientState State => _connection.IsCreated ? _state : EClientState.Disconnected;

        public override void Connect(string address, ushort port) {
            if (State != EClientState.Disconnected) {
                OnLog("Client - Cannot attempt connection. Reason: already connecting/connected!");
                return;
            }
            NetworkEndPoint endpoint;
            if (address == "localhost" || address == "127.0.0.1") {
                endpoint = NetworkEndPoint.LoopbackIpv4;
                endpoint.Port = port;
            } else if (!NetworkEndPoint.TryParse(address, port, out endpoint)) {
                OnLog("Client - Cannot attempt connection. Reason: endpoint could not be resolved!");
                return;
            }
            _connection[0] = _driver.Connect(endpoint);
            _state = _driver.GetConnectionState(_connection[0]) switch {
                NetworkConnection.State.Connected => EClientState.Connected,
                NetworkConnection.State.Connecting => EClientState.Connecting,
                _ => EClientState.Disconnected
            };
            OnLog("Client - Attempting to connect to the server...");
            OnAttemptConnection();
        }

        public override void Disconnect() {
            if (State == EClientState.Disconnected) {
                OnLog("Client - Cannot disconnect. Reason: already disconnected!");
                return;
            }
            _job.Complete();
            _driver.Disconnect(_connection[0]);
            _driver.ScheduleFlushSend(default).Complete();
            _connection[0] = default;
            _state = EClientState.Disconnected;
            _pendingSends.Clear();
            _eventQueue.Clear();
            _sendQueue.Clear();
            OnLog("Client - Disconnected from the server!");
            OnDisconnect();
        }

        public override void Update() {
            _job.Complete();
            if (!_connection[0].IsCreated) return;
            _state = _driver.GetConnectionState(_connection[0]) switch {
                NetworkConnection.State.Connected => EClientState.Connected,
                NetworkConnection.State.Connecting => EClientState.Connecting,
                _ => EClientState.Disconnected
            };
            while (_eventQueue.TryDequeue(out ProcessedEvent networkEvent)) {
                switch (networkEvent.Type) {
                    case NetworkEvent.Type.Connect: {
                        OnLog("Client - Connected to the server!");
                        OnConnect();
                        break;
                    }
                    case NetworkEvent.Type.Data: {
                        _reader.Reset();
                        _reader.Resize(networkEvent.Length);
                        NativeArray<byte>.Copy(_receiveBuffer, networkEvent.Index, _reader.Data, 0, networkEvent.Length);
                        try {
                            OnReceiveMessage(_reader);
                        } catch (Exception exception) {
                            OnLog($"Client - Error trying to receive data! Message: {exception}");
                        }
                        break;
                    }
                    case NetworkEvent.Type.Disconnect: {
                        _state = EClientState.Disconnected;
                        OnLog("Client - Disconnected from the server!");
                        OnDisconnect();
                        break;
                    }
                }
            }
            _pendingSends.Clear();
            _sendBuffer.Clear();
            _receiveBuffer.Clear();
            while (_sendQueue.Count > 0) _sendQueue.Dequeue()();
            SendDataJob sendDataJob = new SendDataJob {
                Driver = _driver.ToConcurrent(),
                Connection = _connection,
                ReliablePipeline = _reliablePipeline,
                PendingSends = _pendingSends,
                SendBuffer = _sendBuffer
            };
            ProcessJob processJob = new ProcessJob {
                Driver = _driver,
                Connection = _connection,
                EventQueue = _eventQueue,
                ReceiveBuffer = _receiveBuffer
            };
            _job = _pendingSends.Length > 0 ? sendDataJob.ScheduleParallel(_pendingSends.Length, 1, default) : default;
            _job = _driver.ScheduleUpdate(_job);
            _job = processJob.Schedule(_job);
        }

        public override void Dispose() {
            Disconnect();
            _connection.Dispose();
            _driver.Dispose();
            _sendBuffer.Dispose();
            _receiveBuffer.Dispose();
            _pendingSends.Dispose();
            _eventQueue.Dispose();
            _reliablePipeline = default;
        }

        public override void SendMessage(uint messageId, WriteMessageDelegate writeMessage, ESendMode sendMode) {
            if (State != EClientState.Connected) {
                OnLog($"Client - Cannot send message {messageId} to the server. Reason: not connected!");
                return;
            }
            _sendQueue.Enqueue(() => {
                if (State != EClientState.Connected) {
                    OnLog($"Client - Cannot send message {messageId} to the server. Reason: not connected!");
                    return;
                }
                _writer.Reset();
                _writer.PutUInt(messageId);
                writeMessage(_writer);
                int index = _writer.Length;
                _sendBuffer.ResizeUninitialized(index + _writer.Length);
                NativeArray<byte>.Copy(_writer.Data, 0, _sendBuffer.AsArray(), index, _writer.Length);
                _pendingSends.Add(new PendingSend(index, _writer.Length, sendMode));
            });
        }
    }
}
