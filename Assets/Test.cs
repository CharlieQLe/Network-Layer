using NetworkLayer;
using NetworkLayer.Transports.UTP;
using UnityEngine;

public class Test : MonoBehaviour {
    [ServerMessageReceiver("sendTime", "Test")]
    private static void ReceiveTime(ulong client, Message.Reader reader) {
        float t = reader.ReadFloat();
        _server.SendMessageToClient(client, "sendTime", writer => writer.PutFloat(t), ESendMode.Unreliable);
    }

    [ClientMessageReceiver("sendTime", "Test")]
    private static void CalculateRTT(Message.Reader reader) {
        float t = reader.ReadFloat();
        Debug.Log($"Client - RTT is {(Time.fixedTime - t) * 1000}ms");
    }
    
    private static UTPClientTransport _client;
    private static UTPServerTransport _server;

    private void Awake() {
        _client = new UTPClientTransport("Test");
        _server = new UTPServerTransport("Test");

        _client.OnLogEvent += Debug.Log;
        _server.OnLogEvent += Debug.Log;
    }

    private void Start() {
        _server.Host(7777);
        _client.Connect("127.0.0.1", 7777);
    }

    private void OnDestroy() {
        _server.Dispose();
        _client.Dispose();
    }

    private void FixedUpdate() {
        _server.Update();
        float t = Time.fixedTime;
        _client.SendMessage("sendTime", writer => writer.PutFloat(t), ESendMode.Unreliable);
        _client.Update();
    }
}