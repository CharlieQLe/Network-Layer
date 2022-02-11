using System;
using NetworkLayer;
using NetworkLayer.Transport.LiteNetLib;
using NetworkLayer.Transports.UTP;
using UnityEngine;

public enum ETransportType {
    UTP,
    LiteNetLib
}

public class ClientStats : ClientManager {
    [SerializeField] private ETransportType _transportType;

    protected override ClientTransport GetTransport() => _transportType switch {
        ETransportType.UTP => new UTPClientTransport("Stats"),
        ETransportType.LiteNetLib => new LiteNetLibClientTransport("Stats"),
        _ => null
    };

    private void Start() {
        Transport.Connect("127.0.0.1", 7777);
    }

    protected override void OnAttemptConnection() { }

    protected override void OnConnect() { }

    protected override void OnDisconnect() { }

    protected override void OnUpdate() { }

    protected override void OnLog(object message) => Debug.Log(message);
}
