using System.Runtime.InteropServices;

namespace NetworkLayer.Utils {
    [StructLayout(LayoutKind.Explicit)]
    public struct IntFloat32 {
        [FieldOffset(0)] public int intValue;
        [FieldOffset(0)] public float floatValue;
    }
    
    [StructLayout(LayoutKind.Explicit)]
    public struct IntFloat64 {
        [FieldOffset(0)] public long intValue;
        [FieldOffset(0)] public double floatValue;
    }
}