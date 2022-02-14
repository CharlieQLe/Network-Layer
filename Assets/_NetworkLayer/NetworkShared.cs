namespace NetworkLayer {
    public enum ESendMode : byte {
        Unreliable,
        Sequenced,
        Reliable
    }

    public enum EClientState : byte {
        Disconnected,
        Connecting,
        Connected
    }
    
    public delegate void WriteToMessageDelegate(Message.Writer writer);
}