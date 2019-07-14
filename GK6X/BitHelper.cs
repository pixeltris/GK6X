using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace GK6X
{
    static class BitHelper
    {
        public static bool[] BytesToBits(byte[] bytes)
        {
            bool[] result = new bool[bytes.Length * 8];
            for (int i = 0; i < result.Length; i++)
            {
                int byteIndex = i / 8;
                int bitIndex = i % 8;
                result[i] = (bytes[byteIndex] & (byte)(1 << bitIndex)) != 0;
            }
            return result;
        }

        public static byte[] BitsToBytes(bool[] bits)
        {
            byte[] result = new byte[bits.Length / 8];
            for (int i = 0; i < bits.Length; i++)
            {
                if (bits[i])
                {
                    int byteIndex = i / 8;
                    int bitIndex = i % 8;
                    result[byteIndex] |= (byte)(1 << bitIndex);
                }
            }
            return result;
        }
    }
}
