using System.Collections.Generic;
using Steamworks;
using UnityEngine;

namespace NetworkLayer.Transport.SteamworksNET {
    public class SteamworksNETServerTransport : NetworkServer.BaseTransport {
        public override bool IsRunning { get; }
        public override int ClientCount { get; }

        public SteamworksNETServerTransport() {
            if (!SteamManager.Initialized) {
                Debug.LogError($"Steam Server - Steam is not initialized!");
                return;
            }
        }
        
        public override int GetRTT(ulong clientId) {
            throw new System.NotImplementedException();
        }

        public override void Host(ushort port) {
            throw new System.NotImplementedException();
        }

        public override void Close() {
            throw new System.NotImplementedException();
        }

        public override void Disconnect(ulong clientId) {
            throw new System.NotImplementedException();
        }

        public override void Send(string messageName, WriteToMessageDelegate writeToMessage, ESendMode sendMode) {
            throw new System.NotImplementedException();
        }

        public override void Send(NetworkServer.SendFilterDelegate filter, string messageName, WriteToMessageDelegate writeToMessage, ESendMode sendMode) {
            throw new System.NotImplementedException();
        }

        public override void Send(ulong clientId, string messageName, WriteToMessageDelegate writeToMessage, ESendMode sendMode) {
            throw new System.NotImplementedException();
        }

        public override void Dispose() {
            throw new System.NotImplementedException();
        }

        public override void PopulateClientIds(List<ulong> clientIds) {
            throw new System.NotImplementedException();
        }

        protected override void Update() {
            throw new System.NotImplementedException();
        }
    }
}