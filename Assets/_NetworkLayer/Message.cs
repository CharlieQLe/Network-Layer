using System;
using NetworkLayer.Utils;

namespace NetworkLayer {
    public sealed class Message {
        public sealed class Reader {
            private readonly Message _message;

            internal Reader(Message message) {
                _message = message;
            }

            public byte ReadByte() => ByteConverter.GetByte(_message._data, _message._position, out _message._position);
            public short ReadShort() => UnsafeByteConverter.GetShort(_message._data, _message._position, out _message._position);
            public ushort ReadUShort() => UnsafeByteConverter.GetUShort(_message._data, _message._position, out _message._position);
            public int ReadInt() => UnsafeByteConverter.GetInt(_message._data, _message._position, out _message._position);
            public uint ReadUInt() => UnsafeByteConverter.GetUInt(_message._data, _message._position, out _message._position);
            public long ReadLong() => UnsafeByteConverter.GetLong(_message._data, _message._position, out _message._position);
            public ulong ReadULong() => UnsafeByteConverter.GetULong(_message._data, _message._position, out _message._position);
            public bool ReadBool() => ByteConverter.GetBool(_message._data, _message._position, out _message._position);
            public char ReadChar() => UnsafeByteConverter.GetChar(_message._data, _message._position, out _message._position);
            public float ReadFloat() => UnsafeByteConverter.GetFloat(_message._data, _message._position, out _message._position);
            public double ReadDouble() => UnsafeByteConverter.GetDouble(_message._data, _message._position, out _message._position);
            public string ReadString() => UnsafeByteConverter.GetString(_message._data, _message._position, out _message._position);
        }

        public sealed class Writer {
            private readonly Message _message;

            internal Writer(Message message) {
                _message = message;
            }

            public void PutByte(byte value) {
                if (_message._position >= _message._data.Length) _message.Resize(_message._data.Length * 2);
                _message._position = ByteConverter.PutByte(_message._data, _message._position, value);
            }

            public void PutShort(short value) {
                if (_message._position + 1 >= _message._data.Length) _message.Resize(_message._data.Length * 2);
                _message._position = UnsafeByteConverter.PutShort(_message._data, _message._position, value);
            }

            public void PutUShort(ushort value) {
                if (_message._position + 1 >= _message._data.Length) _message.Resize(_message._data.Length * 2);
                _message._position = UnsafeByteConverter.PutUShort(_message._data, _message._position, value);
            }

            public void PutInt(int value) {
                if (_message._position + 3 >= _message._data.Length) _message.Resize(_message._data.Length * 2);
                _message._position = UnsafeByteConverter.PutInt(_message._data, _message._position, value);
            }

            public void PutUInt(uint value) {
                if (_message._position + 3 >= _message._data.Length) _message.Resize(_message._data.Length * 2);
                _message._position = UnsafeByteConverter.PutUInt(_message._data, _message._position, value);
            }

            public void PutLong(long value) {
                if (_message._position + 7 >= _message._data.Length) _message.Resize(_message._data.Length * 2);
                _message._position = UnsafeByteConverter.PutLong(_message._data, _message._position, value);
            }

            public void PutULong(ulong value) {
                if (_message._position + 7 >= _message._data.Length) _message.Resize(_message._data.Length * 2);
                _message._position = UnsafeByteConverter.PutULong(_message._data, _message._position, value);
            }

            public void PutBool(bool value) {
                if (_message._position >= _message._data.Length) _message.Resize(_message._data.Length * 2);
                _message._position = ByteConverter.PutBool(_message._data, _message._position, value);
            }

            public void PutChar(char value) {
                if (_message._position + 1 >= _message._data.Length) _message.Resize(_message._data.Length * 2);
                _message._position = UnsafeByteConverter.PutChar(_message._data, _message._position, value);
            }

            public void PutFloat(float value) {
                if (_message._position + 3 >= _message._data.Length) _message.Resize(_message._data.Length * 2);
                _message._position = UnsafeByteConverter.PutFloat(_message._data, _message._position, value);
            }

            public void PutDouble(double value) {
                if (_message._position + 7 >= _message._data.Length) _message.Resize(_message._data.Length * 2);
                _message._position = UnsafeByteConverter.PutDouble(_message._data, _message._position, value);
            }

            public void PutString(string value) {
                if (_message._position + 4 + value.Length >= _message._data.Length) _message.Resize(_message._data.Length * 2);
                _message._position = UnsafeByteConverter.PutString(_message._data, _message._position, value);
            }
        }

        public readonly Reader AsReader;
        public readonly Writer AsWriter;
        
        private byte[] _data;
        private int _position;

        public byte[] Data => _data;
        public int Length => _position;

        public Message() {
            _data = new byte[1024];
            _position = 0;
            AsReader = new Reader(this);
            AsWriter = new Writer(this);
        }

        public void Reset() => _position = 0;

        public void Resize(int newSize) {
            if (newSize >= _data.Length) return;
            byte[] newData = new byte[newSize];
            Buffer.BlockCopy(_data, 0, newData, 0, _position);
            _data = newData;
        }
    }
}