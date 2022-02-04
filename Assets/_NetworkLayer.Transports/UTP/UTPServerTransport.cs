using System;
using System.Collections.Generic;
using NetworkLayer.Utils;
using Unity.Burst;
using Unity.Collections;
using Unity.Jobs;
using Unity.Networking.Transport;

namespace NetworkLayer.Transports.UTP {
    public class UTPServerTransport : ServerTransport {
        private readonly struct PendingSend {
            public readonly NetworkConnection Connection;
            public readonly int Index;
            public readonly int Length;
            public readonly ESendMode SendMode;

            public PendingSend(NetworkConnection connection, int index, int length, ESendMode sendMode) {
                Connection = connection;
                Index = index;
                Length = length;
                SendMode = sendMode;
            }
        }

        private readonly struct ProcessedEvent {
            public readonly ulong Client;
            public readonly NetworkEvent.Type Type;
            public readonly int Index;
            public readonly int Length;

            public ProcessedEvent(ulong client, NetworkEvent.Type type, int index, int length) {
                Client = client;
                Type = type;
                Index = index;
                Length = length;
            }
        }

        [BurstCompile]
        private struct AcceptConnectionsJob : IJob {
            public NetworkDriver Driver;
            public NativeQueue<NetworkConnection> AcceptedConnections;

            public void Execute() {
                NetworkConnection connection;
                while ((connection = Driver.Accept()).IsCreated) AcceptedConnections.Enqueue(connection);
            }
        }
        
        [BurstCompile]
        private struct ProcessJob : IJob {
            public NetworkDriver Driver;
            public NativeQueue<ProcessedEvent> EventQueue;
            public NativeList<byte> ReceiveBuffer;

            public void Execute() {
                int index = 0;
                NetworkEvent.Type type;
                while ((type = Driver.PopEvent(out NetworkConnection connection, out DataStreamReader reader)) != NetworkEvent.Type.Empty) {
                    if (type == NetworkEvent.Type.Data) {
                        NativeArray<byte> stream = new NativeArray<byte>(reader.Length, Allocator.Temp, NativeArrayOptions.UninitializedMemory);
                        reader.ReadBytes(stream);
                        ProcessedEvent processedEvent = new ProcessedEvent((ulong) connection.InternalId, type, index, stream.Length);
                        ReceiveBuffer.Resize(index + reader.Length, NativeArrayOptions.UninitializedMemory);
                        NativeArray<byte>.Copy(stream, 0, ReceiveBuffer.AsArray(), processedEvent.Index, processedEvent.Length);
                        index += processedEvent.Length;
                        EventQueue.Enqueue(processedEvent);
                    } else {
                        EventQueue.Enqueue(new ProcessedEvent((ulong) connection.InternalId, type, 0, 0));
                    }
                }
            }
        }

        [BurstCompile]
        private struct SendDataJob : IJobFor {
            public NetworkDriver.Concurrent Driver;
            public NetworkPipeline UnreliablePipeline;
            public NetworkPipeline ReliablePipeline;
            [ReadOnly] public NativeList<PendingSend> PendingSends;
            [ReadOnly, NativeDisableParallelForRestriction]
            public NativeArray<byte> SendBuffer;

            public void Execute(int index) {
                PendingSend pendingSend = PendingSends[index];
                if (0 != Driver.BeginSend(pendingSend.SendMode == ESendMode.Reliable ? ReliablePipeline : UnreliablePipeline, pendingSend.Connection, out DataStreamWriter writer)) return;
                writer.WriteBytes(SendBuffer.GetSubArray(pendingSend.Index, pendingSend.Length));
                Driver.EndSend(writer);
            }
        }

        private delegate void MainThreadDelegate();

        private NativeList<byte> _sendBuffer;
        private NativeList<byte> _receiveBuffer;
        private NativeList<PendingSend> _pendingSends;
        private NativeQueue<ProcessedEvent> _eventQueue;
        private NativeQueue<NetworkConnection> _acceptedConnections;
        private NetworkDriver _driver;
        private NetworkPipeline _unreliablePipeline;
        private NetworkPipeline _reliablePipeline;
        private JobHandle _job;

        private int _sendBufferIndex;
        private readonly MessageWriter _writer;
        private readonly MessageReader _reader;
        private readonly Queue<MainThreadDelegate> _mainThreadCallbacks;
        private readonly Dictionary<ulong, NetworkConnection> _connections;

        public UTPServerTransport(uint messageGroupId) : base(messageGroupId) {
            _sendBuffer = new NativeList<byte>(1024, Allocator.Persistent);
            _receiveBuffer = new NativeList<byte>(1024, Allocator.Persistent);
            _pendingSends = new NativeList<PendingSend>(1, Allocator.Persistent);
            _eventQueue = new NativeQueue<ProcessedEvent>(Allocator.Persistent);
            _acceptedConnections = new NativeQueue<NetworkConnection>(Allocator.Persistent);
            _writer = new MessageWriter();
            _reader = new MessageReader();
            _mainThreadCallbacks = new Queue<MainThreadDelegate>();
            _connections = new Dictionary<ulong, NetworkConnection>();
        }

        public UTPServerTransport(string messageGroupName) : this(Hash32.Generate(messageGroupName)) { }

        public override bool IsRunning => _driver.IsCreated;

