using NetworkLayer.Transport.LiteNetLib;
using NetworkLayer.Transports.UTP;
using NetworkLayer.Utils;

namespace NetworkLayer.Examples {
    public enum ETransportType {
        UnityTransportPackage,
        LiteNetLib
    }
    
    public static class ExampleUtility {
        public static ClientTransport GetClientTransport(ETransportType type, uint messageGroupId) => type switch {
            ETransportType.UnityTransportPackage => new UTPClientTransport(messageGroupId),
            ETransportType.LiteNetLib => new LiteNetLibClientTransport(messageGroupId),
            _ => null
        };
        
        public static ServerTransport GetServerTransport(ETransportType type, uint messageGroupId) => type switch {
            ETransportType.UnityTransportPackage => new UTPServerTransport(messageGroupId),
            ETransportType.LiteNetLib => new LiteNetLibServerTransport(messageGroupId),
            _ => null
        };

        public static ClientTransport GetClientTransport(ETransportType type, string messageGroupName) => GetClientTransport(type, Hash32.Generate(messageGroupName));
        
        public static ServerTransport GetServerTransport(ETransportType type, string messageGroupName) => GetServerTransport(type, Hash32.Generate(messageGroupName));
    }
}