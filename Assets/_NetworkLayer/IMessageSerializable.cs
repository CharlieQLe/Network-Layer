namespace NetworkLayer {
    public interface IMessageSerializable {
        /// <summary>
        /// Read data from the message reader.
        /// </summary>
        /// <param name="reader"></param>
        void Read(Message.Reader reader);
        
        /// <summary>
        /// Write data to the message writer.
        /// </summary>
        /// <param name="writer"></param>
        void Write(Message.Writer writer);
    }
}