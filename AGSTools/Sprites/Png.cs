using System;
using System.Buffers.Binary;
using System.IO;
using System.IO.Compression;

namespace AGSTools
{
    /// <summary>
    /// Minimal PNG reader and writer for sprite export and import, so the library needs no image package.
    /// Writes 8-bit RGBA. Reads all standard non-interlaced and Adam7 images (grey, RGB, palette, with or
    /// without alpha, 1 to 16 bits); 16-bit samples are reduced to 8 bits.
    /// </summary>
    public static class Png
    {
        private static readonly byte[] Signature = { 0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A };

        public static void Write(Stream output, SpriteImage image)
        {
            output.Write(Signature);
            var ihdr = new byte[13];
            BinaryPrimitives.WriteInt32BigEndian(ihdr, image.Width);
            BinaryPrimitives.WriteInt32BigEndian(ihdr.AsSpan(4), image.Height);
            ihdr[8] = 8;  // bit depth
            ihdr[9] = 6;  // RGBA
            WriteChunk(output, "IHDR", ihdr);

            int stride = image.Width * 4 + 1;
            var raw = new byte[stride * image.Height];
            for (int y = 0; y < image.Height; y++)
            {
                int row = y * stride;
                raw[row] = 0;
                for (int x = 0; x < image.Width; x++)
                {
                    uint p = image.Pixels[y * image.Width + x];
                    int o = row + 1 + x * 4;
                    raw[o] = (byte)(p >> 16);
                    raw[o + 1] = (byte)(p >> 8);
                    raw[o + 2] = (byte)p;
                    raw[o + 3] = (byte)(p >> 24);
                }
            }
            var compressed = new MemoryStream();
            using (var z = new ZLibStream(compressed, CompressionLevel.Optimal, leaveOpen: true))
                z.Write(raw);
            WriteChunk(output, "IDAT", compressed.ToArray());
            WriteChunk(output, "IEND", Array.Empty<byte>());
        }

        public static void Write(string path, SpriteImage image)
        {
            using var fs = File.Create(path);
            Write(fs, image);
        }

        public static SpriteImage Read(string path)
        {
            using var fs = File.OpenRead(path);
            return Read(fs);
        }

        /// <exception cref="AgsFormatException">Not a PNG file or a broken one.</exception>
        public static SpriteImage Read(Stream input)
        {
            var r = new BinaryReader(input);
            if (!r.ReadBytes(8).AsSpan().SequenceEqual(Signature))
                throw new AgsFormatException("Not a PNG file.");

            int width = 0, height = 0, depth = 0, colorType = -1, interlace = 0;
            byte[]? palette = null, transparency = null;
            var idat = new MemoryStream();
            while (true)
            {
                byte[] lengthBytes = r.ReadBytes(4);
                if (lengthBytes.Length < 4)
                    throw new AgsFormatException("The PNG file ends early.");
                int length = BinaryPrimitives.ReadInt32BigEndian(lengthBytes);
                string type = System.Text.Encoding.ASCII.GetString(r.ReadBytes(4));
                if (length < 0 || length > input.Length)
                    throw new AgsFormatException("Broken PNG chunk.");
                byte[] data = r.ReadBytes(length);
                r.ReadBytes(4); // CRC
                if (data.Length < length)
                    throw new AgsFormatException("The PNG file ends early.");
                switch (type)
                {
                    case "IHDR":
                        width = BinaryPrimitives.ReadInt32BigEndian(data);
                        height = BinaryPrimitives.ReadInt32BigEndian(data.AsSpan(4));
                        depth = data[8];
                        colorType = data[9];
                        interlace = data[12];
                        break;
                    case "PLTE":
                        palette = data;
                        break;
                    case "tRNS":
                        transparency = data;
                        break;
                    case "IDAT":
                        idat.Write(data);
                        break;
                }
                if (type == "IEND")
                    break;
            }
            if (width <= 0 || height <= 0 || width > 32767 || height > 32767)
                throw new AgsFormatException("Invalid PNG image size.");
            int channels = colorType switch { 0 => 1, 2 => 3, 3 => 1, 4 => 2, 6 => 4, _ => throw new AgsFormatException($"Unknown PNG color type {colorType}.") };
            if (colorType == 3 && palette == null)
                throw new AgsFormatException("PNG palette image without a palette.");

            idat.Position = 0;
            byte[] inflated;
            using (var z = new ZLibStream(idat, CompressionMode.Decompress))
            using (var all = new MemoryStream())
            {
                z.CopyTo(all);
                inflated = all.ToArray();
            }

            var image = new SpriteImage(width, height);
            int bitsPerPixel = channels * depth;
            int pos = 0;
            if (interlace == 0)
            {
                DecodePass(inflated, ref pos, width, height, bitsPerPixel, (x, y, px) => image[x, y] = px,
                    0, 0, 1, 1, depth, colorType, palette, transparency);
            }
            else
            {
                int[] sx = { 0, 4, 0, 2, 0, 1, 0 }, sy = { 0, 0, 4, 0, 2, 0, 1 };
                int[] dx = { 8, 8, 4, 4, 2, 2, 1 }, dy = { 8, 8, 8, 4, 4, 2, 2 };
                for (int p = 0; p < 7; p++)
                {
                    int pw = (width - sx[p] + dx[p] - 1) / dx[p], ph = (height - sy[p] + dy[p] - 1) / dy[p];
                    if (pw <= 0 || ph <= 0)
                        continue;
                    DecodePass(inflated, ref pos, pw, ph, bitsPerPixel, (x, y, px) => image[x, y] = px,
                        sx[p], sy[p], dx[p], dy[p], depth, colorType, palette, transparency);
                }
            }
            return image;
        }

