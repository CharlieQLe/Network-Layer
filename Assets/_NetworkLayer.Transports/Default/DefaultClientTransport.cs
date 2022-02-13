using System;
using System.Net;
using System.Net.Sockets;

namespace NetworkLayer.Transports.Default {
    public class DefaultClientTransport : NetworkClient.BaseTransport {
        public override EClientState State { get; }

        public override int Rtt { get; }
        
        public override void Connect(string address, ushort port) {
            
        }

        public override void Disconnect() {
            
        }

        public override void Send(string messageName, WriteToMessageDelegate writeToMessage, ESendMode sendMode) {
            
        }

        public override void Dispose() {
            
        }

        protected override void Update() {
            
        }
    }
}