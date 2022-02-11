using System;
using System.Collections.Generic;
using NetworkLayer.Utils;
using Unity.Burst;
using Unity.Collections;
using Unity.Jobs;
using Unity.Networking.Transport;
using UnityEngine;

namespace NetworkLayer.Transports.UTP {
    /// <summary>
    /// The server transport for the Unity Transport Package
    /// </summary>
    public class UTPServerTransport : ServerTransport {
        /// <summary>
        /// The send data
        /// </summary>
        private readonly struct SendData {
            /// <summary>
            /// The id of the client to send to 
            /// </summary>
            public readonly ulong Client;
            
            /// <summary>
            /// The index in the send buffer the data exists at
            /// </summary>
            public readonly int Index;
            
            /// <summary>
            /// The length of the data to send
            /// </summary>
            public readonly int Length;
            
            /// <summary>
            /// The send mode
            /// </summary>
            public readonly ESendMode SendMode;

            public SendData(ulong client, int index, int length, ESendMode sendMode) {
                Client = client;
                Index = index;
                Length = length;
                SendMode = sendMode;
            }
        }

        /// <summary>
        /// The data of the event
        /// </summary>
        private readonly struct EventData {
            /// <summary>
            /// The id of the client that sent the event
            /// </summary>
            public readonly ulong Client;
            
            /// <summary>
            /// The event type
            /// </summary>
            public readonly NetworkEvent.Type Type;
            
            /// <summary>
            /// The index in the receive buffer the data exists at
            /// </summary>
            public readonly int Index;
            
            /// <summary>
            /// The length of the data in the receive buffer
            /// </summary>
            public readonly int Length;

            public EventData(ulong client, NetworkEvent.Type type, int index, int length) {
                Client = client;
                Type = type;
                Index = index;
                Length = length;
            }
        }
        
        /// <summary>
        /// The send data job
        /// </summary>
        [BurstCompile]
        private struct SendDataJob : IJobFor {
            /// <summary>
            /// The concurrent network driver
            /// </summary>
            public NetworkDriver.Concurrent Driver;
            
            /// <summary>
            /// The reliable pipeline
            /// </summary>
            public NetworkPipeline ReliablePipeline;
            
            /// <summary>
            /// The hashmap of connections
            /// </summary>
            [ReadOnly] public NativeHashMap<ulong, NetworkConnection> Connections;
            
            /// <summary>
            /// All of the send data
            /// </summary>
            [ReadOnly] public NativeList<SendData> SendList;
            
            /// <summary>
            /// The send buffer
            /// </summary>
            [ReadOnly] public NativeArray<byte> SendBuffer;

            public void Execute(int index) {
                // Get the send data
                SendData sendData = SendList[index];
                
                // Do nothing if the connection isn't listed or if the send fails
                if (!Connections.TryGetValue(sendData.Client, out NetworkConnection connection) || 0 != Driver.BeginSend(sendData.SendMode == ESendMode.Reliable ? ReliablePipeline : NetworkPipeline.Null, connection, out DataStreamWriter writer)) return;
                
                // Write the bytes from the send buffer
                writer.WriteBytes(SendBuffer.GetSubArray(sendData.Index, sendData.Length));
                
                // End the send
                Driver.EndSend(writer);
            }
        }

        /// <summary>
        /// The accept connections job
        /// </summary>
        [BurstCompile]
        private struct AcceptConnectionsJob : IJob {
            /// <summary>
            /// The network driver
            /// </summary>
            public NetworkDriver Driver;
            
            /// <summary>
            /// The hashmap of connections to add to
            /// </summary>
            [WriteOnly] public NativeHashMap<ulong, NetworkConnection> Connections;
            
            /// <summary>
            /// The queue of accepted connections to add to
            /// </summary>
            [WriteOnly] public NativeQueue<ulong> AcceptedConnections;

            public void Execute() {
                // As long as accepted connections are valid, add to the hashmap and queue.
                NetworkConnection connection;
                while ((connection = Driver.Accept()).IsCreated) {
                    Connections.Add((ulong) connection.InternalId, connection);
                    AcceptedConnections.Enqueue((ulong) connection.InternalId);
                }
            }
        }

        /// <summary>
        /// Process every event
        /// </summary>
        [BurstCompile]
        private struct ProcessJob : IJob {
            /// <summary>
            /// The network driver
            /// </summary>
            public NetworkDriver Driver;
            
            /// <summary>
            /// The receive buffer
            /// </summary>
            public NativeList<byte> ReceiveBuffer;
            
            /// <summary>
            /// The hashmap of connections
            /// </summary>
            public NativeHashMap<ulong, NetworkConnection> Connections;
            
            /// <summary>
            /// The event queue to write to
            /// </summary>
            [WriteOnly] public NativeQueue<EventData> EventQueue;

