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
        
        public event AttemptConnectionDelegate OnAttemptConnectionEvent;
        public event ConnectDelegate OnConnectEvent;
        public event DisconnectDelegate OnDisconnectEvent;
        public event UpdateDelegate OnUpdateEvent;
        public event LogDelegate OnLogEvent;
        
        private readonly Dictionary<uint, ReceiveMessageDelegate> _receiveMessageCallbacks;
        
        protected ClientTransport(uint messageGroupId) {
            _receiveMessageCallbacks = new Dictionary<uint, ReceiveMessageDelegate>();
            foreach (Assembly assembly in AppDomain.CurrentDomain.GetAssemblies()) {
                foreach (Type type in assembly.GetTypes()) {
                    foreach (MethodInfo methodInfo in type.GetMethods(BindingFlags.Public | BindingFlags.Static | BindingFlags.NonPublic)) {
                        ClientMessageReceiverAttribute attribute = methodInfo.GetCustomAttribute<ClientMessageReceiverAttribute>();
                        if (attribute == null || attribute.MessageGroupId != messageGroupId) continue;
                        Delegate callback = Delegate.CreateDelegate(typeof(ReceiveMessageDelegate), methodInfo, false);
                        _receiveMessageCallbacks[attribute.MessageId] = (ReceiveMessageDelegate) callback ?? throw new Exception($"Method {methodInfo.Name} is not a client message receiver!");
                    }
                }
            }
        }
        
        protected ClientTransport(string messageGroupName) : this(Hash32.Generate(messageGroupName)) { }

        public void SendMessage(string messageName, WriteMessageDelegate writeMessage, ESendMode sendMode) => SendMessage(Hash32.Generate(messageName), writeMessage, sendMode);
        
        protected void OnAttemptConnection() => OnAttemptConnectionEvent?.Invoke();

        protected void OnConnect() => OnConnectEvent?.Invoke();

        protected void OnDisconnect() => OnDisconnectEvent?.Invoke();

        protected void OnUpdate() => OnUpdateEvent?.Invoke();

        protected void OnLog(object message) => OnLogEvent?.Invoke(message);
        
        protected void OnReceiveMessage(Message.Reader reader) {
            uint messageId = reader.ReadUInt();
            if (!_receiveMessageCallbacks.TryGetValue(messageId, out ReceiveMessageDelegate callback)) throw new Exception($"Message id { messageId } has no associated callback!");
            callback(reader);
        }
        
        public abstract EClientState State { get; }
        
        public abstract void Connect(string address, ushort port);

        public abstract void Disconnect();

        public abstract void Update();

        public abstract void Dispose();
        
        public abstract void SendMessage(uint messageId, WriteMessageDelegate writeMessage, ESendMode sendMode);
    }
}