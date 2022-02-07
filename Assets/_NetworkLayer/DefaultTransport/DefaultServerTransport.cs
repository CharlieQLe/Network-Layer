namespace NetworkLayer {
    public class DefaultServerTransport : ServerTransport {
        public DefaultServerTransport(uint messageGroupId) : base(messageGroupId) { }
        public DefaultServerTransport(string messageGroupName) : base(messageGroupName) { }
        public override bool IsRunning { get; }

        public override void Host(ushort port) {
            throw new System.NotImplementedException();
        }

        public override void Close() {
            throw new System.NotImplementedException();
        }

        public override void Disconnect(ulong client) {
            throw new System.NotImplementedException();
        }

        public override void Update() {
            throw new System.NotImplementedException();
        }

        public override void Dispose() {
            throw new System.NotImplementedException();
        }

        public override void SendMessageToAll(uint messageId, WriteMessageDelegate writeMessage, ESendMode sendMode) {
            throw new System.NotImplementedException();
        }

        public override void SendMessageToFilter(FilterClientDelegate filter, uint messageId, WriteMessageDelegate writeMessage, ESendMode sendMode) {
            throw new System.NotImplementedException();
        }

        public override void SendMessageToClient(ulong client, uint messageId, WriteMessageDelegate writeMessage, ESendMode sendMode) {
            throw new System.NotImplementedException();
        }
    }
}