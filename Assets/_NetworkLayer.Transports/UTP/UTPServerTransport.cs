using System.Collections.Generic;
using NetworkLayer.Utils;
using Unity.Burst;
using Unity.Collections;
using Unity.Collections.LowLevel.Unsafe;
using Unity.Jobs;
using Unity.Networking.Transport;
using UnityEngine;

namespace NetworkLayer.Transports.UTP {
    /// <summary>
    /// The server transport for the Unity Transport Package
    /// </summary>
    public class UTPServerTransport : NetworkServer.BaseTransport {
        /// <summary>
        /// Stores data for each client
        /// </summary>
        private struct ClientData {
            /// <summary>
            /// The network connection
            /// </summary>
            public NetworkConnection connection;
            
            /// <summary>
            /// The round trip time
            /// </summary>
            public float rtt;
        }
        
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
            public readonly ulong ClientId;
            
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
                ClientId = client;
                Type = type;
                Index = index;
                Length = length;
            }
        }
        
        /// <summary>
        /// The send ping job
        /// </summary>
        [BurstCompile]
        private struct SendPingJob : IJobFor {
            /// <summary>
            /// The concurrent network driver
            /// </summary>
            public NetworkDriver.Concurrent driver;
            
            /// <summary>
            /// All of the send data
            /// </summary>
            [ReadOnly] public NativeArray<ClientData> clientData;

            /// <summary>
            /// The id of the ping to send
            /// </summary>
            public int pingId;
            
            public void Execute(int index) {
                // Get the connection
                ClientData data = clientData[index];
                
                // Do nothing if the connection isn't listed or if the send fails
                if (driver.GetConnectionState(data.connection) != NetworkConnection.State.Connected || 0 != driver.BeginSend(data.connection, out DataStreamWriter writer)) return;

                // Write the header
                writer.WriteByte(UTPUtility.HEADER_SERVER_PING);

                // Write the ping id
                writer.WriteInt(pingId);
                
                // End the send
                driver.EndSend(writer);
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
            public NetworkDriver.Concurrent driver;
            
            /// <summary>
            /// The reliable pipeline
            /// </summary>
            public NetworkPipeline reliablePipeline;
            
            /// <summary>
            /// The hashmap of clients
            /// </summary>
            [ReadOnly] public NativeHashMap<ulong, ClientData> clientData;
            
            /// <summary>
            /// All of the send data
            /// </summary>
            [ReadOnly] public NativeList<SendData> sendList;
            
            /// <summary>
            /// The send buffer
            /// </summary>
            [ReadOnly] public NativeArray<byte> sendBuffer;

            public void Execute(int index) {
                // Get the send data
                SendData sendData = sendList[index];
                
                // Do nothing if the connection isn't listed or if the send fails
                if (!clientData.TryGetValue(sendData.Client, out ClientData data) || 0 != driver.BeginSend(sendData.SendMode == ESendMode.Reliable ? reliablePipeline : NetworkPipeline.Null, data.connection, out DataStreamWriter writer)) return;
                
                // Write the bytes from the send buffer
                writer.WriteBytes(sendBuffer.GetSubArray(sendData.Index, sendData.Length));
                
                // End the send
                driver.EndSend(writer);
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
            public NetworkDriver driver;
            
            /// <summary>
            /// The hashmap of clients to add to
            /// </summary>
            [WriteOnly] public NativeHashMap<ulong, ClientData> clientData;
            
            /// <summary>
            /// The queue of accepted connections to add to
            /// </summary>
            [WriteOnly] public NativeQueue<ulong> acceptedConnections;

            public void Execute() {
                // As long as accepted connections are valid, add to the hashmap and queue.
                NetworkConnection connection;
                while ((connection = driver.Accept()).IsCreated) {
                    clientData.Add((ulong) connection.InternalId, new ClientData {
                        connection = connection,
                        rtt = -1
                    });
                    acceptedConnections.Enqueue((ulong) connection.InternalId);
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
            public NetworkDriver driver;
            
            /// <summary>
            /// The receive buffer
            /// </summary>
            public NativeList<byte> receiveBuffer;
            
            /// <summary>
            /// The hashmap of clients
            /// </summary>
            public NativeHashMap<ulong, ClientData> clientData;
            
            /// <summary>
            /// The event queue to write to
            /// </summary>
            [WriteOnly] public NativeQueue<EventData> eventQueue;

            /// <summary>
            /// The time the job started at.
            /// </summary>
            public float currentTime;

            /// <summary>
            /// Contains the send times.
            /// </summary>
            [ReadOnly] public NativeArray<float> sendTimes;

            public void Execute() {
                // Pop all non-empty events
                NetworkEvent.Type type;
                while ((type = driver.PopEvent(out NetworkConnection connection, out DataStreamReader reader)) != NetworkEvent.Type.Empty) {
                    // Remove client if the popped event is a disconnect event
                    if (type == NetworkEvent.Type.Disconnect) clientData.Remove((ulong) connection.InternalId);
                    
                    // Check the message type
                    byte header = reader.ReadByte();

                    // Handle the data based on the header type
                    switch (header) {
                        case UTPUtility.HEADER_MESSAGE: {
                            // Cache the buffer size
                            int index = receiveBuffer.Length;
                        
                            // Resize the buffer
                            receiveBuffer.ResizeUninitialized(index + reader.Length - 1);
                        
                            // Read the data into the buffer
                            reader.ReadBytes(receiveBuffer.AsArray().GetSubArray(index, reader.Length - 1));
                        
                            // Enqueue the event
                            eventQueue.Enqueue(new EventData((ulong) connection.InternalId, type, index, reader.Length - 1));
                            break;
                        }
                        case UTPUtility.HEADER_CLIENT_PING: {
                            // Read the ping id
                            int id = reader.ReadInt();
                        
                            // Send the id back
                            if (0 != driver.BeginSend(connection, out DataStreamWriter writer)) continue;
                            writer.WriteByte(UTPUtility.HEADER_CLIENT_PING);
                            writer.WriteInt(id);
                            driver.EndSend(writer);
                            break;
                        }
                        case UTPUtility.HEADER_SERVER_PING: {
                            int id = reader.ReadInt();
                            float t = sendTimes[id];
                            if (clientData.TryGetValue((ulong) connection.InternalId, out ClientData data)) data.rtt = (currentTime - t) * 1000;
                            break;
                        }
                    }
                }
            }
        }

        private delegate void PendingDisconnectDelegate();

        private NativeHashMap<ulong, ClientData> _clientData;
        private NativeList<byte> _sendBuffer;
        private NativeList<byte> _receiveBuffer;
        private NativeList<SendData> _sendData;
        private NativeQueue<EventData> _eventQueue;
        private NativeQueue<ulong> _acceptedConnections;
        private NetworkDriver _driver;
        private NetworkPipeline _reliablePipeline;
        private JobHandle _job;
        private int _pingId;
        private NativeArray<float> _sendTimes;
        private float _lastPingTime;
        private readonly Message _message;
        private readonly Queue<PendingDisconnectDelegate> _pendingDisconnectQueue;
        private readonly Queue<SendDelegate> _sendQueue;
        private readonly Dictionary<ulong, int> _cachedRtt;

        private int _connectionCount;

        public UTPServerTransport() {
            _clientData = new NativeHashMap<ulong, ClientData>(1, Allocator.Persistent);
            _sendBuffer = new NativeList<byte>(1024, Allocator.Persistent);
            _receiveBuffer = new NativeList<byte>(1024, Allocator.Persistent);
            _sendData = new NativeList<SendData>(1, Allocator.Persistent);
            _eventQueue = new NativeQueue<EventData>(Allocator.Persistent);
            _acceptedConnections = new NativeQueue<ulong>(Allocator.Persistent);
            _message = new Message();
            _pendingDisconnectQueue = new Queue<PendingDisconnectDelegate>();
            _sendQueue = new Queue<SendDelegate>();
            _cachedRtt = new Dictionary<ulong, int>();
            _sendTimes = new NativeArray<float>(1024, Allocator.Persistent);
        }
                
        /// <summary>
        /// Write data to the send buffer
        /// </summary>
        /// <param name="messageName"></param>
        /// <param name="writeToMessage"></param>
        /// <returns></returns>
        private int WriteToSendBuffer(string messageName, WriteToMessageDelegate writeToMessage) {
            // Reset the message and write the message id and other data
            _message.Reset();
            _message.AsWriter.PutByte(UTPUtility.HEADER_MESSAGE);
            _message.AsWriter.PutUInt(Hash32.Generate(messageName));
            writeToMessage(_message.AsWriter);
            
            // Resize the send buffer and copy the message data into the buffer
            int index = _sendBuffer.Length;
            _sendBuffer.Resize(index + _message.Length, NativeArrayOptions.UninitializedMemory);
            NativeArray<byte>.Copy(_message.Data, 0, _sendBuffer.AsArray(), index, _message.Length);
            
            // Return the start index
            return index;
        }

        public override bool IsRunning => _driver.IsCreated;
        
        public override int ClientCount => _connectionCount;

        public override int GetRTT(ulong clientId) {
            if (_cachedRtt.TryGetValue(clientId, out int rtt)) return rtt;
            return -1;
        }

        public override void Host(ushort port) {
            // Do nothing if the server is running
            if (IsRunning) return;
            
            // Create the endpoint
            NetworkEndPoint endpoint = NetworkEndPoint.AnyIpv4;
            endpoint.Port = port;
            
            // Create the driver and attempt to host
            _driver = NetworkDriver.Create();
            if (0 == _driver.Bind(endpoint) && 0 == _driver.Listen()) {
                // Create the pipeline
                _reliablePipeline = _driver.CreatePipeline(typeof(ReliableSequencedPipelineStage));
                
                // Raise the host event
                OnHost();
            } else {
                // Dispose the driver if hosting fails
                _driver.Dispose();
            }
        }

        public override void Close() {
            // Do nothing if the server isn't running
            if (!IsRunning) return;
            
            // Complete the job
            _job.Complete();
            
            // Allocate the native array of connections
            using NativeArray<ClientData> clientData = _clientData.GetValueArray(Allocator.Temp);
            
            // Disconnect every connection
            for (int i = 0; i < clientData.Length; i++) _driver.Disconnect(clientData[i].connection);
            
            // Send all events to the clients
            _driver.ScheduleFlushSend(default).Complete();
            
            // Clear all the lists and queues
            _clientData.Clear();
            _pendingDisconnectQueue.Clear();
            _sendQueue.Clear();
            _eventQueue.Clear();
            _acceptedConnections.Clear();

            // Dispose the driver and pipelines
            _driver.Dispose();
            _reliablePipeline = default;
            
            // Raise the close event
            OnClose();
        }

        public override void Disconnect(ulong clientId) {
            // Do nothing if the server isn't running
            if (!IsRunning) return;
            
            // Enqueue the disconnect
            _pendingDisconnectQueue.Enqueue(() => {
                // Do nothing if the connection does not exist
                if (!_clientData.TryGetValue(clientId, out ClientData data)) return;
                
                // Disconnect the connection
                _driver.Disconnect(data.connection);
                
                // Remove the connection
                _clientData.Remove(clientId);

                // Raise the disconnect event
                OnDisconnect(clientId);
            });
        }

        protected override void Update() {
            // Complete the job
            _job.Complete();

            // Do nothing if the server is not running
            if (!IsRunning) return;

            // Update the connection count
            _connectionCount = _clientData.Count();
            
            // Dequeue all accepted connections
            while (_acceptedConnections.TryDequeue(out ulong client)) {
                // Add the rtt
                _cachedRtt[client] = -1;
                
                // Raise the connect event
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
                    
                    // Receive the message
                    OnReceiveMessage(networkEvent.ClientId, _message.AsReader);
                } else {
                    // Remove the rtt
                    _cachedRtt.Remove(networkEvent.ClientId);
                    
                    // Raise the disconnect event
                    OnDisconnect(networkEvent.ClientId);
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
            
            // Get the current time
            float currentTime = Time.realtimeSinceStartup;
            
            // Initialize the job
            SendPingJob sendPingJob = default;
            NativeArray<ClientData> clientData = default;
            
            // Copy rtt
            foreach (KeyValue<ulong, ClientData> data in _clientData) _cachedRtt[data.Key] = Mathf.RoundToInt(data.Value.rtt);
            
            // Check if rtt can be retrieved
            bool sendPing = _lastPingTime == 0 || currentTime >= _lastPingTime + 1;
            if (sendPing) {
                _lastPingTime = currentTime;
                _sendTimes[_pingId] = currentTime;
                _pingId = (_pingId + 1) % 1024;
                clientData = _clientData.GetValueArray(Allocator.TempJob);
                sendPingJob = new SendPingJob {
                    driver = _driver.ToConcurrent(),
                    clientData = clientData,
                    pingId = _pingId
                };
            }
            
            // Create jobs
            SendDataJob sendDataJob = new SendDataJob {
                driver = _driver.ToConcurrent(),
                reliablePipeline = _reliablePipeline,
                clientData = _clientData,
                sendList = _sendData,
                sendBuffer = _sendBuffer
            };
            AcceptConnectionsJob acceptConnectionsJob = new AcceptConnectionsJob {
                driver = _driver,
                clientData = _clientData,
                acceptedConnections = _acceptedConnections
            };
            ProcessJob processJob = new ProcessJob {
                driver = _driver,
                clientData = _clientData,
                eventQueue = _eventQueue,
                receiveBuffer = _receiveBuffer,
                currentTime = currentTime,
                sendTimes = _sendTimes
            };
            
            // Schedule jobs
            if (sendPing) {
                _job = sendPingJob.ScheduleParallel(clientData.Length, 1, default);
                _job = clientData.Dispose(_job);
            } else _job = default;
            if (_sendData.Length > 0) _job = sendDataJob.ScheduleParallel(_sendData.Length, 1, default);
            _job = _driver.ScheduleUpdate(_job);
            _job = acceptConnectionsJob.Schedule(_job);
            _job = processJob.Schedule(_job);
            
            // Raise the update event
            OnUpdate();
        }

        public override void Dispose() {
            Close();
            _clientData.Dispose();
            _sendBuffer.Dispose();
            _receiveBuffer.Dispose();
            _sendData.Dispose();
            _eventQueue.Dispose();
            _acceptedConnections.Dispose();
            _sendTimes.Dispose();
        }

        public override void Send(string messageName, WriteToMessageDelegate writeToMessage, ESendMode sendMode) {
            // Do nothing if the server is not running
            if (!IsRunning) return;
            
            // Enqueue the send
            _sendQueue.Enqueue(() => {
                // Write the message to the send buffer
                int index = WriteToSendBuffer(messageName, writeToMessage);
                
                // Enqueue the send for every client
                using NativeArray<ulong> clients = _clientData.GetKeyArray(Allocator.Temp);
                for (int i = 0; i < clients.Length; i++) _sendData.Add(new SendData(clients[i], index, _message.Length, sendMode));
            });
        }

        public override void Send(NetworkServer.SendFilterDelegate filter, string messageName, WriteToMessageDelegate writeToMessage, ESendMode sendMode) {
            // Do nothing if the server is not running
            if (!IsRunning) return;
            
            // Enqueue the send
            _sendQueue.Enqueue(() => {
                // Write the message to the send buffer
                int index = WriteToSendBuffer(messageName, writeToMessage);

                // Enqueue the send for every filtered client
                using NativeArray<ulong> clients = _clientData.GetKeyArray(Allocator.Temp);
                for (int i = 0; i < clients.Length; i++)
                    if (filter(clients[i]))
                        _sendData.Add(new SendData(clients[i], index, _message.Length, sendMode));
            });
        }

        public override void Send(ulong clientId, string messageName, WriteToMessageDelegate writeToMessage, ESendMode sendMode) {
            // Do nothing if the server is not running
            if (!IsRunning) return;
            
            // Enqueue the send
            _sendQueue.Enqueue(() => {
                // Do nothing if the client isn't connected
                if (!_clientData.ContainsKey(clientId)) return;
                
                // Write the message to the send buffer
                int index = WriteToSendBuffer(messageName, writeToMessage);

                // Enqueue the send
                _sendData.Add(new SendData(clientId, index, _message.Length, sendMode));
            });
        }

        public override void PopulateClientIds(List<ulong> clientIds) {
            clientIds.Clear();
            using NativeArray<ulong> ids = _clientData.GetKeyArray(Allocator.Temp);
            for (int i = 0; i < ids.Length; i++) clientIds.Add(ids[i]);
        }
    }
}