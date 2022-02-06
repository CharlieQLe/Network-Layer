using System;
using System.Collections.Generic;
using NetworkLayer.Utils;
using Unity.Burst;
using Unity.Collections;
using Unity.Jobs;
using Unity.Networking.Transport;

namespace NetworkLayer.Transports.UTP {
    public class UTPClientTransport : ClientTransport {
        private readonly struct SendData {
            public readonly int Index;
            public readonly int Length;
            public readonly ESendMode SendMode;

            public SendData(int index, int length, ESendMode sendMode) {
                Index = index;
                Length = length;
                SendMode = sendMode;
            }
        }

        private readonly struct EventData {
            public readonly NetworkEvent.Type Type;
            public readonly int Index;
            public readonly int Length;

            public EventData(NetworkEvent.Type type, int index, int length) {
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
            [WriteOnly] public NativeQueue<EventData> EventQueue;

            public void Execute() {
                NetworkEvent.Type type;
                while ((type = Driver.PopEvent(out NetworkConnection _, out DataStreamReader reader)) != NetworkEvent.Type.Empty) {
                    if (type == NetworkEvent.Type.Data) {
                        int index = ReceiveBuffer.Length;
                        ReceiveBuffer.ResizeUninitialized(index + reader.Length);
                        reader.ReadBytes(ReceiveBuffer.AsArray().GetSubArray(index, reader.Length));
                        EventQueue.Enqueue(new EventData(type, index, reader.Length));
                    } else {
                        if (type == NetworkEvent.Type.Disconnect) Connection[0] = default;
                        EventQueue.Enqueue(new EventData(type, 0, 0));
                    }
                }
            }
        }

        [BurstCompile]
        private struct SendDataJob : IJobFor {
            public NetworkDriver.Concurrent Driver;
            public NetworkPipeline ReliablePipeline;
            [ReadOnly] public NativeArray<NetworkConnection> Connection;
            [ReadOnly] public NativeList<SendData> SendData;
            [ReadOnly] public NativeArray<byte> SendBuffer;

            public void Execute(int index) {
                SendData sendData = SendData[index];
                if (0 != Driver.BeginSend(sendData.SendMode == ESendMode.Reliable ? ReliablePipeline : NetworkPipeline.Null, Connection[0], out DataStreamWriter writer)) return;
                writer.WriteBytes(SendBuffer.GetSubArray(sendData.Index, sendData.Length));
                Driver.EndSend(writer);
            }
        }

        private delegate void SendDelegate();

        private NativeArray<NetworkConnection> _connection;
        private NativeList<byte> _sendBuffer;
        private NativeList<byte> _receiveBuffer;
        private NativeList<SendData> _sendData;
        private NativeQueue<EventData> _eventQueue;
        private NetworkDriver _driver;
        private NetworkPipeline _reliablePipeline;
        private JobHandle _job;

        private EClientState _state;
        private readonly Message _message;
        private readonly Queue<SendDelegate> _sendQueue;

        public UTPClientTransport(uint messageGroupId) : base(messageGroupId) {
            _connection = new NativeArray<NetworkConnection>(1, Allocator.Persistent);
            _driver = NetworkDriver.Create();
            _reliablePipeline = _driver.CreatePipeline(typeof(ReliableSequencedPipelineStage));
            _sendBuffer = new NativeList<byte>(1024, Allocator.Persistent);
            _receiveBuffer = new NativeList<byte>(1024, Allocator.Persistent);
            _sendData = new NativeList<SendData>(1, Allocator.Persistent);
            _eventQueue = new NativeQueue<EventData>(Allocator.Persistent);
            _message = new Message();
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
            _sendData.Clear();
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
            while (_eventQueue.TryDequeue(out EventData networkEvent)) {
                switch (networkEvent.Type) {
                    case NetworkEvent.Type.Connect: {
                        OnLog("Client - Connected to the server!");
                        OnConnect();
                        break;
                    }
                    case NetworkEvent.Type.Data: {
                        _message.Reset();
                        _message.Resize(networkEvent.Length);
                        NativeArray<byte>.Copy(_receiveBuffer, networkEvent.Index, _message.Data, 0, networkEvent.Length);
                        try {
                            OnReceiveMessage(_message.AsReader);
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
            _sendData.Clear();
            _sendBuffer.Clear();
            _receiveBuffer.Clear();
            while (_sendQueue.Count > 0) _sendQueue.Dequeue()();
            SendDataJob sendDataJob = new SendDataJob {
                Driver = _driver.ToConcurrent(),
                Connection = _connection,
                ReliablePipeline = _reliablePipeline,
                SendData = _sendData,
                SendBuffer = _sendBuffer
            };
            ProcessJob processJob = new ProcessJob {
                Driver = _driver,
                Connection = _connection,
                EventQueue = _eventQueue,
                ReceiveBuffer = _receiveBuffer
            };
            _job = _sendData.Length > 0 ? sendDataJob.ScheduleParallel(_sendData.Length, 1, default) : default;
            _job = _driver.ScheduleUpdate(_job);
            _job = processJob.Schedule(_job);
        }

        public override void Dispose() {
            Disconnect();
            _connection.Dispose();
            _driver.Dispose();
            _sendBuffer.Dispose();
            _receiveBuffer.Dispose();
            _sendData.Dispose();
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
                _message.Reset();
                _message.AsWriter.PutUInt(messageId);
                writeMessage(_message.AsWriter);
                int index = _sendBuffer.Length;
                _sendBuffer.ResizeUninitialized(index + _message.Length);
                NativeArray<byte>.Copy(_message.Data, 0, _sendBuffer.AsArray(), index, _message.Length);
                _sendData.Add(new SendData(index, _message.Length, sendMode));
            });
        }
    }
}
