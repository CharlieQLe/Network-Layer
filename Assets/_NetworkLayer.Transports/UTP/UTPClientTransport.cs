using System;
using System.Collections.Generic;
using NetworkLayer.Utils;
using Unity.Burst;
using Unity.Collections;
using Unity.Jobs;
using Unity.Mathematics;
using Unity.Networking.Transport;
using UnityEngine;

namespace NetworkLayer.Transports.UTP {
    /// <summary>
    /// The client transport for the Unity Transport Package
    /// </summary>
    public class UTPClientTransport : ClientTransport {
        /// <summary>
        /// The send data
        /// </summary>
        private readonly struct SendData {
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

            public SendData(int index, int length, ESendMode sendMode) {
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

            public EventData(NetworkEvent.Type type, int index, int length) {
                Type = type;
                Index = index;
                Length = length;
            }
        }

        /// <summary>
        /// Send a ping id to the server.
        /// </summary>
        [BurstCompile]
        private struct SendPingJob : IJob {
            public int PingId;
            public NetworkDriver Driver;
            [ReadOnly] public NativeArray<NetworkConnection> Connection;

            public void Execute() {
                if (Driver.GetConnectionState(Connection[0]) != NetworkConnection.State.Connected || 0 != Driver.BeginSend(Connection[0], out DataStreamWriter writer)) return;
                writer.WriteByte(1);
                writer.WriteInt(PingId);
                Driver.EndSend(writer);
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
            /// The connection to the server
            /// </summary>
            [ReadOnly] public NativeArray<NetworkConnection> Connection;

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

                // Do nothing if the data cannot be send
                if (0 != Driver.BeginSend(sendData.SendMode == ESendMode.Reliable ? ReliablePipeline : NetworkPipeline.Null, Connection[0], out DataStreamWriter writer)) return;

                // Write to the data stream from the send buffer
                writer.WriteBytes(SendBuffer.GetSubArray(sendData.Index, sendData.Length));

                // End the send
                Driver.EndSend(writer);
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
            /// The connection to the server
            /// </summary>
            [WriteOnly] public NativeArray<NetworkConnection> Connection;

            /// <summary>
            /// The event queue
            /// </summary>
            [WriteOnly] public NativeQueue<EventData> EventQueue;

            public float CurrentTime;

            /// <summary>
            /// Contains the send times.
            /// </summary>
            [ReadOnly] public NativeArray<float> SendTimes;

            /// <summary>
            /// Contains the rtt.
            /// </summary>
            [WriteOnly] public NativeArray<float> Rtt;

            public void Execute() {
                // Pop every event
                NetworkEvent.Type type;
                while ((type = Driver.PopEvent(out NetworkConnection _, out DataStreamReader reader)) != NetworkEvent.Type.Empty) {
                    // If this is a connect event, simply enqueue the event
                    // Otherwise, handle the data in the stream
                    if (type == NetworkEvent.Type.Connect) EventQueue.Enqueue(new EventData(type, 0, 0));
                    else {
                        // Set the connection to default if it is a disconnect event
                        if (type == NetworkEvent.Type.Disconnect) Connection[0] = default;

                        // Check the header type
                        byte header = reader.ReadByte();

                        // If the header is 0, then this is a regular message
                        // If the header is 1, then this is a client ping
                        if (header == 0) {
                            // Cache the buffer size
                            int index = ReceiveBuffer.Length;

                            // Resize the buffer
                            ReceiveBuffer.ResizeUninitialized(index + reader.Length);

                            // Read the data into the buffer
                            reader.ReadBytes(ReceiveBuffer.AsArray().GetSubArray(index, reader.Length));

                            // Enqueue the event data
                            EventQueue.Enqueue(new EventData(type, index + 1, reader.Length - 1));
                        }
                        else if (header == 1) {
                            int id = reader.ReadInt();
                            float t = SendTimes[id];
                            Rtt[0] = (CurrentTime - t) * 1000;
                        }
                    }
                }
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

        private int _pingId;
        private NativeArray<float> _sendTimes;
        private NativeArray<float> _rtt;
        private float _lastPingTime;

        private float _storedRtt;
        
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

            _sendTimes = new NativeArray<float>(1024, Allocator.Persistent);
            _rtt = new NativeArray<float>(1, Allocator.Persistent);
            _rtt[0] = -1;
        }

        public UTPClientTransport(string messageGroupName) : this(Hash32.Generate(messageGroupName)) { }

        /// <summary>
        /// Convert the network connection state to the client state.
        /// </summary>
        /// <param name="state"></param>
        /// <returns></returns>
        private EClientState ConvertConnectionState(NetworkConnection.State state) => state switch {
            NetworkConnection.State.Connected => EClientState.Connected,
            NetworkConnection.State.Connecting => EClientState.Connecting,
            _ => EClientState.Disconnected
        };

        public override EClientState State => _state;
        public override int Rtt => _state == EClientState.Connected ? Mathf.RoundToInt(_storedRtt) : -1;

        public override void Connect(string address, ushort port) {
            // Do nothing if not disconnected
            if (State != EClientState.Disconnected) {
                OnLog("Client - Cannot attempt connection. Reason: already connecting/connected!");
                return;
            }

            // Try to parse the endpoint. If it fails, do nothing
            NetworkEndPoint endpoint;
            if (address == "localhost" || address == "127.0.0.1") {
                endpoint = NetworkEndPoint.LoopbackIpv4;
                endpoint.Port = port;
            }
            else if (!NetworkEndPoint.TryParse(address, port, out endpoint)) {
                OnLog("Client - Cannot attempt connection. Reason: endpoint could not be resolved!");
                return;
            }

            // Try to connect
            _connection[0] = _driver.Connect(endpoint);

            // Set the connection state
            _state = ConvertConnectionState(_driver.GetConnectionState(_connection[0]));

            // Raise the attempt connection event
            OnLog("Client - Attempting to connect to the server...");
            OnAttemptConnection();
        }

        public override void Disconnect() {
            // Do nothing if already disconnected
            if (State == EClientState.Disconnected) {
                OnLog("Client - Cannot disconnect. Reason: already disconnected!");
                return;
            }

            // Complete the job
            _job.Complete();

            // Disconnect the connection
            _driver.Disconnect(_connection[0]);

            // Send all pending events 
            _driver.ScheduleFlushSend(default).Complete();

            // Set the connection to default
            _connection[0] = default;
            _state = EClientState.Disconnected;

            // Clear data
            _sendData.Clear();
            _eventQueue.Clear();
            _sendQueue.Clear();
            _rtt[0] = -1;

            // Raise disconnect event
            OnLog("Client - Disconnected from the server!");
            OnDisconnect();
        }

        public override void Update() {
            // Complete the job
            _job.Complete();

            // Update the connection state
            _state = ConvertConnectionState(_driver.GetConnectionState(_connection[0]));

            // Do nothing if the connection is not available
            if (!_connection[0].IsCreated) return;

            // Dequeue all events
            while (_eventQueue.TryDequeue(out EventData networkEvent)) {
                switch (networkEvent.Type) {
                    case NetworkEvent.Type.Connect: {
                        // Raise the connect event
                        OnLog("Client - Connected to the server!");
                        OnConnect();
                        break;
                    }
                    case NetworkEvent.Type.Data: {
                        // Reset and resize the message
                        _message.Reset();
                        _message.Resize(networkEvent.Length);

                        // Copy the buffer into the message
                        NativeArray<byte>.Copy(_receiveBuffer, networkEvent.Index, _message.Data, 0, networkEvent.Length);

                        // Try to receive the message
                        try {
                            OnReceiveMessage(_message.AsReader);
                        }
                        catch (Exception exception) {
                            OnLog($"Client - Error trying to receive data! Message: {exception}");
                        }

                        break;
                    }
                    case NetworkEvent.Type.Disconnect: {
                        // Handle the disconnect event
                        _state = EClientState.Disconnected;

                        // Raise the disconnect event
                        OnLog("Client - Disconnected from the server!");
                        OnDisconnect();
                        break;
                    }
                }
            }

            // Clear data and buffers
            _sendData.Clear();
            _sendBuffer.Clear();
            _receiveBuffer.Clear();

            // Process the send data
            while (_sendQueue.Count > 0) _sendQueue.Dequeue()();

            // RTT
            _storedRtt = _rtt[0];
            float currentTime = Time.realtimeSinceStartup;
            SendPingJob sendPingJob = default;
            bool sendPing = false;
            if (_lastPingTime == 0 || currentTime >= _lastPingTime + 1) {
                _lastPingTime = currentTime;
                _sendTimes[_pingId] = currentTime;
                sendPingJob = new SendPingJob {
                    Driver = _driver,
                    Connection = _connection,
                    PingId = _pingId
                };
                _pingId = (_pingId + 1) % 1024;
                sendPing = true;
            }

            // Create jobs
            SendDataJob sendDataJob = new SendDataJob {
                Driver = _driver.ToConcurrent(),
                Connection = _connection,
                ReliablePipeline = _reliablePipeline,
                SendList = _sendData,
                SendBuffer = _sendBuffer
            };
            ProcessJob processJob = new ProcessJob {
                Driver = _driver,
                Connection = _connection,
                EventQueue = _eventQueue,
                ReceiveBuffer = _receiveBuffer,
                CurrentTime = currentTime,
                SendTimes = _sendTimes,
                Rtt = _rtt
            };

            // Schedule jobs
            _job = sendPing ? sendPingJob.Schedule() : default;
            if (_sendData.Length > 0) _job = sendDataJob.ScheduleParallel(_sendData.Length, 1, _job);
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
            _reliablePipeline = default;

            _sendTimes.Dispose();
            _rtt.Dispose();
        }

        public override void SendMessage(uint messageId, WriteMessageDelegate writeMessage, ESendMode sendMode) {
            // Do nothing if not connected
            if (State != EClientState.Connected) {
                OnLog($"Client - Cannot send message {messageId} to the server. Reason: not connected!");
                return;
            }

            _sendQueue.Enqueue(() => {
                // Do nothing if not connected
                if (State != EClientState.Connected) {
                    OnLog($"Client - Cannot send message {messageId} to the server. Reason: not connected!");
                    return;
                }

                // Write the data into the message
                _message.Reset();
                _message.AsWriter.PutByte(0);
                _message.AsWriter.PutUInt(messageId);
                writeMessage(_message.AsWriter);

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