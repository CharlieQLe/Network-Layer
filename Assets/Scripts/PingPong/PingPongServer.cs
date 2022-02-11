using System;
using System.Collections;
using System.Collections.Generic;
using NetworkLayer;
using NetworkLayer.Transport.LiteNetLib;
using UnityEngine;

public class PingPongServer : ServerManager {
    [ServerMessageReceiver("Ping", "PingPong")]
    private static void ReceivePing(ulong client, Message.Reader reader) {
        int ping = reader.ReadInt();
        Debug.Log($"Server - Received ping {ping} from client {client}! Sending pong back!");
        Singleton.Transport.SendMessageToClient(client, "Pong", writer => writer.PutInt(ping), ESendMode.Unreliable);
    }

    private void Start() => Transport.Host(7777);

    protected override ServerTransport GetTransport() => new LiteNetLibServerTransport("PingPong");

    protected override void OnHost() { }

    protected override void OnClose() { }

    protected override void OnConnect(ulong client) { }

    protected override void OnDisconnect(ulong client) { }

    protected override void OnUpdate() { }

    protected override void OnLog(object message) => Debug.Log(message);
}