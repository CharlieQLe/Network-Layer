using System;
using System.Collections.Generic;
using NetworkLayer.Utils;
using Unity.Burst;
using Unity.Collections;
using Unity.Jobs;
using Unity.Networking.Transport;
using UnityEngine;

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
            public NetworkConnection Connection;
            public NativeQueue<ProcessedEvent> EventQueue;
            public NativeList<byte> ReceiveBuffer;

            public void Execute() {
                int index = 0;
                NetworkEvent.Type type;
                while ((type = Driver.PopEventForConnection(Connection, out DataStreamReader reader)) != NetworkEvent.Type.Empty) {
                    if (type == NetworkEvent.Type.Data) {
                        NativeArray<byte> stream = new NativeArray<byte>(reader.Length, Allocator.Temp, NativeArrayOptions.UninitializedMemory);
                        reader.ReadBytes(stream);
                        ProcessedEvent processedEvent = new ProcessedEvent(type, index, stream.Length);
                        if (ReceiveBuffer.Length < index + reader.Length) ReceiveBuffer.Resize(index + reader.Length, NativeArrayOptions.UninitializedMemory);
                        NativeArray<byte>.Copy(stream, 0, ReceiveBuffer, processedEvent.Index, processedEvent.Length);
                        index += processedEvent.Length;
                        EventQueue.Enqueue(processedEvent);
                    } else {
                        EventQueue.Enqueue(new ProcessedEvent(type, 0, 0));
                    }
                }
            }
        }

        [BurstCompile]
        private struct SendDataJob : IJobFor {
            public NetworkDriver.Concurrent Driver;
            public NetworkConnection Connection;
            public NetworkPipeline UnreliablePipeline;
            public NetworkPipeline ReliablePipeline;
            [ReadOnly] public NativeList<PendingSend> PendingSends;
            [ReadOnly, NativeDisableParallelForRestriction]
            public NativeArray<byte> SendBuffer;

            public void Execute(int index) {
                PendingSend pendingSend = PendingSends[index];
                if (0 != Driver.BeginSend(pendingSend.SendMode == ESendMode.Reliable ? ReliablePipeline : UnreliablePipeline, Connection, out DataStreamWriter writer)) return;
                writer.WriteBytes(SendBuffer.GetSubArray(pendingSend.Index, pendingSend.Length));
                Driver.EndSend(writer);
            }
        }

        private delegate void MainThreadDelegate();

        private NativeList<byte> _sendBuffer;
        private NativeList<byte> _receiveBuffer;
        private NativeList<PendingSend> _pendingSends;
        private NativeQueue<ProcessedEvent> _eventQueue;
        private NetworkDriver _driver;
        private NetworkPipeline _unreliablePipeline;
        private NetworkPipeline _reliablePipeline;
        private NetworkConnection _connection;
        private JobHandle _job;

        private int _sendBufferIndex;
        private EClientState _state;
        private readonly MessageReader _reader;
        private readonly Queue<MainThreadDelegate> _mainThreadCallbacks;

        public UTPClientTransport(uint messageGroupId) : base(messageGroupId) {
            _driver = NetworkDriver.Create();
            _unreliablePipeline = _driver.CreatePipeline(typeof(UnreliableSequencedPipelineStage));
            _reliablePipeline = _driver.CreatePipeline(typeof(ReliableSequencedPipelineStage));
            _sendBuffer = new NativeList<byte>(1024, Allocator.Persistent);
            _receiveBuffer = new NativeList<byte>(1024, Allocator.Persistent);
            _pendingSends = new NativeList<PendingSend>(1, Allocator.Persistent);
            _eventQueue = new NativeQueue<ProcessedEvent>(Allocator.Persistent);
            _reader = new MessageReader();
            _mainThreadCallbacks = new Queue<MainThreadDelegate>();
        }

        public UTPClientTransport(string messageGroupName) : this(Hash32.Generate(messageGroupName)) { }

        public override EClientState State => _state;

        public override void Connect(string address, ushort port) {
            if (_state != EClientState.Disconnected || !NetworkEndPoint.TryParse(address, port, out NetworkEndPoint endpoint)) return;
            _connection = _driver.Connect(endpoint);
            _state = EClientState.Connecting;
            OnAttemptConnection();
        }

        public override void Disconnect() {
            if (_state == EClientState.Disconnected) return;
            _job.Complete();
            _driver.Disconnect(_connection);
            _driver.ScheduleFlushSend(default).Complete();
            _connection = default;
            _state = EClientState.Disconnected;
            _pendingSends.Clear();
            _eventQueue.Clear();
            _mainThreadCallbacks.Clear();
            OnDisconnect();
        }

        public override void Update() {
            _job.Complete();
            if (_state == EClientState.Disconnected) return;
            while (_eventQueue.TryDequeue(out ProcessedEvent networkEvent)) {
                switch (networkEvent.Type) {
                    case NetworkEvent.Type.Connect: {
                        _state = EClientState.Connected;
                        OnConnect();
                        break;
                    }
                    case NetworkEvent.Type.Data: {
                        _reader.Reset();
                        _reader.Resize(networkEvent.Length);
                        NativeArray<byte>.Copy(_receiveBuffer, networkEvent.Index, _reader.Data, 0, networkEvent.Length);
                        OnReceiveMessage(_reader);
                        break;
                    }
                    case NetworkEvent.Type.Disconnect: {
                        _connection = default;
                        _state = EClientState.Disconnected;
                        OnDisconnect();
                        break;
                    }
                }
            }
            _job = default;
            _sendBufferIndex = 0;
            while (_mainThreadCallbacks.Count > 0) _mainThreadCallbacks.Dequeue()();
            SendDataJob sendDataJob = new SendDataJob {
                Driver = _driver.ToConcurrent(),
                Connection = _connection,
                UnreliablePipeline = _unreliablePipeline,
                ReliablePipeline = _reliablePipeline,
                PendingSends = _pendingSends,
                SendBuffer = _sendBuffer.AsDeferredJobArray()
            };
            ProcessJob processJob = new ProcessJob {
                Driver = _driver,
                Connection = _connection,
                EventQueue = _eventQueue,
                ReceiveBuffer = _receiveBuffer
            };
            _job = processJob.Schedule(_driver.ScheduleUpdate(_pendingSends.Length > 0 ? sendDataJob.ScheduleParallel(_pendingSends.Length, 1, default) : default));
        }

        public override void Dispose() {
            Disconnect();
            _driver.Dispose();
            _sendBuffer.Dispose();
            _pendingSends.Dispose();
            _eventQueue.Dispose();
            _unreliablePipeline = default;
            _reliablePipeline = default;
        }

        protected override void Send(byte[] data, int count, ESendMode sendMode) {
            _mainThreadCallbacks.Enqueue(() => {
                
                // todo: fix send
                
                //if (_sendBuffer.Capacity < _sendBufferIndex + count) _sendBuffer.Resize(_sendBuffer.Capacity * 2, NativeArrayOptions.UninitializedMemory);
                //NativeArray<byte>.Copy(data, 0, _sendBuffer, _sendBufferIndex, count);
                //_pendingSends.Add(new PendingSend(_sendBufferIndex, count, sendMode));
                //_sendBufferIndex += count;
            });
        }
    }
}