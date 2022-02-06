using System;
using NetworkLayer;
using NetworkLayer.Transports.UTP;
using UnityEngine;

public class RTTClient : ClientManager<RTTClient> {
    [ClientMessageReceiver("SendTick", "RTT")]
    private static void ReceiveTick(Message.Reader reader) {
        int t = reader.ReadInt();
        float time = Singleton._times[t];
        float currentTime = Time.fixedTime;
        Debug.Log($"RTT is {(currentTime - time) * 1000}ms");
    }
    
    private int _tick;
    private float[] _times;
    protected override ClientTransport GetTransport() => new UTPClientTransport("RTT");

    private void Start() {
        _times = new float[1024];
        Transport.Connect("127.0.0.1", 7777);
    }

    protected override void OnAttemptConnection() { }

    protected override void OnConnect() { }

    protected override void OnDisconnect() { }

    protected override void OnUpdate() {
        int t = _tick % 1024;
        _times[t] = Time.fixedTime;
        _tick++;
        Transport.SendMessage("SendTick", writer => writer.PutInt(t), ESendMode.Unreliable);
    }

    protected override void OnLog(object message) => Debug.Log(message);
}