        private static void DecodePass(byte[] data, ref int pos, int w, int h, int bitsPerPixel, Action<int, int, uint> set,
            int x0, int y0, int dx, int dy, int depth, int colorType, byte[]? palette, byte[]? trns)
        {
            int stride = (w * bitsPerPixel + 7) / 8;
            int bpp = Math.Max(1, bitsPerPixel / 8);
            var prev = new byte[stride];
            var cur = new byte[stride];
            for (int y = 0; y < h; y++)
            {
                if (pos + 1 + stride > data.Length)
                    throw new AgsFormatException("PNG image data is too short.");
                int filter = data[pos++];
                Buffer.BlockCopy(data, pos, cur, 0, stride);
                pos += stride;
                Unfilter(filter, cur, prev, bpp);
                for (int x = 0; x < w; x++)
                    set(x0 + x * dx, y0 + y * dy, Pixel(cur, x, depth, colorType, palette, trns));
                (prev, cur) = (cur, prev);
            }
        }

        private static void Unfilter(int filter, byte[] cur, byte[] prev, int bpp)
        {
            for (int i = 0; i < cur.Length; i++)
            {
                int a = i >= bpp ? cur[i - bpp] : 0, b = prev[i], c = i >= bpp ? prev[i - bpp] : 0;
                cur[i] = filter switch
                {
                    0 => cur[i],
                    1 => (byte)(cur[i] + a),
                    2 => (byte)(cur[i] + b),
                    3 => (byte)(cur[i] + (a + b) / 2),
                    4 => (byte)(cur[i] + Paeth(a, b, c)),
                    _ => throw new AgsFormatException($"Unknown PNG filter {filter}."),
                };
            }
        }

        private static int Paeth(int a, int b, int c)
        {
            int p = a + b - c, pa = Math.Abs(p - a), pb = Math.Abs(p - b), pc = Math.Abs(p - c);
            return pa <= pb && pa <= pc ? a : pb <= pc ? b : c;
        }

        private static int Sample(byte[] row, int index, int depth)
        {
            switch (depth)
            {
                case 8: return row[index];
                case 16: return row[index * 2];
                default:
                    int bit = index * depth;
                    return (row[bit / 8] >> (8 - depth - bit % 8)) & ((1 << depth) - 1);
            }
        }

        private static int Scale(int v, int depth) => depth >= 8 ? v : v * 255 / ((1 << depth) - 1);

        private static int RawSample16(byte[] row, int index) => row[index * 2] << 8 | row[index * 2 + 1];

        private static uint Pixel(byte[] row, int x, int depth, int colorType, byte[]? palette, byte[]? trns)
        {
            uint Argb(int a, int r, int g, int b) => (uint)(a << 24 | r << 16 | g << 8 | b);
            switch (colorType)
            {
                case 0:
                {
                    int raw = depth == 16 ? RawSample16(row, x) : Sample(row, x, depth);
                    bool transparent = trns is { Length: >= 2 } && raw == (trns[0] << 8 | trns[1]);
                    int v = Scale(Sample(row, x, depth), depth);
                    return Argb(transparent ? 0 : 255, v, v, v);
                }
                case 2:
                {
                    int r = Sample(row, x * 3, depth), g = Sample(row, x * 3 + 1, depth), b = Sample(row, x * 3 + 2, depth);
                    bool transparent = trns is { Length: >= 6 } && (depth == 16
                        ? RawSample16(row, x * 3) == (trns[0] << 8 | trns[1]) && RawSample16(row, x * 3 + 1) == (trns[2] << 8 | trns[3]) && RawSample16(row, x * 3 + 2) == (trns[4] << 8 | trns[5])
                        : r == trns[1] && g == trns[3] && b == trns[5]);
                    return Argb(transparent ? 0 : 255, r, g, b);
                }
                case 3:
                {
                    int i = Sample(row, x, depth);
                    if (i * 3 + 2 >= palette!.Length)
                        return 0;
                    int a = trns != null && i < trns.Length ? trns[i] : 255;
                    return Argb(a, palette[i * 3], palette[i * 3 + 1], palette[i * 3 + 2]);
                }
                case 4:
                {
                    int v = Sample(row, x * 2, depth);
                    return Argb(Sample(row, x * 2 + 1, depth), v, v, v);
                }
                default:
                    return Argb(Sample(row, x * 4 + 3, depth), Sample(row, x * 4, depth), Sample(row, x * 4 + 1, depth), Sample(row, x * 4 + 2, depth));
            }
        }

        private static void WriteChunk(Stream output, string type, byte[] data)
        {
            Span<byte> head = stackalloc byte[8];
            BinaryPrimitives.WriteInt32BigEndian(head, data.Length);
            System.Text.Encoding.ASCII.GetBytes(type, head[4..]);
            output.Write(head);
            output.Write(data);
            uint crc = Crc(head[4..], 0xFFFFFFFFu);
            crc = Crc(data, crc) ^ 0xFFFFFFFFu;
            Span<byte> c = stackalloc byte[4];
            BinaryPrimitives.WriteUInt32BigEndian(c, crc);
            output.Write(c);
        }

        private static readonly uint[] CrcTable = BuildCrcTable();

        private static uint[] BuildCrcTable()
        {
            var table = new uint[256];
            for (uint n = 0; n < 256; n++)
            {
                uint c = n;
                for (int k = 0; k < 8; k++)
                    c = (c & 1) != 0 ? 0xEDB88320u ^ (c >> 1) : c >> 1;
                table[n] = c;
            }
            return table;
        }

        private static uint Crc(ReadOnlySpan<byte> data, uint crc)
        {
            foreach (byte b in data)
                crc = CrcTable[(crc ^ b) & 0xFF] ^ (crc >> 8);
            return crc;
        }
    }
}
