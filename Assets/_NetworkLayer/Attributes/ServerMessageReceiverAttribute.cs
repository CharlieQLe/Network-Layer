using System;
using NetworkLayer.Utils;

namespace NetworkLayer {
    [AttributeUsage(AttributeTargets.Method, AllowMultiple = false, Inherited = false)]
    public class ServerMessageReceiverAttribute : Attribute {
        public readonly string MessageName;
        public readonly string MessageGroup;

        private uint _messageId;
        private bool _hashed;

        public uint MessageId => _hashed ? _messageId : _messageId = Hash32.Generate(MessageName);

        public ServerMessageReceiverAttribute(string messageName, string messageGroup) {
            MessageName = messageName;
            MessageGroup = messageGroup;
        }
    }
}