using System;
using NetworkLayer;
using NetworkLayer.Transport.LiteNetLib;
using NetworkLayer.Transports.UTP;
using UnityEngine;

public class ServerStats : ServerManager {
    [SerializeField] private ETransportType _transportType;
    
    private void Start() {
        Transport.Host(7777);
    }

    protected override ServerTransport GetTransport() => _transportType switch {
        ETransportType.UTP => new UTPServerTransport("Stats"),
        ETransportType.LiteNetLib => new LiteNetLibServerTransport("Stats"),
        _ => null
    };
    
    protected override void OnHost() { }

    protected override void OnClose() { }

    protected override void OnConnect(ulong client) { }

    protected override void OnDisconnect(ulong client) { }

    protected override void OnUpdate() { }

    protected override void OnLog(object message) => Debug.Log(message);
}