using Steamworks;
using UnityEngine;

namespace NetworkLayer.Transport.SteamworksNET {
    public class SteamworksNETClientTransport : NetworkClient.BaseTransport {
        private HSteamNetConnection _connection;
        
        public override EClientState State { get; }
        public override int Rtt { get; }

        public SteamworksNETClientTransport() {
            if (!SteamManager.Initialized) {
                Debug.LogError($"Steam Client - Steam is not initialized!");
                return;
            }
        }
        
        public override void Connect(string address, ushort port) {
            throw new System.NotImplementedException();
        }

        public override void Disconnect() {
            throw new System.NotImplementedException();
        }

        public override void Send(string messageName, WriteToMessageDelegate writeToMessage, ESendMode sendMode) {
            throw new System.NotImplementedException();
        }

        public override void Dispose() {
            throw new System.NotImplementedException();
        }

        protected override void Update() {
            throw new System.NotImplementedException();
        }
    }
}