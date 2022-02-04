using System;
using NetworkLayer.Utils;

namespace NetworkLayer {
    [AttributeUsage(AttributeTargets.Method, AllowMultiple = false, Inherited = false)]
    public class ClientMessageReceiverAttribute : Attribute {
        public readonly uint MessageId;
        public readonly uint MessageGroupId;

        public ClientMessageReceiverAttribute(uint messageId, uint messageGroupId) {
            MessageId = messageId;
            MessageGroupId = messageGroupId;
        }
        
        public ClientMessageReceiverAttribute(string messageName, uint messageGroupId) : this(Hash32.Generate(messageName), messageGroupId) { }
        
        public ClientMessageReceiverAttribute(uint messageId, string messageGroupName) : this(messageId, Hash32.Generate(messageGroupName)) { }
        
        public ClientMessageReceiverAttribute(string messageName, string messageGroupName) : this(Hash32.Generate(messageName), Hash32.Generate(messageGroupName)) { }
    }
}