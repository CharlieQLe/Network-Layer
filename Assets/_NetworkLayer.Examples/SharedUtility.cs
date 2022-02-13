using NetworkLayer;
using NetworkLayer.Transport.LiteNetLib;
using NetworkLayer.Transports.UTP;

public enum ETransportType {
    UnityTransport,
    LiteNetLib
}

public static class SharedUtility {
    public static NetworkClient.BaseTransport GetClientTransport(ETransportType transportType) => transportType switch {
        ETransportType.UnityTransport => new UTPClientTransport(),
        ETransportType.LiteNetLib => new LiteNetLibClientTransport(),
        _ => null
    };
    
    public static NetworkServer.BaseTransport GetServerTransport(ETransportType transportType) => transportType switch {
        ETransportType.UnityTransport => new UTPServerTransport(),
        ETransportType.LiteNetLib => new LiteNetLibServerTransport(),
        _ => null
    };
}
