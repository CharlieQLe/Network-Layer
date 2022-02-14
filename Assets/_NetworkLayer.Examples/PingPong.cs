using System;
using NetworkLayer;
using NetworkLayer.Transports.UTP;
using UnityEngine;
using Random = UnityEngine.Random;

#if UNITY_EDITOR

using UnityEditor;

[CustomEditor(typeof(PingPong))]
public class PingPongInspector : Editor {
    private PingPong _pingPong;

    private void OnEnable() {
        _pingPong = (PingPong) target;
    }

    public override void OnInspectorGUI() {
        base.OnInspectorGUI();

        if (Application.isPlaying && GUILayout.Button("Send Ping")) {
            int num = _pingPong.NumToSend;
            NetworkClient.Transport.Send("Ping", writer => writer.PutInt(num), ESendMode.Unreliable);
        }
    }
}

#endif

public class PingPong : MonoBehaviour {
    [ClientMessageReceiver("Pong", "PingPong")]
    private static void ReceivePong(Message.Reader reader) {
        int num = reader.ReadInt();
        Debug.Log($"Received pong { num }!");
    }

    [ServerMessageReceiver("Ping", "PingPong")]
    private static void ReceivePing(ulong clientId, Message.Reader reader) {
        int num = reader.ReadInt();
        Debug.Log($"Received pong { num } from client { clientId }!");
        NetworkServer.Transport.Send(clientId, "Pong", writer => writer.PutInt(num), ESendMode.Unreliable);
    }

    [SerializeField] private ETransportType transportType;
    [SerializeField] private string ipAddress = "127.0.0.1";
    [SerializeField] private ushort port = 7777;
    [SerializeField] private int numToSend;

    public int NumToSend => numToSend;
    
    private void Start() {
        NetworkClient.Initialize(SharedUtility.GetClientTransport(transportType), "PingPong");
        NetworkServer.Initialize(SharedUtility.GetServerTransport(transportType), "PingPong");
        NetworkClient.Transport.Connect(ipAddress, port);
        NetworkServer.Transport.Host(port);
    }
}
