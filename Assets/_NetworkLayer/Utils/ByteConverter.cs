using System;

namespace NetworkLayer.Utils {
    public static class ByteConverter {
        public static int PutByte(byte[] buffer, int offset, byte value) {
            if (offset >= buffer.Length) throw new IndexOutOfRangeException();
            buffer[offset] = value;
            return offset + 1;
        }

        public static int PutBool(byte[] buffer, int offset, bool value) => PutByte(buffer, offset, (byte) (value ? 1 : 0));

        public static byte GetByte(byte[] buffer, int offset, out int nextOffset) {
            if (offset >= buffer.Length) throw new IndexOutOfRangeException();
            nextOffset = offset + 1;
            return buffer[offset];
        }

        public static bool GetBool(byte[] buffer, int offset, out int nextOffset) => GetByte(buffer, offset, out nextOffset) == 1;
    }
}