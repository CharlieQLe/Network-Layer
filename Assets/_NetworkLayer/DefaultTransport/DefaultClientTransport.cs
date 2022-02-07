namespace NetworkLayer {
    public class DefaultClientTransport : ClientTransport {
        public DefaultClientTransport(uint messageGroupId) : base(messageGroupId) { }
        public DefaultClientTransport(string messageGroupName) : base(messageGroupName) { }
        public override EClientState State { get; }

        public override void Connect(string address, ushort port) {
            throw new System.NotImplementedException();
        }

        public override void Disconnect() {
            throw new System.NotImplementedException();
        }

        public override void Update() {
            throw new System.NotImplementedException();
        }

        public override void Dispose() {
            throw new System.NotImplementedException();
        }

        public override void SendMessage(uint messageId, WriteMessageDelegate writeMessage, ESendMode sendMode) {
            throw new System.NotImplementedException();
        }
    }
}