using System.Collections.Generic;

namespace NetworkLayer.Transports.Default {
    public class DefaultServerTransport : NetworkServer.BaseTransport {
        public override bool IsRunning { get; }
        public override int ClientCount { get; }
        public override void Host(ushort port) {
            
        }

        public override void Close() {
            
        }

        public override void Disconnect(ulong clientId) {
            
        }

        public override void Send(string messageName, WriteToMessageDelegate writeToMessage, ESendMode sendMode) {
            
        }

        public override void Send(NetworkServer.SendFilterDelegate filter, string messageName, WriteToMessageDelegate writeToMessage, ESendMode sendMode) {
            
        }

        public override void Send(ulong clientId, string messageName, WriteToMessageDelegate writeToMessage, ESendMode sendMode) {
            
        }

        public override void Dispose() {
            
        }

        public override void PopulateClientIds(List<ulong> clientIds) {
            
        }

        protected override void Update() {
            
        }
    }
}