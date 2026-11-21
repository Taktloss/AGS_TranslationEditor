using System;
using System.Collections.Generic;
using System.IO;

namespace AGSTools
{
    /// <summary>
    /// The "LZW" compression AGS uses for room backgrounds, which is really an LZ77 variant with a 4 KB window:
    /// a flag byte announces eight items, each a literal byte (bit clear) or a 16-bit back reference (bit set)
    /// with the distance - 1 in the low 12 bits and the length - 3 in the high 4 bits.
    /// </summary>
    internal static class AgsLzw
    {
        private const int Window = 4096;
        private const int MinMatch = 3;
        private const int MaxMatch = 18;
        private const int MaxChain = 48;

        /// <summary>Expands <paramref name="data"/> into exactly <paramref name="size"/> bytes.</summary>
        /// <param name="used">How many bytes of <paramref name="data"/> were needed.</param>
        public static byte[] Decode(ReadOnlySpan<byte> data, int size, out int used)
        {
            var output = new byte[size];
            int o = 0, p = 0;
            while (o < size)
            {
                if (p >= data.Length)
                    throw new AgsFormatException("Compressed room image ends too early.");
                int flags = data[p++];
                for (int bit = 0; bit < 8 && o < size; bit++)
                {
                    if ((flags & (1 << bit)) == 0)
                    {
                        if (p >= data.Length)
                            throw new AgsFormatException("Compressed room image ends too early.");
                        output[o++] = data[p++];
                        continue;
                    }
                    if (p + 1 >= data.Length)
                        throw new AgsFormatException("Compressed room image ends too early.");
                    int code = data[p] | data[p + 1] << 8;
                    p += 2;
                    int distance = (code & 0xFFF) + 1, length = (code >> 12) + MinMatch;
                    // The window starts out zeroed, so references before the start give zeros
                    int from = o - distance;
                    for (int k = 0; k < length && o < size; k++, from++)
                        output[o++] = from < 0 ? (byte)0 : output[from];
                }
            }
            used = p;
            return output;
        }

        public static byte[] Encode(byte[] input)
        {
            var output = new MemoryStream(input.Length / 2 + 16);
            // Hash chains over 3-byte prefixes, limited to the window
            var head = new int[1 << 15];
            var previous = new int[input.Length];
            Array.Fill(head, -1);
            int Hash(int i) => ((input[i] << 10) ^ (input[i + 1] << 5) ^ input[i + 2]) & 0x7FFF;
            void Insert(int i)
            {
                if (i + 2 >= input.Length)
                    return;
                int h = Hash(i);
                previous[i] = head[h];
                head[h] = i;
            }

            var items = new List<byte>(17);
            int flags = 0, count = 0;
            void Flush()
            {
                output.WriteByte((byte)flags);
                foreach (byte b in items)
                    output.WriteByte(b);
                items.Clear();
                flags = 0;
                count = 0;
            }

            int pos = 0;
            while (pos < input.Length)
            {
                int bestLength = 0, bestDistance = 0;
                if (pos + MinMatch <= input.Length)
                {
                    int limit = Math.Min(MaxMatch, input.Length - pos);
                    int candidate = head[Hash(pos)];
                    for (int chain = 0; candidate >= 0 && chain < MaxChain; chain++, candidate = previous[candidate])
                    {
                        int distance = pos - candidate;
                        // Distance 4096 would read the window slot being written; stay below it
                        if (distance >= Window)
                            break;
                        int length = 0;
                        while (length < limit && input[candidate + length] == input[pos + length])
                            length++;
                        if (length > bestLength)
                        {
                            bestLength = length;
                            bestDistance = distance;
                            if (length == limit)
                                break;
                        }
                    }
                }

                if (bestLength >= MinMatch)
                {
                    int code = (bestLength - MinMatch) << 12 | (bestDistance - 1);
                    flags |= 1 << count;
                    items.Add((byte)code);
                    items.Add((byte)(code >> 8));
                    for (int k = 0; k < bestLength; k++)
                        Insert(pos + k);
                    pos += bestLength;
                }
                else
                {
                    items.Add(input[pos]);
                    Insert(pos);
                    pos++;
                }
                if (++count == 8)
                    Flush();
            }
            if (count > 0)
                Flush();
            return output.ToArray();
        }
    }
}
