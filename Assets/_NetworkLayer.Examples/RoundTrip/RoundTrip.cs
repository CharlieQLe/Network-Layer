using UnityEngine;

namespace NetworkLayer.Examples.RoundTrip {
    public class RoundTrip : ExampleNetworkManager {
        [ServerMessageReceiver("SendTime", "RoundTrip")]
        private static void ReceiveTime(ulong client, Message.Reader reader) {
            int t = reader.ReadInt();
            Singleton.Server.SendMessageToClient(client, "SendTime", writer => writer.PutInt(t), ESendMode.Unreliable);
        }

        [ClientMessageReceiver("SendTime", "RoundTrip")]
        private static void ReceiveTime(Message.Reader reader) {
            float currentTime = Time.realtimeSinceStartup;
            int t = reader.ReadInt();
            if (!(Singleton is RoundTrip trip)) return;
            float rtt = (currentTime - trip._times[t]) * 1000;
            Debug.Log($"Round Trip Client - RTT is {rtt}ms");
        }
        
        private int _nextId;
        private float[] _times;
        
        private void Start() {
            _times = new float[1024];
            _nextId = 0;
            
            Server.Host(Port);
            Client.Connect(Ip, Port);
        }

        protected override string GetMessageGroupName() => "RoundTrip";

        protected override void OnClientAttemptConnection() { }

        protected override void OnClientConnect() { }

        protected override void OnClientDisconnect() { }

        protected override void OnClientUpdate() {
            float time = Time.realtimeSinceStartup;
            _times[_nextId] = time;
            Client.SendMessage("SendTime", writer => writer.PutInt(_nextId), ESendMode.Unreliable);
            _nextId = (_nextId + 1) % 1024;
        }

        protected override void OnServerHost() { }

        protected override void OnServerClose() { }

        protected override void OnServerConnect(ulong clientId) { }

        protected override void OnServerDisconnect(ulong clientId) { }

        protected override void OnServerUpdate() { }
    }
}

