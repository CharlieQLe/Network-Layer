using LiteNetLib;
using LiteNetLib.Utils;

namespace NetworkLayer.Transport.LiteNetLib {
    public delegate void WriteDisconnectDataDelegate(NetDataWriter writer);
    
    public static class LiteNetLibUtility {
        /// <summary>
        /// Convert the connection state to the client state.
        /// </summary>
        /// <param name="state"></param>
        /// <returns></returns>
        public static EClientState ConvertConnectionState(ConnectionState state) => state switch {
            ConnectionState.Connected => EClientState.Connected,
            ConnectionState.Outgoing => EClientState.Connecting,
            _ => EClientState.Disconnected
        };

        /// <summary>
        /// Convert the send mode to the delivery method.
        /// </summary>
        /// <param name="sendMode"></param>
        /// <returns></returns>
        public static DeliveryMethod ConvertSendMode(ESendMode sendMode) => sendMode switch {
            ESendMode.Reliable => DeliveryMethod.ReliableOrdered,
            ESendMode.Sequenced => DeliveryMethod.Sequenced,
            _ => DeliveryMethod.Unreliable
        };
    }
}