            public void Execute() {
                // Pop all non-empty events
                NetworkEvent.Type type;
                while ((type = Driver.PopEvent(out NetworkConnection connection, out DataStreamReader reader)) != NetworkEvent.Type.Empty) {
                    // Remove connections if the popped event is a disconnect event
                    if (type == NetworkEvent.Type.Disconnect) Connections.Remove((ulong) connection.InternalId);
                    
                    // Check the message type
                    byte header = reader.ReadByte();
                    
                    // If the header is 0, then this is a regular message.
                    // If the header is 1, then this is a client ping.
                    if (header == 0) {
                        // Cache the buffer size
                        int index = ReceiveBuffer.Length;
                        
                        // Resize the buffer
                        ReceiveBuffer.ResizeUninitialized(index + reader.Length - 1);
                        
                        // Read the data into the buffer
                        reader.ReadBytes(ReceiveBuffer.AsArray().GetSubArray(index, reader.Length - 1));
                        
                        // Enqueue the event
                        EventQueue.Enqueue(new EventData((ulong) connection.InternalId, type, index, reader.Length - 1));
                    } else if (header == 1) {
                        // Read the ping id
                        int id = reader.ReadInt();
                        
                        // Send the id back
                        if (0 == Driver.BeginSend(connection, out DataStreamWriter writer)) {
                            writer.WriteByte(1);
                            writer.WriteInt(id);
                            Driver.EndSend(writer);
                        }
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

        public override bool IsRunning => _driver.IsCreated;

        /// <summary>
        /// Write data to the send buffer
        /// </summary>
        /// <param name="messageId"></param>
        /// <param name="writeMessage"></param>
        private void WriteToSendBuffer(uint messageId, WriteMessageDelegate writeMessage) {
            // Reset the message and write the message id and other data
            _message.Reset();
            _message.AsWriter.PutByte(0);
            _message.AsWriter.PutUInt(messageId);
            writeMessage(_message.AsWriter);
            
            // Resize the send buffer and copy the message data into the buffer
            int index = _sendBuffer.Length;
            _sendBuffer.Resize(index + _message.Length, NativeArrayOptions.UninitializedMemory);
            NativeArray<byte>.Copy(_message.Data, 0, _sendBuffer.AsArray(), index, _message.Length);
        }

        public override void Host(ushort port) {
            // Do nothing if the server is running
            if (IsRunning) {
                OnLog("Server - Cannot host. Reason: already hosting!");
                return;
            }
            
            // Create the endpoint
            NetworkEndPoint endpoint = NetworkEndPoint.AnyIpv4;
            endpoint.Port = port;
            
            // Create the driver and attempt to host
            _driver = NetworkDriver.Create();
            if (0 == _driver.Bind(endpoint) && 0 == _driver.Listen()) {
                // Create the pipeline
                _reliablePipeline = _driver.CreatePipeline(typeof(ReliableSequencedPipelineStage));
                
                // Raise the host event
                OnLog("Server - Hosting server!");
                OnHost();
            } else {
                // Dispose the driver if hosting fails
                _driver.Dispose();
                OnLog("Server - Cannot host. Reason: server failed to bind or listen!");
            }
        }

        public override void Close() {
            // Do nothing if the server isn't running
            if (!IsRunning) {
                OnLog("Server - Cannot close server. Reason: server already closed!");
                return;
            }
            
            // Complete the job
            _job.Complete();
            
            // Allocate the native array of connections
            using NativeArray<NetworkConnection> connections = _connections.GetValueArray(Allocator.Temp);
            
            // Disconnect every connection
            for (int i = 0; i < connections.Length; i++) _driver.Disconnect(connections[i]);
            
            // Send all events to the clients
            _driver.ScheduleFlushSend(default).Complete();
            
            // Clear all the lists and queues
            _connections.Clear();
            _pendingDisconnectQueue.Clear();
            _sendQueue.Clear();
            _eventQueue.Clear();
            _acceptedConnections.Clear();
            
            // Dispose the driver and pipelines
            _driver.Dispose();
            _reliablePipeline = default;
            
            // Raise the close event
            OnLog("Server - Closed server!");
            OnClose();
        }

        public override void Disconnect(ulong client) {
            // Do nothing if the server isn't running
            if (!IsRunning) {
                OnLog($"Server - Cannot disconnect client {client}. Reason: server is closed!");
                return;
            }
            
            // Enqueue the disconnect
            _pendingDisconnectQueue.Enqueue(() => {
                // Do nothing if the connection does not exist
                if (!_connections.TryGetValue(client, out NetworkConnection connection)) {
                    OnLog($"Server - Cannot disconnect client {client}. Reason: client not found!");
                    return;
                }
                
                // Disconnect the connection
                _driver.Disconnect(connection);
                
                // Remove the connection
                _connections.Remove(client);
                
                // Raise the disconnect event
                OnLog($"Server - Disconnected client {client}!");
                OnDisconnect(client);
            });
        }

        public override void Update() {
            // Complete the job
            _job.Complete();

            // Do nothing if the server is not running
            if (!IsRunning) return;

            // Raise the connect event for all accepted connections
            while (_acceptedConnections.TryDequeue(out ulong client)) {
                OnLog($"Server - Client {client} connected!");
                OnConnect(client);
            }

            // Process all events
            while (_eventQueue.TryDequeue(out EventData networkEvent)) {
                // If the event is a data event, process the data.
                // Otherwise, it is a disconnect event.
                if (networkEvent.Type == NetworkEvent.Type.Data) {
                    // Reset the message and resize it
                    _message.Reset();
                    _message.Resize(networkEvent.Length);
                    
                    // Copy the receive buffer data into the message
                    NativeArray<byte>.Copy(_receiveBuffer, networkEvent.Index, _message.Data, 0, networkEvent.Length);
                    
                    // Try to receive the message. If it fails, log the error
                    try {
                        OnReceiveMessage(networkEvent.Client, _message.AsReader);
                    } catch (Exception exception) {
                        OnLog($"Server - Error trying to receive data from client {networkEvent.Client}! Message: {exception}");
                    }
                } else {
                    // Raise the disconnect event
                    OnLog($"Server - Client {networkEvent.Client} disconnected!");
                    OnDisconnect(networkEvent.Client);
                }
            }
            
            // Process all pending disconnects
            while (_pendingDisconnectQueue.Count > 0) _pendingDisconnectQueue.Dequeue()();
            
            // Clear send and receive buffers
            _sendData.Clear();
            _sendBuffer.Clear();
            _receiveBuffer.Clear();
            
            // Process all sends
            while (_sendQueue.Count > 0) _sendQueue.Dequeue()();
            
            // Create jobs
            SendDataJob sendDataJob = new SendDataJob {
                Driver = _driver.ToConcurrent(),
                ReliablePipeline = _reliablePipeline,
                Connections = _connections,
                SendList = _sendData,
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
            
            // Schedule jobs
            _job = _sendData.Length > 0 ? sendDataJob.ScheduleParallel(_sendData.Length, 1, default) : default;
            _job = _driver.ScheduleUpdate(_job);
            _job = acceptConnectionsJob.Schedule(_job);
            _job = processJob.Schedule(_job);
            
            // Raise the update event
            OnUpdate();
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
            // Do nothing if the server is not running
            if (!IsRunning) {
                OnLog($"Server - Cannot send message {messageId} to all clients. Reason: server not running!");
                return;
            }
            
            // Enqueue the send
            _sendQueue.Enqueue(() => {
                // Cache the length of the send buffer
                int index = _sendBuffer.Length;
                
                // Write the message to the send buffer
                WriteToSendBuffer(messageId, writeMessage);
                
                // Enqueue the send for every client
                using NativeArray<ulong> clients = _connections.GetKeyArray(Allocator.Temp);
                for (int i = 0; i < clients.Length; i++) _sendData.Add(new SendData(clients[i], index, _message.Length, sendMode));
            });
        }

        public override void SendMessageToFilter(FilterClientDelegate filter, uint messageId, WriteMessageDelegate writeMessage, ESendMode sendMode) {
            // Do nothing if the server is not running
            if (!IsRunning) {
                OnLog($"Server - Cannot send message {messageId} to clients. Reason: server not running!");
                return;
            }
            
            // Enqueue the send
            _sendQueue.Enqueue(() => {
                // Cache the length of the send buffer
                int index = _sendBuffer.Length;
                
                // Write the message to the send buffer
                WriteToSendBuffer(messageId, writeMessage);
                
                // Enqueue the send for every filtered client
                using NativeArray<ulong> clients = _connections.GetKeyArray(Allocator.Temp);
                for (int i = 0; i < clients.Length; i++)
                    if (filter(clients[i]))
                        _sendData.Add(new SendData(clients[i], index, _message.Length, sendMode));
            });
        }

        public override void SendMessageToClient(ulong client, uint messageId, WriteMessageDelegate writeMessage, ESendMode sendMode) {
            // Do nothing if the server is not running
            if (!IsRunning) {
                OnLog($"Server - Cannot send message {messageId} to client {client}. Reason: server not running!");
                return;
            }
            
            // Enqueue the send
            _sendQueue.Enqueue(() => {
                // Do nothing if the client isn't connected
                if (!_connections.ContainsKey(client)) {
                    OnLog($"Server - Cannot send message {messageId} to client {client}. Reason: client not found!");
                    return;
                }
                
                // Cache the length of the send buffer
                int index = _sendBuffer.Length;
                
                // Write the message to the send buffer
                WriteToSendBuffer(messageId, writeMessage);
                
                // Enqueue the send
                _sendData.Add(new SendData(client, index, _message.Length, sendMode));
            });
        }
    }
}