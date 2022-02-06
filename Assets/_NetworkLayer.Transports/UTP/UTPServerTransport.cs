using System;
using System.Collections.Generic;
using NetworkLayer.Utils;
using Unity.Burst;
using Unity.Collections;
using Unity.Jobs;
using Unity.Networking.Transport;

namespace NetworkLayer.Transports.UTP {
    public class UTPServerTransport : ServerTransport {
        private readonly struct SendData {
            public readonly ulong ConnectionId;
            public readonly int Index;
            public readonly int Length;
            public readonly ESendMode SendMode;

            public SendData(ulong connectionId, int index, int length, ESendMode sendMode) {
                ConnectionId = connectionId;
                Index = index;
                Length = length;
                SendMode = sendMode;
            }
        }

        private readonly struct EventData {
            public readonly ulong Client;
            public readonly NetworkEvent.Type Type;
            public readonly int Index;
            public readonly int Length;

            public EventData(ulong client, NetworkEvent.Type type, int index, int length) {
                Client = client;
                Type = type;
                Index = index;
                Length = length;
            }
        }

        [BurstCompile]
        private struct SendDataJob : IJobFor {
            public NetworkDriver.Concurrent Driver;
            public NetworkPipeline ReliablePipeline;
            [ReadOnly] public NativeHashMap<ulong, NetworkConnection> Connections;
            [ReadOnly] public NativeList<SendData> SendData;
            [ReadOnly] public NativeArray<byte> SendBuffer;

            public void Execute(int index) {
                SendData sendData = SendData[index];
                if (!Connections.TryGetValue(sendData.ConnectionId, out NetworkConnection connection) || 0 != Driver.BeginSend(sendData.SendMode == ESendMode.Reliable ? ReliablePipeline : NetworkPipeline.Null, connection, out DataStreamWriter writer)) return;
                writer.WriteBytes(SendBuffer.GetSubArray(sendData.Index, sendData.Length));
                Driver.EndSend(writer);
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
            [WriteOnly] public NativeQueue<EventData> EventQueue;

            public void Execute() {
                if (!Driver.IsCreated) return;
                NetworkEvent.Type type;
                while ((type = Driver.PopEvent(out NetworkConnection connection, out DataStreamReader reader)) != NetworkEvent.Type.Empty) {
                    if (type == NetworkEvent.Type.Data) {
                        int index = ReceiveBuffer.Length;
                        ReceiveBuffer.ResizeUninitialized(index + reader.Length);
                        reader.ReadBytes(ReceiveBuffer.AsArray().GetSubArray(index, reader.Length));
                        EventQueue.Enqueue(new EventData((ulong) connection.InternalId, type, index, reader.Length));
                    } else {
                        Connections.Remove((ulong) connection.InternalId);
                        EventQueue.Enqueue(new EventData((ulong) connection.InternalId, type, 0, 0));
                    }
                }
            }
        }
        
        private delegate void PendingDisconnectDelegate();
        private delegate void SendDelegate();

        private NativeHashMap<ulong, NetworkConnection> _connections;
        private NativeList<byte> _sendBuffer;
        private NativeList<byte> _receiveBuffer;
        private NativeList<SendData> _sendData;
        private NativeQueue<EventData> _eventQueue;
        private NativeQueue<ulong> _acceptedConnections;
        private NetworkDriver _driver;
        private NetworkPipeline _reliablePipeline;
        private JobHandle _job;

        private bool _isRunning;
        private readonly Message _message;
        private readonly Queue<PendingDisconnectDelegate> _pendingDisconnectQueue;
        private readonly Queue<SendDelegate> _sendQueue;

        public UTPServerTransport(uint messageGroupId) : base(messageGroupId) {
            _connections = new NativeHashMap<ulong, NetworkConnection>(1, Allocator.Persistent);
            _sendBuffer = new NativeList<byte>(1024, Allocator.Persistent);
            _receiveBuffer = new NativeList<byte>(1024, Allocator.Persistent);
            _sendData = new NativeList<SendData>(1, Allocator.Persistent);
            _eventQueue = new NativeQueue<EventData>(Allocator.Persistent);
            _acceptedConnections = new NativeQueue<ulong>(Allocator.Persistent);
            _message = new Message();
            _pendingDisconnectQueue = new Queue<PendingDisconnectDelegate>();
            _sendQueue = new Queue<SendDelegate>();
        }

        public UTPServerTransport(string messageGroupName) : this(Hash32.Generate(messageGroupName)) { }

        public override bool IsRunning => _isRunning;

        private void WriteToSendBuffer(uint messageId, WriteMessageDelegate writeMessage) {
            _message.Reset();
            _message.AsWriter.PutUInt(messageId);
            writeMessage(_message.AsWriter);
            int index = _sendBuffer.Length;
            _sendBuffer.Resize(index + _message.Length, NativeArrayOptions.UninitializedMemory);
            NativeArray<byte>.Copy(_message.Data, 0, _sendBuffer.AsArray(), index, _message.Length);
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
            _pendingDisconnectQueue.Clear();
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
            _pendingDisconnectQueue.Enqueue(() => {
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
            while (_eventQueue.TryDequeue(out EventData networkEvent)) {
                if (networkEvent.Type == NetworkEvent.Type.Data) {
                    _message.Reset();
                    _message.Resize(networkEvent.Length);
                    NativeArray<byte>.Copy(_receiveBuffer, networkEvent.Index, _message.Data, 0, networkEvent.Length);
                    try {
                        OnReceiveMessage(networkEvent.Client, _message.AsReader);
                    } catch (Exception exception) {
                        OnLog($"Server - Error trying to receive data from client { networkEvent.Client }! Message: {exception}");
                    }
                }
                else {
                    OnLog($"Server - Client {networkEvent.Client} disconnected!");
                    OnDisconnect(networkEvent.Client);
                }
            }
            while (_pendingDisconnectQueue.Count > 0) _pendingDisconnectQueue.Dequeue()();
            _sendData.Clear();
            _sendBuffer.Clear();
            _receiveBuffer.Clear();
            while (_sendQueue.Count > 0) _sendQueue.Dequeue()();
            SendDataJob sendDataJob = new SendDataJob {
                Driver = _driver.ToConcurrent(),
                ReliablePipeline = _reliablePipeline,
                Connections = _connections,
                SendData = _sendData,
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
            _job = _sendData.Length > 0 ? sendDataJob.ScheduleParallel(_sendData.Length, 1, default) : default;
            _job = _driver.ScheduleUpdate(_job);
            _job = acceptConnectionsJob.Schedule(_job);
            _job = processJob.Schedule(_job);
        }

        public override void Dispose() {
            Close();
            _connections.Dispose();
            _sendBuffer.Dispose();
            _receiveBuffer.Dispose();
            _sendData.Dispose();
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
                for (int i = 0; i < clients.Length; i++) _sendData.Add(new SendData(clients[i], index, _message.Length, sendMode));
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
                    if (_connections.ContainsKey(client)) _sendData.Add(new SendData(client, index, _message.Length, sendMode));
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
                _sendData.Add(new SendData(client, index, _message.Length, sendMode));
            });
        }
    }
}