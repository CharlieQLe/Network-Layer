using Unity.Networking.Transport;

namespace NetworkLayer.Transports.UTP {
    
    public delegate void SendDelegate();
    
    public static class UTPUtility {
        public const byte HEADER_MESSAGE = 0;
        public const byte HEADER_CLIENT_RTT = 1;
        public const byte HEADER_SERVER_RTT = 2;
        
        /// <summary>
        /// Convert the network connection state to the client state.
        /// </summary>
        /// <param name="state"></param>
        /// <returns></returns>
        public static EClientState ConvertConnectionState(NetworkConnection.State state) => state switch {
            NetworkConnection.State.Connected => EClientState.Connected,
            NetworkConnection.State.Connecting => EClientState.Connecting,
            _ => EClientState.Disconnected
        };
    }
}