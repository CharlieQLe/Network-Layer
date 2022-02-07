using System;
using System.Collections.Generic;
using System.Reflection;
using NetworkLayer.Utils;

namespace NetworkLayer {
    public abstract class ServerTransport : IDisposable {
        public delegate void HostDelegate();
        public delegate void CloseDelegate();
        public delegate void ConnectDelegate(ulong client);
        public delegate void DisconnectDelegate(ulong client);
        public delegate void WriteMessageDelegate(Message.Writer writer);
        public delegate void LogDelegate(object message);
        public delegate void UpdateDelegate();
        public delegate bool FilterClientDelegate(ulong client);
        private delegate void ReceiveMessageDelegate(ulong client, Message.Reader reader);
        
        /// <summary>
        /// Raised when the server starts.
        /// </summary>
        public event HostDelegate OnHostEvent;
        
        /// <summary>
        /// Raised when the server closes.
        /// </summary>
        public event CloseDelegate OnCloseEvent;
        
        /// <summary>
        /// Raised when a client connects.
        /// </summary>
        public event ConnectDelegate OnConnectEvent;

        /// <summary>
        /// Raised when a client disconnects.
        /// </summary>
        public event DisconnectDelegate OnDisconnectEvent;
        
        /// <summary>
        /// Raised when the server updates.
        /// </summary>
        public event UpdateDelegate OnUpdateEvent;
        
        /// <summary>
        /// Raised when the server logs.
        /// </summary>
        public event LogDelegate OnLogEvent;
        
        private readonly Dictionary<uint, ReceiveMessageDelegate> _receiveMessageCallbacks;

        protected ServerTransport(uint messageGroupId) {
            // Create the message callback dictionary
            _receiveMessageCallbacks = new Dictionary<uint, ReceiveMessageDelegate>();
            
            // Iterate through all assemblies
            foreach (Assembly assembly in AppDomain.CurrentDomain.GetAssemblies()) {
                // Iterate through all types
                foreach (Type type in assembly.GetTypes()) {
                    // Iterate through every static method
                    foreach (MethodInfo methodInfo in type.GetMethods(BindingFlags.Public | BindingFlags.Static | BindingFlags.NonPublic)) {
                        // Try to get the attribute
                        ServerMessageReceiverAttribute attribute = methodInfo.GetCustomAttribute<ServerMessageReceiverAttribute>();
                        
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
        
        protected ServerTransport(string messageGroupName) : this(Hash32.Generate(messageGroupName)) { }

        /// <summary>
        /// Send a message to every client.
        /// </summary>
        /// <param name="messageName"></param>
        /// <param name="writeMessage"></param>
        /// <param name="sendMode"></param>
        public void SendMessageToAll(string messageName, WriteMessageDelegate writeMessage, ESendMode sendMode) => SendMessageToAll(Hash32.Generate(messageName), writeMessage, sendMode);
        
        /// <summary>
        /// Send a message to every filtered client.
        /// </summary>
        /// <param name="filter"></param>
        /// <param name="messageName"></param>
        /// <param name="writeMessage"></param>
        /// <param name="sendMode"></param>
        public void SendMessageToFilter(FilterClientDelegate filter, string messageName, WriteMessageDelegate writeMessage, ESendMode sendMode) => SendMessageToFilter(filter, Hash32.Generate(messageName), writeMessage, sendMode);
        
        /// <summary>
        /// Send a message to a specific client.
        /// </summary>
        /// <param name="client"></param>
        /// <param name="messageName"></param>
        /// <param name="writeMessage"></param>
        /// <param name="sendMode"></param>
        public void SendMessageToClient(ulong client, string messageName, WriteMessageDelegate writeMessage, ESendMode sendMode) => SendMessageToClient(client, Hash32.Generate(messageName), writeMessage, sendMode);

        /// <summary>
        /// Raise the host event.
        /// </summary>
        protected void OnHost() => OnHostEvent?.Invoke();

        /// <summary>
        /// Raise the close event.
        /// </summary>
        protected void OnClose() => OnCloseEvent?.Invoke();

        /// <summary>
        /// Raise the connect event.
        /// </summary>
        /// <param name="client"></param>
        protected void OnConnect(ulong client) => OnConnectEvent?.Invoke(client);

        /// <summary>
        /// Raise the disconnect event.
        /// </summary>
        /// <param name="client"></param>
        protected void OnDisconnect(ulong client) => OnDisconnectEvent?.Invoke(client);

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
        /// <param name="client"></param>
        /// <param name="reader"></param>
        /// <exception cref="Exception"></exception>
        protected void OnReceiveMessage(ulong client, Message.Reader reader) {
            // Read the message id
            uint messageId = reader.ReadUInt();
            
            // If no callback exists, throw an exception
            if (!_receiveMessageCallbacks.TryGetValue(messageId, out ReceiveMessageDelegate callback)) throw new Exception($"Message id { messageId } has no associated callback!");
            
            // Invoke the callback
            callback(client, reader);
        }
        
        /// <summary>
        /// Returns true if the server is running, false otherwise.
        /// </summary>
        public abstract bool IsRunning { get; }
        
        /// <summary>
        /// Host a server on the port.
        /// </summary>
        /// <param name="port"></param>
        public abstract void Host(ushort port);

        /// <summary>
        /// Close the server.
        /// </summary>
        public abstract void Close();
        
        /// <summary>
        /// Disconnect the specified client.
        /// </summary>
        /// <param name="client"></param>
        public abstract void Disconnect(ulong client);

        /// <summary>
        /// Update the server.
        /// </summary>
        public abstract void Update();

        /// <summary>
        /// Dispose the server.
        /// </summary>
        public abstract void Dispose();
        
        /// <summary>
        /// Send a message to every client.
        /// </summary>
        /// <param name="messageId"></param>
        /// <param name="writeMessage"></param>
        /// <param name="sendMode"></param>
        public abstract void SendMessageToAll(uint messageId, WriteMessageDelegate writeMessage, ESendMode sendMode);

        /// <summary>
        /// Send a message to every filtered client.
        /// </summary>
        /// <param name="filter"></param>
        /// <param name="messageId"></param>
        /// <param name="writeMessage"></param>
        /// <param name="sendMode"></param>
        public abstract void SendMessageToFilter(FilterClientDelegate filter, uint messageId, WriteMessageDelegate writeMessage, ESendMode sendMode);

        /// <summary>
        /// Send a message to a specific client.
        /// </summary>
        /// <param name="client"></param>
        /// <param name="messageId"></param>
        /// <param name="writeMessage"></param>
        /// <param name="sendMode"></param>
        public abstract void SendMessageToClient(ulong client, uint messageId, WriteMessageDelegate writeMessage, ESendMode sendMode);
    }
}