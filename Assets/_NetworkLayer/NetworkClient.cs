using System;
using System.Collections.Generic;
using System.Reflection;
using NetworkLayer.Utils;
using UnityEngine;
using UnityEngine.LowLevel;
using UnityEngine.PlayerLoop;

namespace NetworkLayer {
    public static class NetworkClient {
        public delegate void StartConnectingDelegate();

        public delegate void ConnectDelegate();

        public delegate void DisconnectDelegate();

        public delegate void UpdateDelegate();

        public delegate void ReceiveMessageDelegate(Message.Reader reader);

        /// <summary>
        /// The base transport all client transports derive from.
        /// </summary>
        public abstract class BaseTransport : IDisposable {
            /// <summary>
            /// Invoke when starting to connect.
            /// </summary>
            protected static void OnStartConnecting() => StartConnectingEvent?.Invoke();
            
            /// <summary>
            /// Invoke when successfully connected to the server.
            /// </summary>
            protected static void OnConnect() => ConnectEvent?.Invoke();
            
            /// <summary>
            /// Invoke when disconnect from the server or failed to connect.
            /// </summary>
            protected static void OnDisconnect() => DisconnectEvent?.Invoke();
            
            /// <summary>
            /// Invoke when updating the client.
            /// </summary>
            protected static void OnUpdate() => UpdateEvent?.Invoke();

            /// <summary>
            /// Invoke when a message is received.
            /// </summary>
            /// <param name="reader"></param>
            protected void OnReceiveMessage(Message.Reader reader) {
                uint messageId = reader.ReadUInt();
                if (_receiveMessageCallbacks.TryGetValue(messageId, out ReceiveMessageDelegate callback)) callback(reader);
                else Debug.LogError($"NetworkClient - Message with id { messageId } has no associated callback!");
            }
            
            /// <summary>
            /// Get the state of the client.
            /// </summary>
            public abstract EClientState State { get; }
            
            /// <summary>
            /// Get the round trip time from the client to the server.
            /// </summary>
            public abstract int Rtt { get; }
            
            /// <summary>
            /// Try to connect to the server.
            /// </summary>
            /// <param name="address"></param>
            /// <param name="port"></param>
            public abstract void Connect(string address, ushort port);
            
            /// <summary>
            /// Disconnect from the server.
            /// </summary>
            public abstract void Disconnect();
            
            /// <summary>
            /// Send data to the server.
            /// </summary>
            /// <param name="messageName"></param>
            /// <param name="writeToMessage"></param>
            /// <param name="sendMode"></param>
            public abstract void Send(string messageName, WriteToMessageDelegate writeToMessage, ESendMode sendMode);
            
            /// <summary>
            /// Dispose the client.
            /// </summary>
            public abstract void Dispose();
            
            /// <summary>
            /// Update the client.
            /// </summary>
            protected internal abstract void Update();
        }

        private static BaseTransport _transport;
        private static Dictionary<uint, ReceiveMessageDelegate> _receiveMessageCallbacks = new Dictionary<uint, ReceiveMessageDelegate>();

        public static event StartConnectingDelegate StartConnectingEvent;
        public static event ConnectDelegate ConnectEvent;
        public static event DisconnectDelegate DisconnectEvent;
        public static event UpdateDelegate UpdateEvent;

        /// <summary>
        /// Get the transport as a base.
        /// </summary>
        public static BaseTransport Transport => _transport;
        
        /// <summary>
        /// Get the transport as a specific type.
        /// </summary>
        /// <typeparam name="T"></typeparam>
        /// <returns></returns>
        public static T GetTransport<T>() where T : BaseTransport => _transport as T;
        
        /// <summary>
        /// Initialize the client.
        /// </summary>
        /// <param name="transport"></param>
        /// <param name="messageGroup"></param>
        public static void Initialize(BaseTransport transport, string messageGroup) {
            // Dispose current transport
            _transport?.Dispose();
            
            // Set transport
            _transport = transport;
            
            // Clear callbacks
            _receiveMessageCallbacks.Clear();
            
            // Iterate through all methods in every loaded assembly
            foreach (Assembly assembly in AppDomain.CurrentDomain.GetAssemblies()) {
                foreach (Type type in assembly.GetTypes()) {
                    foreach (MethodInfo methodInfo in type.GetMethods(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static)) {
                        // Check if the method has the attribute with a matching message group
                        ClientMessageReceiverAttribute attribute = methodInfo.GetCustomAttribute<ClientMessageReceiverAttribute>();
                        if (attribute == null || attribute.MessageGroup != messageGroup) continue;
                        
                        // Try to create the callback
                        Delegate callback = Delegate.CreateDelegate(typeof(ReceiveMessageDelegate), methodInfo, false);
                        if (callback == null) Debug.LogError($"NetworkClient - Error registering method {methodInfo.Name} as message callback- fields do not match!");
                        else _receiveMessageCallbacks[attribute.MessageId] = (ReceiveMessageDelegate) callback;
                    }
                }
            }
        }

        /// <summary>
        /// Handles all updates.
        /// </summary>
        private static void Update() => _transport?.Update(); // Update the transport

        /// <summary>
        /// Handles all quitting actions.
        /// </summary>
        private static void Quitting() {
            // Dispose the transport
            _transport?.Dispose();
            
            // Reset the player loop
            PlayerLoop.SetPlayerLoop(PlayerLoop.GetDefaultPlayerLoop());
        }

        /// <summary>
        /// Do all initial setup.
        /// </summary>
        [RuntimeInitializeOnLoadMethod]
        private static void OnLoad() => Application.quitting += Quitting; // Subscribe the quitting callback to the quitting event
            
        /// <summary>
        /// Update the player loop during the subsystem registration phase.
        /// </summary>
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void UpdatePlayerLoop() {
            // Insert the subsystem at the front of the FixedUpdate subsystem.
            SubsystemUtility.InsertAtFront(new PlayerLoopSystem {
                type = typeof(NetworkClient),
                updateDelegate = Update
            }, typeof(FixedUpdate));
        }
    }
}