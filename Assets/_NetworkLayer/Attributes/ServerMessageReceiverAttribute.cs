using System;
using NetworkLayer.Utils;

namespace NetworkLayer {
    [AttributeUsage(AttributeTargets.Method, AllowMultiple = false, Inherited = false)]
    public class ServerMessageReceiverAttribute : Attribute {
        public readonly uint MessageId;
        public readonly uint MessageGroupId;

        public ServerMessageReceiverAttribute(uint messageId, uint messageGroupId) {
            MessageId = messageId;
            MessageGroupId = messageGroupId;
        }
        
        public ServerMessageReceiverAttribute(string messageName, uint messageGroupId) : this(Hash32.Generate(messageName), messageGroupId) { }
        
        public ServerMessageReceiverAttribute(uint messageId, string messageGroupName) : this(messageId, Hash32.Generate(messageGroupName)) { }
        
        public ServerMessageReceiverAttribute(string messageName, string messageGroupName) : this(Hash32.Generate(messageName), Hash32.Generate(messageGroupName)) { }
    }
}