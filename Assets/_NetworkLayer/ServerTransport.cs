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
        public delegate void WriteMessageDelegate(MessageWriter writer);
        public delegate void LogDelegate(object message);
        private delegate void ReceiveMessageDelegate(ulong client, MessageReader reader);
        
        public event HostDelegate OnHostEvent;
        public event CloseDelegate OnCloseEvent;
        public event ConnectDelegate OnConnectEvent;
        public event DisconnectDelegate OnDisconnectEvent;
        public event LogDelegate OnLogEvent;
        
        private readonly Dictionary<uint, ReceiveMessageDelegate> _receiveMessageCallbacks;

        protected ServerTransport(uint messageGroupId) {
            _receiveMessageCallbacks = new Dictionary<uint, ReceiveMessageDelegate>();
            foreach (Assembly assembly in AppDomain.CurrentDomain.GetAssemblies()) {
                foreach (Type type in assembly.GetTypes()) {
                    foreach (MethodInfo methodInfo in type.GetMethods(BindingFlags.Public | BindingFlags.Static | BindingFlags.NonPublic)) {
                        ServerMessageReceiverAttribute attribute = methodInfo.GetCustomAttribute<ServerMessageReceiverAttribute>();
                        if (attribute == null || attribute.MessageGroupId != messageGroupId) continue;
                        Delegate callback = Delegate.CreateDelegate(typeof(ReceiveMessageDelegate), methodInfo, false);
                        _receiveMessageCallbacks[attribute.MessageId] = (ReceiveMessageDelegate) callback ?? throw new Exception($"Method {methodInfo.Name} is not a server message receiver!");
                    }
                }
            }
        }
        
        protected ServerTransport(string messageGroupName) : this(Hash32.Generate(messageGroupName)) { }

        public void SendMessageToAll(string messageName, WriteMessageDelegate writeMessage, ESendMode sendMode) => SendMessageToAll(Hash32.Generate(messageName), writeMessage, sendMode);
        
        public void SendMessageToClients(IEnumerable<ulong> clients, string messageName, WriteMessageDelegate writeMessage, ESendMode sendMode) => SendMessageToClients(clients, Hash32.Generate(messageName), writeMessage, sendMode);
        
        public void SendMessageToClient(ulong client, string messageName, WriteMessageDelegate writeMessage, ESendMode sendMode) => SendMessageToClient(client, Hash32.Generate(messageName), writeMessage, sendMode);

        protected void OnHost() => OnHostEvent?.Invoke();

        protected void OnClose() => OnCloseEvent?.Invoke();

        protected void OnConnect(ulong client) => OnConnectEvent?.Invoke(client);

        protected void OnDisconnect(ulong client) => OnDisconnectEvent?.Invoke(client);

        protected void OnLog(object message) => OnLogEvent?.Invoke(message);
        
        protected void OnReceiveMessage(ulong client, MessageReader reader) {
            uint messageId = reader.ReadUInt();
            if (!_receiveMessageCallbacks.TryGetValue(messageId, out ReceiveMessageDelegate callback)) throw new Exception($"Message id { messageId } has no associated callback!");
            callback(client, reader);
        }
        
        public abstract bool IsRunning { get; }
        
        public abstract void Host(ushort port);

        public abstract void Close();
        
        public abstract void Disconnect(ulong client);

        public abstract void Update();

        public abstract void Dispose();
        
        public abstract void SendMessageToAll(uint messageId, WriteMessageDelegate writeMessage, ESendMode sendMode);

        public abstract void SendMessageToClients(IEnumerable<ulong> clients, uint messageId, WriteMessageDelegate writeMessage, ESendMode sendMode);

        public abstract void SendMessageToClient(ulong client, uint messageId, WriteMessageDelegate writeMessage, ESendMode sendMode);
    }
}