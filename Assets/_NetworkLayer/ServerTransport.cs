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
        private delegate void ReceiveMessageDelegate(ulong client, MessageReader reader);
        
        public event HostDelegate OnHostEvent;
        public event CloseDelegate OnCloseEvent;
        public event ConnectDelegate OnConnectEvent;
        public event DisconnectDelegate OnDisconnectEvent;
        
        private readonly Dictionary<uint, ReceiveMessageDelegate> _receiveMessageCallbacks;
        private readonly MessageWriter _writer;

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
            _writer = new MessageWriter();
        }
        
        protected ServerTransport(string messageGroupName) : this(Hash32.Generate(messageGroupName)) { }

        private void WriteToMessage(uint messageId, WriteMessageDelegate writeMessage) {
            _writer.Reset();
            _writer.PutUInt(messageId);
            writeMessage(_writer);
        }
        
        public void SendMessageToAll(string messageName, WriteMessageDelegate writeMessage, ESendMode sendMode) => SendMessageToAll(Hash32.Generate(messageName), writeMessage, sendMode);
        
        public void SendMessageToClients(IEnumerable<ulong> clients, string messageName, WriteMessageDelegate writeMessage, ESendMode sendMode) => SendMessageToClients(clients, Hash32.Generate(messageName), writeMessage, sendMode);
        
        public void SendMessageToClient(ulong client, string messageName, WriteMessageDelegate writeMessage, ESendMode sendMode) => SendMessageToClient(client, Hash32.Generate(messageName), writeMessage, sendMode);

        public void SendMessageToAll(uint messageId, WriteMessageDelegate writeMessage, ESendMode sendMode) {
            if (!IsRunning) return;
            WriteToMessage(messageId, writeMessage);
            SendToAll(_writer.Data, _writer.Length, sendMode);
        }

        public void SendMessageToClients(IEnumerable<ulong> clients, uint messageId, WriteMessageDelegate writeMessage, ESendMode sendMode) {
            if (!IsRunning) return;
            WriteToMessage(messageId, writeMessage);
            SendToClients(clients, _writer.Data, _writer.Length, sendMode);
        }

        public void SendMessageToClient(ulong client, uint messageId, WriteMessageDelegate writeMessage, ESendMode sendMode) {
            if (!IsRunning) return;
            WriteToMessage(messageId, writeMessage);
            SendToClient(client, _writer.Data, _writer.Length, sendMode);
        }

        protected void OnHost() => OnHostEvent?.Invoke();

        protected void OnClose() => OnCloseEvent?.Invoke();

        protected void OnConnect(ulong client) => OnConnectEvent?.Invoke(client);

        protected void OnDisconnect(ulong client) => OnDisconnectEvent?.Invoke(client);
        
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
        
        protected abstract void SendToAll(byte[] data, int count, ESendMode sendMode);

        protected abstract void SendToClients(IEnumerable<ulong> clients, byte[] data, int count, ESendMode sendMode);
        
        protected abstract void SendToClient(ulong client, byte[] data, int count, ESendMode sendMode);
        
    }
}