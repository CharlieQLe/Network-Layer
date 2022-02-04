using System;
using NetworkLayer.Utils;

namespace NetworkLayer {
    public sealed class MessageReader {
        private byte[] _data;
        private int _position;

        public byte[] Data => _data;
        public int Length => _position;
        
        public MessageReader() {
            _data = new byte[1024];
            _position = 0;
        }

        public void Reset() => _position = 0;

        public void Resize(int newSize) {
            if (newSize >= _data.Length) return;
            byte[] newData = new byte[newSize];
            Buffer.BlockCopy(_data, 0, newData, 0, _position);
            _data = newData;
        }
        
        public byte ReadByte() => ByteConverter.GetByte(_data, _position, out _position);
        public short ReadShort() => UnsafeByteConverter.GetShort(_data, _position, out _position);
        public ushort ReadUShort() => UnsafeByteConverter.GetUShort(_data, _position, out _position);
        public int ReadInt() => UnsafeByteConverter.GetInt(_data, _position, out _position);
        public uint ReadUInt() => UnsafeByteConverter.GetUInt(_data, _position, out _position);
        public long ReadLong() => UnsafeByteConverter.GetLong(_data, _position, out _position);
        public ulong ReadULong() => UnsafeByteConverter.GetULong(_data, _position, out _position);
        public bool ReadBool() => ByteConverter.GetBool(_data, _position, out _position);
        public char ReadChar() => UnsafeByteConverter.GetChar(_data, _position, out _position);
        public float ReadFloat() => UnsafeByteConverter.GetFloat(_data, _position, out _position);
        public double ReadDouble() => UnsafeByteConverter.GetDouble(_data, _position, out _position);
        public string ReadString() => UnsafeByteConverter.GetString(_data, _position, out _position);
    }
}