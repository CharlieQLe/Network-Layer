using System.Collections.Generic;
using NetworkLayer.Utils;
using Unity.Burst;
using Unity.Collections;
using Unity.Jobs;
using Unity.Networking.Transport;
using UnityEngine;

namespace NetworkLayer.Transports.UTP {
    /// <summary>
    /// The client transport for the Unity Transport Package
    /// </summary>
    public class UTPClientTransport : NetworkClient.BaseTransport {
        /// <summary>
        /// The send data
        /// </summary>
        private readonly struct SendData {
            /// <summary>
            /// The index in the send buffer the data exists at
            /// </summary>
            public readonly int index;

            /// <summary>
            /// The length of the data to send
            /// </summary>
            public readonly int length;

            /// <summary>
            /// The send mode
            /// </summary>
            public readonly ESendMode sendMode;

            public SendData(int index, int length, ESendMode sendMode) {
                this.index = index;
                this.length = length;
                this.sendMode = sendMode;
            }
        }

        /// <summary>
        /// The data of the event
        /// </summary>
        private readonly struct EventData {
            /// <summary>
            /// The event type
            /// </summary>
            public readonly NetworkEvent.Type type;

            /// <summary>
            /// The index in the receive buffer the data exists at
            /// </summary>
            public readonly int index;

            /// <summary>
            /// The length of the data in the receive buffer
            /// </summary>
            public readonly int length;

            public EventData(NetworkEvent.Type type, int index, int length) {
                this.type = type;
                this.index = index;
                this.length = length;
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
            /// The sequenced pipeline
            /// </summary>
            public NetworkPipeline sequencedPipeline;

            /// <summary>
            /// The connection to the server
            /// </summary>
            [ReadOnly] public NativeArray<NetworkConnection> connection;

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

                // Do nothing if the data cannot be send
                if (0 != driver.BeginSend(sendData.sendMode switch {
                        ESendMode.Reliable => reliablePipeline,
                        ESendMode.Sequenced => sequencedPipeline,
                        _ => NetworkPipeline.Null
                    }, connection[0], out DataStreamWriter writer)) return;

                // Write to the data stream from the send buffer
                writer.WriteBytes(sendBuffer.GetSubArray(sendData.index, sendData.length));

                // End the send
                driver.EndSend(writer);
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
            /// The connection to the server
            /// </summary>
            public NativeArray<NetworkConnection> connection;

            /// <summary>
            /// The event queue
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

            /// <summary>
            /// Contains the rtt.
            /// </summary>
            [WriteOnly] public NativeArray<float> rtt;

            public void Execute() {
                // Pop every event
                NetworkEvent.Type type;
                while ((type = driver.PopEvent(out NetworkConnection _, out DataStreamReader reader)) != NetworkEvent.Type.Empty) {
                    // If this is a connect event, simply enqueue the event
                    // Otherwise, handle the data in the stream
                    if (type == NetworkEvent.Type.Connect) eventQueue.Enqueue(new EventData(type, 0, 0));
                    else {
                        // Set the connection to default if it is a disconnect event
                        if (type == NetworkEvent.Type.Disconnect) connection[0] = default;

                        // Check the header type
                        byte header = reader.ReadByte();

                        // Handle data based on the header
                        switch (header) {
                            case UTPUtility.HEADER_MESSAGE: {
                                // Cache the buffer size
                                int index = receiveBuffer.Length;

                                // Resize the buffer
                                receiveBuffer.ResizeUninitialized(index + reader.Length - 1);

                                // Read the data into the buffer
                                reader.ReadBytes(receiveBuffer.AsArray().GetSubArray(index, reader.Length - 1));

                                // Enqueue the event data
                                eventQueue.Enqueue(new EventData(type, index, reader.Length - 1));
                                break;
                            }
                            case UTPUtility.HEADER_CLIENT_RTT: {;
                                int id = reader.ReadInt();
                                float t = sendTimes[id];
                                rtt[0] = (currentTime - t) * 1000;
                                break;
                            }
                            case UTPUtility.HEADER_SERVER_RTT: {
                                // Read the ping id
                                int id = reader.ReadInt();
                        
                                // Send the id back
                                if (0 != driver.BeginSend(connection[0], out DataStreamWriter writer)) continue;
                                writer.WriteByte(UTPUtility.HEADER_SERVER_RTT);
                                writer.WriteInt(id);
                                driver.EndSend(writer);
                                break;
                            }
                        }
                    }
                }
            }
        }

        private NativeArray<NetworkConnection> _connection;
        private NativeList<byte> _sendBuffer;
        private NativeList<byte> _receiveBuffer;
        private NativeList<SendData> _sendData;
        private NativeQueue<EventData> _eventQueue;
        private NetworkDriver _driver;
        private NetworkPipeline _reliablePipeline;
        private NetworkPipeline _sequencedPipeline;
        private JobHandle _job;
        private EClientState _state;
        private readonly Message _message;
        private readonly Queue<SendDelegate> _sendQueue;
        private int _rttId;
        private NativeArray<float> _sendTimes;
        private NativeArray<float> _rtt;
        private float _lastPingTime;
        private float _storedRtt;

        public UTPClientTransport() {
            _connection = new NativeArray<NetworkConnection>(1, Allocator.Persistent);
            _driver = NetworkDriver.Create();
            _reliablePipeline = _driver.CreatePipeline(typeof(ReliableSequencedPipelineStage));
            _sequencedPipeline = _driver.CreatePipeline(typeof(UnreliableSequencedPipelineStage));
            _sendBuffer = new NativeList<byte>(1024, Allocator.Persistent);
            _receiveBuffer = new NativeList<byte>(1024, Allocator.Persistent);
            _sendData = new NativeList<SendData>(1, Allocator.Persistent);
            _eventQueue = new NativeQueue<EventData>(Allocator.Persistent);
            _message = new Message();
            _sendQueue = new Queue<SendDelegate>();
            _sendTimes = new NativeArray<float>(1024, Allocator.Persistent);
            _rtt = new NativeArray<float>(1, Allocator.Persistent);
            _rtt[0] = -1;
        }

        /// <summary>
        /// Handle disconnecting.
        /// </summary>
        private void HandleDisconnect() {
            // Set the connection to default
            _connection[0] = default;
            _state = EClientState.Disconnected;

            // Clear data
            _sendData.Clear();
            _eventQueue.Clear();
            _sendQueue.Clear();
            _rtt[0] = -1;

            // Raise disconnect event
            OnDisconnect();
        }
        
        public override EClientState State => _state;
        public override int Rtt => _state == EClientState.Connected ? Mathf.RoundToInt(_storedRtt) : -1;

        public override void Connect(string address, ushort port) {
            // Do nothing if not disconnected
            if (State != EClientState.Disconnected) return;

            // Try to parse the endpoint. If it fails, do nothing
            NetworkEndPoint endpoint;
            if (address == "localhost" || address == "127.0.0.1") {
                endpoint = NetworkEndPoint.LoopbackIpv4;
                endpoint.Port = port;
            } else if (!NetworkEndPoint.TryParse(address, port, out endpoint)) return;

            // Try to connect
            _connection[0] = _driver.Connect(endpoint);

            // Set the connection state
            _state = UTPUtility.ConvertConnectionState(_driver.GetConnectionState(_connection[0]));

            // Raise the start connecting event
            OnStartConnecting();
        }

        public override void Disconnect() {
            // Do nothing if already disconnected
            if (State == EClientState.Disconnected) return;

            // Complete the job
            _job.Complete();

            // Disconnect the connection
            _driver.Disconnect(_connection[0]);

            // Send all pending events 
            _driver.ScheduleFlushSend(default).Complete();

            // Handle disconnecting
            HandleDisconnect();
        }

        protected override void Update() {
            // Complete the job
            _job.Complete();
            
            // Do nothing if the connection is not available
            if (!_connection.IsCreated || !_connection[0].IsCreated) return;

            // Set the rtt
            _storedRtt = _rtt[0];
            
            // Update the connection state
            _state = UTPUtility.ConvertConnectionState(_driver.GetConnectionState(_connection[0]));
            
            // Dequeue all events
            while (_eventQueue.TryDequeue(out EventData networkEvent)) {
                switch (networkEvent.type) {
                    case NetworkEvent.Type.Connect: {
                        // Raise the connect event
                        OnConnect();
                        break;
                    }
                    case NetworkEvent.Type.Data: {
                        // Reset and resize the message
                        _message.Reset();
                        _message.Resize(networkEvent.length);

                        // Copy the buffer into the message
                        NativeArray<byte>.Copy(_receiveBuffer, networkEvent.index, _message.Data, 0, networkEvent.length);

                        // Receive the message
                        OnReceiveMessage(_message.AsReader);
                        break;
                    }
                    case NetworkEvent.Type.Disconnect: {
                        // Handle disconnecting.
                        HandleDisconnect();
                        return;
                    }
                }
            }

            // Clear data and buffers
            _sendData.Clear();
            _sendBuffer.Clear();
            _receiveBuffer.Clear();

            // Process the send data
            while (_sendQueue.Count > 0) _sendQueue.Dequeue()();

            // Get the current time
            float currentTime = Time.realtimeSinceStartup;
            
            // Check if it is time to update rtt
            if (_lastPingTime == 0 || currentTime >= _lastPingTime + 1) {
                // Set the last ping time
                _lastPingTime = currentTime;
                
                // Set the send time
                _sendTimes[_rttId] = currentTime;
                
                // Write the data into the message
                _message.Reset();
                _message.AsWriter.PutByte(UTPUtility.HEADER_CLIENT_RTT);
                _message.AsWriter.PutInt(_rttId);

                // Copy the message data into the send buffer
                int index = _sendBuffer.Length;
                _sendBuffer.ResizeUninitialized(index + _message.Length);
                NativeArray<byte>.Copy(_message.Data, 0, _sendBuffer.AsArray(), index, _message.Length);

                // Add the send data
                _sendData.Add(new SendData(index, _message.Length, ESendMode.Unreliable));
                
                // Calculate the next ping id
                _rttId = (_rttId + 1) % 1024;
            }

            // Create jobs
            SendDataJob sendDataJob = new SendDataJob {
                driver = _driver.ToConcurrent(),
                connection = _connection,
                reliablePipeline = _reliablePipeline,
                sequencedPipeline = _sequencedPipeline,
                sendList = _sendData,
                sendBuffer = _sendBuffer
            };
            ProcessJob processJob = new ProcessJob {
                driver = _driver,
                connection = _connection,
                eventQueue = _eventQueue,
                receiveBuffer = _receiveBuffer,
                currentTime = currentTime,
                sendTimes = _sendTimes,
                rtt = _rtt
            };

            // Schedule jobs
            _job = _state == EClientState.Connected && _sendData.Length > 0 ? sendDataJob.ScheduleParallel(_sendData.Length, 1, _job) : default;
            _job = _driver.ScheduleUpdate(_job);
            _job = processJob.Schedule(_job);

            // Raise the update event
            OnUpdate();
        }

        public override void Dispose() {
            Disconnect();
            _connection.Dispose();
            _driver.Dispose();
            _sendBuffer.Dispose();
            _receiveBuffer.Dispose();
            _sendData.Dispose();
            _eventQueue.Dispose();
            _sendTimes.Dispose();
            _rtt.Dispose();
            _reliablePipeline = default;
            _sequencedPipeline = default;
        }

        public override void Send(string messageName, WriteToMessageDelegate writeToMessage, ESendMode sendMode) {
            // Do nothing if not connected
            if (State != EClientState.Connected) return;

            _sendQueue.Enqueue(() => {
                // Write the data into the message
                _message.Reset();
                _message.AsWriter.PutByte(UTPUtility.HEADER_MESSAGE);
                _message.AsWriter.PutUInt(Hash32.Generate(messageName));
                writeToMessage(_message.AsWriter);

                // Copy the message data into the send buffer
                int index = _sendBuffer.Length;
                _sendBuffer.ResizeUninitialized(index + _message.Length);
                NativeArray<byte>.Copy(_message.Data, 0, _sendBuffer.AsArray(), index, _message.Length);

                // Add the send data
                _sendData.Add(new SendData(index, _message.Length, sendMode));
            });
        }
    }
}