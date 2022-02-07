using NetworkLayer;
using NetworkLayer.Transport.LiteNetLib;
using UnityEngine;

public class PingPongClient : ClientManager<PingPongClient> {
    [ClientMessageReceiver("Pong", "PingPong")]
    private static void ReceivePong(Message.Reader reader) {
        Debug.Log($"Client - Received pong {reader.ReadInt()}!");
        Singleton.Transport.Disconnect();
    }

    private void Start() => Transport.Connect("127.0.0.1", 7777);

    protected override ClientTransport GetTransport() => new LiteNetLibClientTransport("PingPong");

    protected override void OnAttemptConnection() { }

    protected override void OnConnect() {
        Debug.Log("Client - Sending ping 1 to server!");
        Transport.SendMessage("Ping", writer => writer.PutInt(1), ESendMode.Unreliable);
    }

    protected override void OnDisconnect() { }

    protected override void OnUpdate() { }

    protected override void OnLog(object message) => Debug.Log(message);
}
