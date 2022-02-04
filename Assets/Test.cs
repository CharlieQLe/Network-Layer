using NetworkLayer;
using NetworkLayer.Transports.UTP;
using UnityEngine;

public class Test : MonoBehaviour {
    [ServerMessageReceiver("sendInt", "Test")]
    private static void ReceiveInt(ulong client, MessageReader reader) {
        int i = reader.ReadInt();
        _server.SendMessageToClient(client, "sendInt", writer => writer.PutInt(i), ESendMode.Unreliable);
    }

    [ClientMessageReceiver("sendInt", "Test")]
    private static void FinishInt(MessageReader reader) {
        int i = reader.ReadInt();
        Debug.Log($"Client - Received {i}.");
        _client.Disconnect();
    }
    
    private static UTPClientTransport _client;
    private static UTPServerTransport _server;

    private void Awake() {
        _client = new UTPClientTransport("Test");
        _server = new UTPServerTransport("Test");

        _client.OnAttemptConnectionEvent += () => Debug.Log("Client - Connecting to server...");
        _client.OnConnectEvent += () => {
            Debug.Log("Client - Connected to server!");
            _client.SendMessage("sendInt", writer => writer.PutInt(1), ESendMode.Unreliable);
        };
        _client.OnDisconnectEvent += () => Debug.Log("Client - Disconnected from server!");

        _server.OnHostEvent += () => Debug.Log("Server - Started server!");
        _server.OnCloseEvent += () => Debug.Log("Server - Closed server!");
        _server.OnConnectEvent += (client) => Debug.Log($"Server - Client {client} connected to server!");
        _server.OnDisconnectEvent += (client) => Debug.Log($"Server - Client {client} disconnected from server!");
    }

    private void Start() {
        _client.Connect("127.0.0.1", 7777);
        _server.Host(7777);
    }

    private void OnDestroy() {
        _client.Dispose();
        _server.Dispose();
    }

    private void FixedUpdate() {
        _client.Update();
        _server.Update();
    }
}