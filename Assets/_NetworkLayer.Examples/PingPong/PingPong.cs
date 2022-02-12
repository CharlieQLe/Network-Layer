using UnityEngine;

namespace NetworkLayer.Examples.PingPong {
    public class PingPong : ExampleNetworkManager {
        [ServerMessageReceiver("Ping", "PingPong")]
        private static void ReceivePing(ulong client, Message.Reader reader) {
            int ping = reader.ReadInt();
            Debug.Log($"Server - Received ping {ping} from client {client}! Sending pong back!");
            Singleton.Server.SendMessageToClient(client, "Pong", writer => writer.PutInt(ping), ESendMode.Unreliable);
        }
        
        [ClientMessageReceiver("Pong", "PingPong")]
        private static void ReceivePong(Message.Reader reader) => Debug.Log($"Client - Received pong {reader.ReadInt()}!");
        
        private void SendPing() {
            int send = Random.Range(0, 32);
            Debug.Log($"Client - Sending ping {send} to server!");
            Client.SendMessage("Ping", writer => writer.PutInt(send), ESendMode.Unreliable);
        }
        
        private void Start() {
            Server.Host(Port);
            Client.Connect(Ip, Port);
        }

        protected override string GetMessageGroupName() => "PingPong";

        protected override void OnClientAttemptConnection() { }

        protected override void OnClientConnect() => SendPing();

        protected override void OnClientDisconnect() { }

        protected override void OnClientUpdate() { }

        protected override void OnServerHost() { }

        protected override void OnServerClose() { }

        protected override void OnServerConnect(ulong clientId) { }

        protected override void OnServerDisconnect(ulong clientId) { }

        protected override void OnServerUpdate() { }
    }
}