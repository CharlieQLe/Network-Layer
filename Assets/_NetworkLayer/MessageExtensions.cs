using System;
using UnityEngine;

namespace NetworkLayer {
    public static class MessageExtensions {
        public static Vector2 ReadVector2(this Message.Reader reader) {
            if (!reader.CheckLength(8)) throw new IndexOutOfRangeException();
            return new Vector2(reader.ReadFloat(), reader.ReadFloat());
        }
        
        public static Vector3 ReadVector3(this Message.Reader reader) {
            if (!reader.CheckLength(12)) throw new IndexOutOfRangeException();
            return new Vector3(reader.ReadFloat(), reader.ReadFloat(), reader.ReadFloat());
        }
        
        public static Quaternion ReadQuaternion(this Message.Reader reader) {
            if (!reader.CheckLength(16)) throw new IndexOutOfRangeException();
            return new Quaternion(reader.ReadFloat(), reader.ReadFloat(), reader.ReadFloat(), reader.ReadFloat());
        }

        public static void PutVector2(this Message.Writer writer, Vector2 value) {
            writer.PutFloat(value.x);
            writer.PutFloat(value.y);
        }
        
        public static void PutVector3(this Message.Writer writer, Vector3 value) {
            writer.PutFloat(value.x);
            writer.PutFloat(value.y);
            writer.PutFloat(value.z);
        }
        
        public static void PutQuaternion(this Message.Writer writer, Quaternion value) {
            writer.PutFloat(value.x);
            writer.PutFloat(value.y);
            writer.PutFloat(value.z);
            writer.PutFloat(value.w);
        }
    }
}