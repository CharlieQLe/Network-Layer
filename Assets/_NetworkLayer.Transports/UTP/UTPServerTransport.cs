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
            public readonly ulong ConnectionId;
            public readonly int Index;
            public readonly int Length;
            public readonly ESendMode SendMode;

            public PendingSend(ulong connectionId, int index, int length, ESendMode sendMode) {
                ConnectionId = connectionId;
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
            [WriteOnly] public NativeHashMap<ulong, NetworkConnection> Connections;
            [WriteOnly] public NativeQueue<ulong> AcceptedConnections;

            public void Execute() {
                if (!Driver.IsCreated) return;
                NetworkConnection connection;
                while ((connection = Driver.Accept()).IsCreated) {
                    Connections.Add((ulong) connection.InternalId, connection);
                    AcceptedConnections.Enqueue((ulong) connection.InternalId);
                }
            }
        }

        [BurstCompile]
        private struct ProcessJob : IJob {
            public NetworkDriver Driver;
            public NativeList<byte> ReceiveBuffer;
            public NativeHashMap<ulong, NetworkConnection> Connections;
            [WriteOnly] public NativeQueue<ProcessedEvent> EventQueue;

            public void Execute() {
                if (!Driver.IsCreated) return;
                NetworkEvent.Type type;
                while ((type = Driver.PopEvent(out NetworkConnection connection, out DataStreamReader reader)) != NetworkEvent.Type.Empty) {
                    if (type == NetworkEvent.Type.Data) {
                        int index = ReceiveBuffer.Length;
                        ReceiveBuffer.ResizeUninitialized(index + reader.Length);
                        reader.ReadBytes(ReceiveBuffer.AsArray().GetSubArray(index, reader.Length));
                        EventQueue.Enqueue(new ProcessedEvent((ulong) connection.InternalId, type, index, reader.Length));
                    } else {
                        Connections.Remove((ulong) connection.InternalId);
                        EventQueue.Enqueue(new ProcessedEvent((ulong) connection.InternalId, type, 0, 0));
                    }
                }
            }
        }
        
        [BurstCompile]
        private struct SendDataJob : IJobFor {
            public NetworkDriver.Concurrent Driver;
            public NetworkPipeline ReliablePipeline;
            [ReadOnly] public NativeHashMap<ulong, NetworkConnection> Connections;
            [ReadOnly] public NativeList<PendingSend> PendingSends;
            [ReadOnly] public NativeArray<byte> SendBuffer;

            public void Execute(int index) {
                PendingSend pendingSend = PendingSends[index];
                if (!Connections.TryGetValue(pendingSend.ConnectionId, out NetworkConnection connection) || 0 != Driver.BeginSend(pendingSend.SendMode == ESendMode.Reliable ? ReliablePipeline : NetworkPipeline.Null, connection, out DataStreamWriter writer)) return;
                writer.WriteBytes(SendBuffer.GetSubArray(pendingSend.Index, pendingSend.Length));
                Driver.EndSend(writer);
            }
        }

        private delegate void PendingCommandDelegate();
        private delegate void SendDelegate();

        private NativeHashMap<ulong, NetworkConnection> _connections;
        private NativeList<byte> _sendBuffer;
        private NativeList<byte> _receiveBuffer;
        private NativeList<PendingSend> _pendingSends;
        private NativeQueue<ProcessedEvent> _eventQueue;
        private NativeQueue<ulong> _acceptedConnections;
        private NetworkDriver _driver;
        private NetworkPipeline _reliablePipeline;
        private JobHandle _job;

        private bool _isRunning;
        private readonly MessageWriter _writer;
        private readonly MessageReader _reader;
        private readonly Queue<PendingCommandDelegate> _pendingCommandQueue;
        private readonly Queue<SendDelegate> _sendQueue;

        public UTPServerTransport(uint messageGroupId) : base(messageGroupId) {
            _connections = new NativeHashMap<ulong, NetworkConnection>(1, Allocator.Persistent);
            _sendBuffer = new NativeList<byte>(1024, Allocator.Persistent);
            _receiveBuffer = new NativeList<byte>(1024, Allocator.Persistent);
            _pendingSends = new NativeList<PendingSend>(1, Allocator.Persistent);
            _eventQueue = new NativeQueue<ProcessedEvent>(Allocator.Persistent);
            _acceptedConnections = new NativeQueue<ulong>(Allocator.Persistent);
            _writer = new MessageWriter();
            _reader = new MessageReader();
            _pendingCommandQueue = new Queue<PendingCommandDelegate>();
            _sendQueue = new Queue<SendDelegate>();
        }

        public UTPServerTransport(string messageGroupName) : this(Hash32.Generate(messageGroupName)) { }

        public override bool IsRunning => _isRunning;

        private void WriteToSendBuffer(uint messageId, WriteMessageDelegate writeMessage) {
            _writer.Reset();
            _writer.PutUInt(messageId);
            writeMessage(_writer);
            int index = _sendBuffer.Length;
            _sendBuffer.Resize(index + _writer.Length, NativeArrayOptions.UninitializedMemory);
            NativeArray<byte>.Copy(_writer.Data, 0, _sendBuffer.AsArray(), index, _writer.Length);
        }

        public override void Host(ushort port) {
            if (IsRunning) {
                OnLog("Server - Cannot host. Reason: already hosting!");
                return;
            }
            NetworkEndPoint endpoint = NetworkEndPoint.AnyIpv4;
            endpoint.Port = port;
            _driver = NetworkDriver.Create();
            if (0 == _driver.Bind(endpoint) && 0 == _driver.Listen()) {
                _reliablePipeline = _driver.CreatePipeline(typeof(ReliableSequencedPipelineStage));
                _isRunning = true;
                OnLog("Server - Hosting server!");
                OnHost();
            } else {
                _driver.Dispose();
                OnLog("Server - Cannot host. Reason: server failed to bind or listen!");
            }
        }

        public override void Close() {
            if (!IsRunning) {
                OnLog("Server - Cannot close server. Reason: server already closed!");
                return;
            }
            _job.Complete();
            using NativeArray<NetworkConnection> connections = _connections.GetValueArray(Allocator.Temp);
            for (int i = 0; i < connections.Length; i++) _driver.Disconnect(connections[i]);
            _driver.ScheduleFlushSend(default).Complete();
            _connections.Clear();
            _pendingCommandQueue.Clear();
            _sendQueue.Clear();
            _eventQueue.Clear();
            _acceptedConnections.Clear();
            _driver.Dispose();
            _reliablePipeline = default;
            _isRunning = false;
            OnLog("Server - Closed server!");
            OnClose();
        }

        public override void Disconnect(ulong client) {
            if (!IsRunning) {
                OnLog($"Server - Cannot disconnect client {client}. Reason: server is closed!");
                return;
            }
            _pendingCommandQueue.Enqueue(() => {
                if (!_connections.TryGetValue(client, out NetworkConnection connection)) {
                    OnLog($"Server - Cannot disconnect client {client}. Reason: client not found!");
                    return;
                }
                _driver.Disconnect(connection);
                _connections.Remove(client);
                OnLog($"Server - Disconnected client {client}!");
                OnDisconnect(client);
            });
        }

        public override void Update() {
            _job.Complete();
            _isRunning = _driver.IsCreated;
            if (!IsRunning) return;
            while (_acceptedConnections.TryDequeue(out ulong client)) {
                OnLog($"Server - Client {client} connected!");
                OnConnect(client);
            }
            while (_eventQueue.TryDequeue(out ProcessedEvent networkEvent)) {
                if (networkEvent.Type == NetworkEvent.Type.Data) {
                    _reader.Reset();
                    _reader.Resize(networkEvent.Length);
                    NativeArray<byte>.Copy(_receiveBuffer, networkEvent.Index, _reader.Data, 0, networkEvent.Length);
                    try {
                        OnReceiveMessage(networkEvent.Client, _reader);
                    } catch (Exception exception) {
                        OnLog($"Server - Error trying to receive data from client { networkEvent.Client }! Message: {exception}");
                    }
                }
                else {
                    OnLog($"Server - Client {networkEvent.Client} disconnected!");
                    OnDisconnect(networkEvent.Client);
                }
            }
            while (_pendingCommandQueue.Count > 0) _pendingCommandQueue.Dequeue()();
            _pendingSends.Clear();
            _sendBuffer.Clear();
            _receiveBuffer.Clear();
            while (_sendQueue.Count > 0) _sendQueue.Dequeue()();
            SendDataJob sendDataJob = new SendDataJob {
                Driver = _driver.ToConcurrent(),
                ReliablePipeline = _reliablePipeline,
                Connections = _connections,
                PendingSends = _pendingSends,
                SendBuffer = _sendBuffer
            };
            AcceptConnectionsJob acceptConnectionsJob = new AcceptConnectionsJob {
                Driver = _driver,
                Connections = _connections,
                AcceptedConnections = _acceptedConnections
            };
            ProcessJob processJob = new ProcessJob {
                Driver = _driver,
                Connections = _connections,
                EventQueue = _eventQueue,
                ReceiveBuffer = _receiveBuffer
            };
            _job = _pendingSends.Length > 0 ? sendDataJob.ScheduleParallel(_pendingSends.Length, 1, default) : default;
            _job = _driver.ScheduleUpdate(_job);
            _job = acceptConnectionsJob.Schedule(_job);
            _job = processJob.Schedule(_job);
        }

        public override void Dispose() {
            Close();
            _connections.Dispose();
            _sendBuffer.Dispose();
            _receiveBuffer.Dispose();
            _pendingSends.Dispose();
            _eventQueue.Dispose();
            _acceptedConnections.Dispose();
        }

        public override void SendMessageToAll(uint messageId, WriteMessageDelegate writeMessage, ESendMode sendMode) {
            if (!IsRunning) {
                OnLog($"Server - Cannot send message {messageId} to all clients. Reason: server not running!");
                return;
            }
            _sendQueue.Enqueue(() => {
                int index = _sendBuffer.Length;
                WriteToSendBuffer(messageId, writeMessage);
                using NativeArray<ulong> clients = _connections.GetKeyArray(Allocator.Temp);
                for (int i = 0; i < clients.Length; i++) _pendingSends.Add(new PendingSend(clients[i], index, _writer.Length, sendMode));
            });
        }

        public override void SendMessageToClients(IEnumerable<ulong> clients, uint messageId, WriteMessageDelegate writeMessage, ESendMode sendMode) {
            if (!IsRunning) {
                OnLog($"Server - Cannot send message {messageId} to clients. Reason: server not running!");
                return;
            }
            _sendQueue.Enqueue(() => {
                int index = _sendBuffer.Length;
                WriteToSendBuffer(messageId, writeMessage);
                foreach (ulong client in clients) {
                    if (_connections.ContainsKey(client)) _pendingSends.Add(new PendingSend(client, index, _writer.Length, sendMode));
                    else OnLog($"Server - Could not send message {messageId} to client {client}. Reason: client not found!");
                }
            });
        }

        public override void SendMessageToClient(ulong client, uint messageId, WriteMessageDelegate writeMessage, ESendMode sendMode) {
            if (!IsRunning) {
                OnLog($"Server - Cannot send message {messageId} to client {client}. Reason: server not running!");
                return;
            }
            _sendQueue.Enqueue(() => {
                if (!_connections.ContainsKey(client)) {
                    OnLog($"Server - Cannot send message {messageId} to client {client}. Reason: client not found!");
                    return;
                }
                int index = _sendBuffer.Length;
                WriteToSendBuffer(messageId, writeMessage);
                _pendingSends.Add(new PendingSend(client, index, _writer.Length, sendMode));
            });
        }
    }
}