using System;
using System.Collections.Generic;
using System.Reflection;
using NetworkLayer.Utils;
using UnityEngine;
using UnityEngine.LowLevel;
using UnityEngine.PlayerLoop;

namespace NetworkLayer {
    public static class NetworkServer {
        public delegate void HostDelegate();

        public delegate void CloseDelegate();
        
        public delegate void ConnectDelegate(ulong clientId);

        public delegate void DisconnectDelegate(ulong clientId);

        public delegate void UpdateDelegate();

        public delegate void ReceiveMessageDelegate(ulong clientId, Message.Reader reader);

        public delegate bool SendFilterDelegate(ulong clientId);
        
        /// <summary>
        /// The base transport all server transports derive from.
        /// </summary>
        public abstract class BaseTransport : IDisposable {
            /// <summary>
            /// Invoke when server hosts.
            /// </summary>
            protected void OnHost() => HostEvent?.Invoke();
            
            /// <summary>
            /// Invoke when server closes.
            /// </summary>
            protected void OnClose() => CloseEvent?.Invoke();
            
            /// <summary>
            /// Invoke when a client connects.
            /// </summary>
            /// <param name="clientId"></param>
            protected void OnConnect(ulong clientId) => ConnectEvent?.Invoke(clientId);
            
            /// <summary>
            /// Invoke when a client disconnects.
            /// </summary>
            /// <param name="clientId"></param>
            protected void OnDisconnect(ulong clientId) => DisconnectEvent?.Invoke(clientId);
            
            /// <summary>
            /// Invoke when updating the client.
            /// </summary>
            protected void OnUpdate() => UpdateEvent?.Invoke();

            /// <summary>
            /// Invoke when a message is received.
            /// </summary>
            /// <param name="clientId"></param>
            /// <param name="reader"></param>
            protected void OnReceiveMessage(ulong clientId, Message.Reader reader) {
                uint messageId = reader.ReadUInt();
                if (_receiveMessageCallbacks.TryGetValue(messageId, out ReceiveMessageDelegate callback)) callback(clientId, reader);
                else Debug.LogError($"NetworkClient - Message with id { messageId } has no associated callback!");
            }
            
            /// <summary>
            /// Returns true if the server is hosting, false otherwise.
            /// </summary>
            public abstract bool IsRunning { get; }
            
            /// <summary>
            /// Get the number of clients connected to the server.
            /// </summary>
            public abstract int ClientCount { get; }

            /// <summary>
            /// Try to host the server.
            /// </summary>
            /// <param name="port"></param>
            public abstract void Host(ushort port);

            /// <summary>
            /// Close the server.
            /// </summary>
            public abstract void Close();
            
            /// <summary>
            /// Disconnect the client from the server.
            /// </summary>
            /// <param name="clientId"></param>
            public abstract void Disconnect(ulong clientId);
            
            /// <summary>
            /// Send data to all clients.
            /// </summary>
            /// <param name="messageName"></param>
            /// <param name="writeToMessage"></param>
            /// <param name="sendMode"></param>
            public abstract void Send(string messageName, WriteToMessageDelegate writeToMessage, ESendMode sendMode);

            /// <summary>
            /// Send data to all clients that satisfy the filter.
            /// </summary>
            /// <param name="filter"></param>
            /// <param name="messageName"></param>
            /// <param name="writeToMessage"></param>
            /// <param name="sendMode"></param>
            public abstract void Send(SendFilterDelegate filter, string messageName, WriteToMessageDelegate writeToMessage, ESendMode sendMode);

            /// <summary>
            /// Send data to a specific client.
            /// </summary>
            /// <param name="clientId"></param>
            /// <param name="messageName"></param>
            /// <param name="writeToMessage"></param>
            /// <param name="sendMode"></param>
            public abstract void Send(ulong clientId, string messageName, WriteToMessageDelegate writeToMessage, ESendMode sendMode);

            /// <summary>
            /// Dispose the client.
            /// </summary>
            public abstract void Dispose();

            /// <summary>
            /// Populate a list with client ids.
            /// </summary>
            /// <param name="clientIds"></param>
            public abstract void PopulateClientIds(List<ulong> clientIds);
            
            /// <summary>
            /// Update the client.
            /// </summary>
            protected internal abstract void Update();
        }

        private static BaseTransport _transport;
        private static Dictionary<uint, ReceiveMessageDelegate> _receiveMessageCallbacks;

        public static event HostDelegate HostEvent;
        public static event CloseDelegate CloseEvent;
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
        /// Initialize the server.
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
                        ServerMessageReceiverAttribute attribute = methodInfo.GetCustomAttribute<ServerMessageReceiverAttribute>();
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
        private static void Quitting() => _transport?.Dispose(); // Dispose the transport

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
                type = typeof(NetworkServer),
                updateDelegate = Update
            }, typeof(FixedUpdate));
        }
    }
}