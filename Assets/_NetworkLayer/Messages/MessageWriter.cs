using System;
using NetworkLayer.Utils;

namespace NetworkLayer {
    public sealed class MessageWriter {
        private byte[] _data;
        private int _position;

        public byte[] Data => _data;
        public int Length => _position;
        
        public MessageWriter() {
            _data = new byte[1024];
            _position = 0;
        }
        
        public void Reset() => _position = 0;

        private void Resize() {
            byte[] newData = new byte[_data.Length * 2];
            Buffer.BlockCopy(_data, 0, newData, 0, _position);
            _data = newData;
        }

        public void PutByte(byte value) {
            if (_position >= _data.Length) Resize();
            _position = ByteConverter.PutByte(_data, _position, value);
        }
        
        public void PutShort(short value) {
            if (_position + 1 >= _data.Length) Resize();
            _position = UnsafeByteConverter.PutShort(_data, _position, value);
        }
        
        public void PutUShort(ushort value) {
            if (_position + 1 >= _data.Length) Resize();
            _position = UnsafeByteConverter.PutUShort(_data, _position, value);
        }
        
        public void PutInt(int value) {
            if (_position + 3 >= _data.Length) Resize();
            _position = UnsafeByteConverter.PutInt(_data, _position, value);
        }

        public void PutUInt(uint value) {
            if (_position + 3 >= _data.Length) Resize();
            _position = UnsafeByteConverter.PutUInt(_data, _position, value);
        }

        public void PutLong(long value) {
            if (_position + 7 >= _data.Length) Resize();
            _position = UnsafeByteConverter.PutLong(_data, _position, value);
        }

        public void PutULong(ulong value) {
            if (_position + 7 >= _data.Length) Resize();
            _position = UnsafeByteConverter.PutULong(_data, _position, value);
        }

        public void PutBool(bool value) {
            if (_position >= _data.Length) Resize();
            _position = ByteConverter.PutBool(_data, _position, value);
        }

        public void PutChar(char value) {
            if (_position + 1 >= _data.Length) Resize();
            _position = UnsafeByteConverter.PutChar(_data, _position, value);
        }

        public void PutFloat(float value) {
            if (_position + 3 >= _data.Length) Resize();
            _position = UnsafeByteConverter.PutFloat(_data, _position, value);
        }
        
        public void PutDouble(double value) {
            if (_position + 7 >= _data.Length) Resize();
            _position = UnsafeByteConverter.PutDouble(_data, _position, value);
        }
        
        public void PutString(string value) {
            if (_position + 4 + value.Length >= _data.Length) Resize();
            _position = UnsafeByteConverter.PutString(_data, _position, value);
        }
    }
}