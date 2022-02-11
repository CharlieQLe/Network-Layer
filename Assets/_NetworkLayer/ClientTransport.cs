using System;
using System.Collections.Generic;
using System.Reflection;
using NetworkLayer.Utils;

namespace NetworkLayer {
    public enum ESendMode : byte {
        Unreliable,
        Reliable
    }

    public enum EClientState : byte {
        Disconnected,
        Connecting,
        Connected
    }
    
    public abstract class ClientTransport : IDisposable {
        public delegate void AttemptConnectionDelegate();
        public delegate void ConnectDelegate();
        public delegate void DisconnectDelegate();
        public delegate void LogDelegate(object message);
        public delegate void UpdateDelegate();
        public delegate void WriteMessageDelegate(Message.Writer writer);
        private delegate void ReceiveMessageDelegate(Message.Reader reader);
        
        /// <summary>
        /// Raised when the client attempts a connection.
        /// </summary>
        public event AttemptConnectionDelegate OnAttemptConnectionEvent;
        
        /// <summary>
        /// Raised when the client connects to a server.
        /// </summary>
        public event ConnectDelegate OnConnectEvent;
        
        /// <summary>
        /// Raised when the client disconnects from the server.
        /// </summary>
        public event DisconnectDelegate OnDisconnectEvent;
        
        /// <summary>
        /// Raised when the client updates.
        /// </summary>
        public event UpdateDelegate OnUpdateEvent;
        
        /// <summary>
        /// Raised when the client logs.
        /// </summary>
        public event LogDelegate OnLogEvent;
        
        private readonly Dictionary<uint, ReceiveMessageDelegate> _receiveMessageCallbacks;
        
        protected ClientTransport(uint messageGroupId) {
            // Create the message callback dictionary
            _receiveMessageCallbacks = new Dictionary<uint, ReceiveMessageDelegate>();
            
            // Iterate through all assemblies
            foreach (Assembly assembly in AppDomain.CurrentDomain.GetAssemblies()) {
                // Iterate through all types
                foreach (Type type in assembly.GetTypes()) {
                    // Iterate through every static method
                    foreach (MethodInfo methodInfo in type.GetMethods(BindingFlags.Public | BindingFlags.Static | BindingFlags.NonPublic)) {
                        // Try to get the attribute
                        ClientMessageReceiverAttribute attribute = methodInfo.GetCustomAttribute<ClientMessageReceiverAttribute>();
                        
                        // Skip if the attribute is null or if the message group does not match
                        if (attribute == null || attribute.MessageGroupId != messageGroupId) continue;
                        
                        // Create the delegate
                        Delegate callback = Delegate.CreateDelegate(typeof(ReceiveMessageDelegate), methodInfo, false);
                        
                        // Try to set the callback
                        _receiveMessageCallbacks[attribute.MessageId] = (ReceiveMessageDelegate) callback ?? throw new Exception($"Method {methodInfo.Name} is not a client message receiver!");
                    }
                }
            }
        }
        
        protected ClientTransport(string messageGroupName) : this(Hash32.Generate(messageGroupName)) { }

        /// <summary>
        /// Send a message to the server.
        /// </summary>
        /// <param name="messageName"></param>
        /// <param name="writeMessage"></param>
        /// <param name="sendMode"></param>
        public void SendMessage(string messageName, WriteMessageDelegate writeMessage, ESendMode sendMode) => SendMessage(Hash32.Generate(messageName), writeMessage, sendMode);
        
        /// <summary>
        /// Raise the attempt connection event.
        /// </summary>
        protected void OnAttemptConnection() => OnAttemptConnectionEvent?.Invoke();

        /// <summary>
        /// Raise the connect event.
        /// </summary>
        protected void OnConnect() => OnConnectEvent?.Invoke();

        /// <summary>
        /// Raise the disconnect event.
        /// </summary>
        protected void OnDisconnect() => OnDisconnectEvent?.Invoke();

        /// <summary>
        /// Raise the update event.
        /// </summary>
        protected void OnUpdate() => OnUpdateEvent?.Invoke();

        /// <summary>
        /// Raise the log event.
        /// </summary>
        /// <param name="message"></param>
        protected void OnLog(object message) => OnLogEvent?.Invoke(message);
        
        /// <summary>
        /// Receive a message in the reader.
        /// </summary>
        /// <param name="reader"></param>
        /// <exception cref="Exception"></exception>
        protected void OnReceiveMessage(Message.Reader reader) {
            // Read the message id
            uint messageId = reader.ReadUInt();
            
            // If no callback exists, throw an exception
            if (!_receiveMessageCallbacks.TryGetValue(messageId, out ReceiveMessageDelegate callback)) throw new Exception($"Message id { messageId } has no associated callback!");
            
            // Invoke the callback
            callback(reader);
        }
        
        /// <summary>
        /// Get the state of the client.
        /// </summary>
        public abstract EClientState State { get; }
        
        /// <summary>
        /// Get the round trip time.
        /// </summary>
        public abstract int Rtt { get; }
        
        /// <summary>
        /// Connect to the server.
        /// </summary>
        /// <param name="address"></param>
        /// <param name="port"></param>
        public abstract void Connect(string address, ushort port);

        /// <summary>
        /// Disconnect from the server.
        /// </summary>
        public abstract void Disconnect();

        /// <summary>
        /// Update the client.
        /// </summary>
        public abstract void Update();

        /// <summary>
        /// Dispose the client.
        /// </summary>
        public abstract void Dispose();
        
        /// <summary>
        /// Send a message to the server
        /// </summary>
        /// <param name="messageId"></param>
        /// <param name="writeMessage"></param>
        /// <param name="sendMode"></param>
        public abstract void SendMessage(uint messageId, WriteMessageDelegate writeMessage, ESendMode sendMode);
    }
}