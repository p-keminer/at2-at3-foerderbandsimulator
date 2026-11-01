using System;
using Plant.Core;

namespace Plant.Adapter
{
    // Explicit address-mode representation: S7 high byte at the smaller byte offset.
    // This conversion is NOT used for typed SDK calls, which already convert their values.
    public static class S7Bytes
    {
        public static byte[] Int16(short value) => new[] { (byte)((ushort)value >> 8), (byte)value };
        public static short Int16(byte[] bytes)
        {
            Numbers.Require(bytes.Length == 2, "INT benötigt zwei Bytes."); return unchecked((short)((bytes[0] << 8) | bytes[1]));
        }
        public static int Int32(byte[] bytes)
        {
            Numbers.Require(bytes.Length == 4, "DINT benötigt vier Bytes.");
            return unchecked((int)((uint)bytes[0] << 24 | (uint)bytes[1] << 16 | (uint)bytes[2] << 8 | bytes[3]));
        }
        public static double Double(byte[] bytes)
        {
            Numbers.Require(bytes.Length == 8, "LREAL benötigt acht Bytes."); var copy = (byte[])bytes.Clone();
            if (BitConverter.IsLittleEndian) Array.Reverse(copy);
            double value = BitConverter.ToDouble(copy, 0); Numbers.Require(Numbers.Finite(value), "Ungültiger LREAL-Messwert."); return value;
        }
    }
}
