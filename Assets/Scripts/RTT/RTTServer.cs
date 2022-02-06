using NetworkLayer;
using NetworkLayer.Transports.UTP;
using UnityEngine;

public class RTTServer : ServerManager<RTTServer> {
    [ServerMessageReceiver("SendTick", "RTT")]
    private static void ReceiveTick(ulong client, Message.Reader reader) {
        int t = reader.ReadInt();
        Singleton.Transport.SendMessageToClient(client, "SendTick", writer => writer.PutInt(t), ESendMode.Unreliable);
    }
    
    protected override ServerTransport GetTransport() => new UTPServerTransport("RTT");

    private void Start() {
        Transport.Host(7777);
    }

    protected override void OnHost() { }

    protected override void OnClose() { }

    protected override void OnConnect(ulong client) { }

    protected override void OnDisconnect(ulong client) { }

    protected override void OnUpdate() { }

    protected override void OnLog(object message) => Debug.Log(message);
}
