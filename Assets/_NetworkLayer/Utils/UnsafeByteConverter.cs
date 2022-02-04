using System;
using System.Text;

namespace NetworkLayer.Utils {
    public static unsafe class UnsafeByteConverter {
        public static int PutShort(byte[] buffer, int offset, short value) {
            if (offset + 1 >= buffer.Length) throw new IndexOutOfRangeException();
            fixed (byte* pbuffer = &buffer[offset]) {
                *(short*) pbuffer = value;
            }
            return offset + 2;
        }
        
        public static int PutUShort(byte[] buffer, int offset, ushort value) {
            if (offset + 1 >= buffer.Length) throw new IndexOutOfRangeException();
            fixed (byte* pbuffer = &buffer[offset]) {
                *(ushort*) pbuffer = value;
            }
            return offset + 2;
        }
        
        public static int PutInt(byte[] buffer, int offset, int value) {
            if (offset + 3 >= buffer.Length) throw new IndexOutOfRangeException();
            fixed (byte* pbuffer = &buffer[offset]) {
                *(int*) pbuffer = value;
            }
            return offset + 4;
        }
        
        public static int PutUInt(byte[] buffer, int offset, uint value) {
            if (offset + 3 >= buffer.Length) throw new IndexOutOfRangeException();
            fixed (byte* pbuffer = &buffer[offset]) {
                *(uint*) pbuffer = value;
            }
            return offset + 4;
        }
        
        public static int PutLong(byte[] buffer, int offset, long value) {
            if (offset + 3 >= buffer.Length) throw new IndexOutOfRangeException();
            fixed (byte* pbuffer = &buffer[offset]) {
                *(long*) pbuffer = value;
            }
            return offset + 4;
        }
        
        public static int PutULong(byte[] buffer, int offset, ulong value) {
            if (offset + 3 >= buffer.Length) throw new IndexOutOfRangeException();
            fixed (byte* pbuffer = &buffer[offset]) {
                *(ulong*) pbuffer = value;
            }
            return offset + 4;
        }

        public static int PutChar(byte[] buffer, int offset, char value) => PutShort(buffer, offset, (short) value);

        public static int PutFloat(byte[] buffer, int offset, float value) {
            if (offset + 3 >= buffer.Length) throw new IndexOutOfRangeException();
            fixed (byte* pbuffer = &buffer[offset]) {
                *(int*) pbuffer = *(int*)&value;
            }
            return offset + 4;
        }
        
        public static int PutDouble(byte[] buffer, int offset, double value) {
            if (offset + 7 >= buffer.Length) throw new IndexOutOfRangeException();
            fixed (byte* pbuffer = &buffer[offset]) {
                *(long*) pbuffer = *(long*)&value;
            }
            return offset + 8;
        }

        public static int PutString(byte[] buffer, int offset, string value) {
            if (offset + 3 + value.Length >= buffer.Length) throw new IndexOutOfRangeException();
            PutInt(buffer, offset, value.Length);
            Encoding.ASCII.GetBytes(value, 0, value.Length, buffer, offset);
            return offset + 4 + value.Length;
        }

        public static short GetShort(byte[] buffer, int offset, out int nextOffset) {
            if (offset + 1 >= buffer.Length) throw new IndexOutOfRangeException();
            nextOffset = offset + 2;
            fixed (byte* pbuffer = &buffer[offset]) {
                return *(short*) pbuffer;
            }
        }
        
        public static ushort GetUShort(byte[] buffer, int offset, out int nextOffset) {
            if (offset + 1 >= buffer.Length) throw new IndexOutOfRangeException();
            nextOffset = offset + 2;
            fixed (byte* pbuffer = &buffer[offset]) {
                return *(ushort*) pbuffer;
            }
        }
        
        public static int GetInt(byte[] buffer, int offset, out int nextOffset) {
            if (offset + 3 >= buffer.Length) throw new IndexOutOfRangeException();
            nextOffset = offset + 4;
            fixed (byte* pbuffer = &buffer[offset]) {
                return *(int*) pbuffer;
            }
        }
        
        public static uint GetUInt(byte[] buffer, int offset, out int nextOffset) {
            if (offset + 3 >= buffer.Length) throw new IndexOutOfRangeException();
            nextOffset = offset + 4;
            fixed (byte* pbuffer = &buffer[offset]) {
                return *(uint*) pbuffer;
            }
        }
        
        public static long GetLong(byte[] buffer, int offset, out int nextOffset) {
            if (offset + 7 >= buffer.Length) throw new IndexOutOfRangeException();
            nextOffset = offset + 8;
            fixed (byte* pbuffer = &buffer[offset]) {
                return *(long*) pbuffer;
            }
        }
        
        public static ulong GetULong(byte[] buffer, int offset, out int nextOffset) {
            if (offset + 7 >= buffer.Length) throw new IndexOutOfRangeException();
            nextOffset = offset + 8;
            fixed (byte* pbuffer = &buffer[offset]) {
                return *(ulong*) pbuffer;
            }
        }

        public static char GetChar(byte[] buffer, int offset, out int nextOffset) => (char) GetShort(buffer, offset, out nextOffset);

        public static float GetFloat(byte[] buffer, int offset, out int nextOffset) {
            if (offset + 3 >= buffer.Length) throw new IndexOutOfRangeException();
            nextOffset = offset + 4;
            fixed (byte* pbuffer = &buffer[offset]) {
                return *(float*)(int*) pbuffer;
            }
        }
        
        public static double GetDouble(byte[] buffer, int offset, out int nextOffset) {
            if (offset + 7 >= buffer.Length) throw new IndexOutOfRangeException();
            nextOffset = offset + 8;
            fixed (byte* pbuffer = &buffer[offset]) {
                return *(double*)(long*) pbuffer;
            }
        }
        
        public static string GetString(byte[] buffer, int offset, out int nextOffset) {
            int stringLength = GetInt(buffer, offset, out int stringIndex);
            if (offset + 3 + stringLength >= buffer.Length) throw new IndexOutOfRangeException();
            nextOffset = offset + 4 + stringLength;
            return Encoding.ASCII.GetString(buffer, stringIndex, stringLength);
        }
    }
}