        private void WriteToSendBuffer(uint messageId, WriteMessageDelegate writeMessage) {
            _writer.Reset();
            _writer.PutUInt(messageId);
            writeMessage(_writer);
            _sendBuffer.Resize(_sendBufferIndex + _writer.Length, NativeArrayOptions.UninitializedMemory);
            NativeArray<byte>.Copy(_writer.Data, 0, _sendBuffer.AsArray(), _sendBufferIndex, _writer.Length);
        }

        public override void Host(ushort port) {
            if (IsRunning) return;
            NetworkEndPoint endpoint = NetworkEndPoint.AnyIpv4;
            endpoint.Port = port;
            _driver = NetworkDriver.Create();
            if (0 == _driver.Bind(endpoint) && 0 == _driver.Listen()) {
                _unreliablePipeline = _driver.CreatePipeline(typeof(UnreliableSequencedPipelineStage));
                _reliablePipeline = _driver.CreatePipeline(typeof(ReliableSequencedPipelineStage));
                OnHost();
            } else {
                _driver.Dispose();
                throw new Exception("Server failed to bind or listen!");
            }
        }

        public override void Close() {
            if (!IsRunning) return;
            _job.Complete();
            foreach (NetworkConnection connection in _connections.Values) _driver.Disconnect(connection);
            _driver.ScheduleFlushSend(default).Complete();
            _mainThreadCallbacks.Clear();
            _eventQueue.Clear();
            _acceptedConnections.Clear();
            _connections.Clear();
            _driver.Dispose();
            _unreliablePipeline = default;
            _reliablePipeline = default;
        }

        public override void Disconnect(ulong client) {
            if (!IsRunning) return;
            _mainThreadCallbacks.Enqueue(() => {
                _driver.Disconnect(_connections[client]);
                _connections.Remove(client);
                OnDisconnect(client);
            });
        }

        public override void Update() {
            _job.Complete();
            if (!IsRunning) return;
            while (_acceptedConnections.TryDequeue(out NetworkConnection connection)) {
                _connections[(ulong) connection.InternalId] = connection;
                OnConnect((ulong) connection.InternalId);
            }
            while (_eventQueue.TryDequeue(out ProcessedEvent networkEvent)) {
                switch (networkEvent.Type) {
                    case NetworkEvent.Type.Data: {
                        _reader.Reset();
                        _reader.Resize(networkEvent.Length);
                        NativeArray<byte>.Copy(_receiveBuffer, networkEvent.Index, _reader.Data, 0, networkEvent.Length);
                        OnReceiveMessage(networkEvent.Client, _reader);
                        break;
                    }
                    case NetworkEvent.Type.Disconnect: {
                        _connections.Remove(networkEvent.Client);
                        OnDisconnect(networkEvent.Client);
                        break;
                    }
                }
            }
            _sendBufferIndex = 0;
            _pendingSends.Clear();
            _sendBuffer.Clear();
            while (_mainThreadCallbacks.Count > 0) _mainThreadCallbacks.Dequeue()();
            SendDataJob sendDataJob = new SendDataJob {
                Driver = _driver.ToConcurrent(),
                UnreliablePipeline = _unreliablePipeline,
                ReliablePipeline = _reliablePipeline,
                PendingSends = _pendingSends,
                SendBuffer = _sendBuffer
            };
            AcceptConnectionsJob acceptConnectionsJob = new AcceptConnectionsJob {
                Driver = _driver,
                AcceptedConnections = _acceptedConnections
            };
            ProcessJob processJob = new ProcessJob {
                Driver = _driver,
                EventQueue = _eventQueue,
                ReceiveBuffer = _receiveBuffer
            };
            _job = processJob.Schedule(acceptConnectionsJob.Schedule(_driver.ScheduleUpdate(_pendingSends.Length > 0 ? sendDataJob.ScheduleParallel(_pendingSends.Length, 1, default) : default)));
        }

        public override void Dispose() {
            Close();
            _sendBuffer.Dispose();
            _receiveBuffer.Dispose();
            _pendingSends.Dispose();
            _eventQueue.Dispose();
            _acceptedConnections.Dispose();
        }

        public override void SendMessageToAll(uint messageId, WriteMessageDelegate writeMessage, ESendMode sendMode) {
            if (!IsRunning) return;
            _mainThreadCallbacks.Enqueue(() => {
                WriteToSendBuffer(messageId, writeMessage);
                foreach (NetworkConnection connection in _connections.Values) _pendingSends.Add(new PendingSend(connection, _sendBufferIndex, _writer.Length, sendMode));
                _sendBufferIndex += _writer.Length;
            });
        }

        public override void SendMessageToClients(IEnumerable<ulong> clients, uint messageId, WriteMessageDelegate writeMessage, ESendMode sendMode) {
            if (!IsRunning) return;
            _mainThreadCallbacks.Enqueue(() => {
                WriteToSendBuffer(messageId, writeMessage);
                foreach (ulong client in clients) if (_connections.TryGetValue(client, out NetworkConnection connection)) _pendingSends.Add(new PendingSend(connection, _sendBufferIndex, _writer.Length, sendMode));
                _sendBufferIndex += _writer.Length;
            });
        }

        public override void SendMessageToClient(ulong client, uint messageId, WriteMessageDelegate writeMessage, ESendMode sendMode) {
            if (!IsRunning) return;
            _mainThreadCallbacks.Enqueue(() => {
                if (!_connections.TryGetValue(client, out NetworkConnection connection)) return;
                WriteToSendBuffer(messageId, writeMessage);
                _pendingSends.Add(new PendingSend(connection, _sendBufferIndex, _writer.Length, sendMode));
                _sendBufferIndex += _writer.Length;
            });
        }
    }
}