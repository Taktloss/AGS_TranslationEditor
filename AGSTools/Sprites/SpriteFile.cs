using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;

namespace AGSTools
{
    /// <summary>Where and how one sprite is stored in acsprset.spr.</summary>
    public sealed record SpriteEntry(int Number, long Offset, int BytesPerPixel, int Format, int PaletteCount,
        int Compression, int Width, int Height, long DataOffset, long DataSize)
    {
        /// <summary>Bytes of one stored pixel: palette formats store one index byte per pixel.</summary>
        public int StoredPixelSize => Format == SpriteFile.FormatPlain ? BytesPerPixel : 1;
    }

    /// <summary>
    /// The sprite file of an AGS 3.6 game (acsprset.spr, version 12) and its index (sprindex.dat).
    /// Layout: version, signature, compression, file ID, highest sprite number, storage flags, then
    /// for every sprite number either an empty marker or a header, an optional palette and the pixel data.
    /// </summary>
    public sealed class SpriteFile
    {
        public const string FileName = "acsprset.spr";
        public const string IndexFileName = "sprindex.dat";

        /// <summary>Sprite flag in the game data: the 32-bit sprite uses its alpha channel.</summary>
        public const byte FlagAlphaChannel = 0x10;

        public const int FormatPlain = 0;
        public const int FormatPaletteRgb888 = 32;
        public const int FormatPaletteArgb8888 = 33;
        public const int FormatPaletteRgb565 = 34;

        public const int CompressionNone = 0;
        public const int CompressionRle = 1;
        public const int CompressionLzw = 2;
        public const int CompressionDeflate = 3;

        private const int SupportedVersion = 12;
        private const int IndexVersion = 11;
        private static readonly byte[] Signature = " Sprite File "u8.ToArray();
        private static readonly byte[] IndexSignature = "SPRINDEX"u8.ToArray();
        private const int HeaderSize = 2 + 13 + 1 + 4 + 4 + 4;

        private readonly SpriteEntry?[] _entries;

        private SpriteFile(int version, int compression, uint fileId, byte storeFlags, SpriteEntry?[] entries)
        {
            Version = version;
            Compression = compression;
            FileId = fileId;
            StoreFlags = storeFlags;
            _entries = entries;
        }

        public int Version { get; }

        /// <summary>Compression the game was built with; every sprite also states its own.</summary>
        public int Compression { get; }

        /// <summary>Random ID that ties sprindex.dat to this file.</summary>
        public uint FileId { get; }

        public byte StoreFlags { get; }

        /// <summary>Number of sprite slots (highest sprite number + 1).</summary>
        public int SlotCount => _entries.Length;

        /// <summary>The sprite with this number, or null for an empty slot.</summary>
        public SpriteEntry? this[int number] => number >= 0 && number < _entries.Length ? _entries[number] : null;

        public IEnumerable<SpriteEntry> Sprites
        {
            get
            {
                foreach (SpriteEntry? e in _entries)
                    if (e != null)
                        yield return e;
            }
        }

        /// <summary>Reads the sprite table; <paramref name="stream"/> must be seekable and start at the sprite file.</summary>
        /// <exception cref="AgsFormatException">Not a sprite file or a version other than 12.</exception>
        public static SpriteFile Read(Stream stream)
        {
            long start = stream.Position;
            var r = new BinaryReader(stream);
            int version = r.ReadInt16();
            if (!r.ReadBytes(Signature.Length).AsSpan().SequenceEqual(Signature))
                throw new AgsFormatException("Not an AGS sprite file.");
            if (version != SupportedVersion)
                throw new AgsFormatException($"Sprite file version {version} is not supported (only {SupportedVersion}, AGS 3.6 and later).");
            int compression = r.ReadByte();
            uint id = r.ReadUInt32();
            int top = r.ReadInt32();
            if (top < -1 || top >= 10_000_000)
                throw new AgsFormatException($"Invalid sprite count {top + 1}.");
            byte flags = r.ReadByte();
            r.ReadBytes(3);

            long length = stream.Length - start;
            var entries = new SpriteEntry?[top + 1];
            for (int n = 0; n <= top; n++)
            {
                long offset = stream.Position - start;
                int bpp = r.ReadByte();
                int format = r.ReadByte();
                if (bpp == 0)
                {
                    // Empty slot: a 16-bit zero
                    if (format != 0)
                        throw new AgsFormatException($"Invalid header of sprite {n} at offset {offset}.");
                    continue;
                }
                int palCount = r.ReadByte() + 1;
                int compr = r.ReadByte();
                int w = r.ReadUInt16(), h = r.ReadUInt16();
                if (bpp is not (1 or 2 or 4) || PaletteEntrySize(format) < 0 || w == 0 || h == 0)
                    throw new AgsFormatException($"Invalid header of sprite {n} at offset {offset}.");
                if (format == FormatPlain)
                    palCount = 0;
                stream.Position += (long)palCount * PaletteEntrySize(format);
                long size = r.ReadUInt32();
                long dataOffset = stream.Position - start;
                if (dataOffset + size > length)
                    throw new AgsFormatException($"Sprite {n} lies outside of the sprite file.");
                entries[n] = new SpriteEntry(n, offset, bpp, format, palCount, compr, w, h, dataOffset, size);
                stream.Position += size;
            }
            return new SpriteFile(version, compression, id, flags, entries);
        }

        private static int PaletteEntrySize(int format) => format switch
        {
            FormatPlain => 0,
            FormatPaletteRgb888 => 3,
            FormatPaletteArgb8888 => 4,
            FormatPaletteRgb565 => 2,
            _ => -1,
        };

        /// <summary>
        /// Decodes a sprite to ARGB pixels (0xAARRGGBB). Transparency follows the engine: palette index 0 in
        /// 8-bit sprites, magenta in 16-bit and in 32-bit sprites without an alpha channel.
        /// </summary>
        /// <param name="gamePalette">The game palette (256 x r, g, b, filler, 6 bit), for 8-bit sprites.</param>
        public SpriteImage ReadImage(Stream stream, long fileStart, SpriteEntry e, bool hasAlpha, byte[]? gamePalette)
        {
            stream.Position = fileStart + e.Offset + 8;
            var r = new BinaryReader(stream);
            byte[] palette = r.ReadBytes(e.PaletteCount * PaletteEntrySize(e.Format));
            stream.Position = fileStart + e.DataOffset;
            byte[] data = r.ReadBytes(checked((int)e.DataSize));
            byte[] raw = Decompress(data, e.Compression, e.StoredPixelSize, e.Width * e.StoredPixelSize, e.Height);

            var pixels = new uint[e.Width * e.Height];
            uint[]? lookup = e.Format == FormatPlain ? null : DecodePalette(palette, e, hasAlpha);
            for (int i = 0; i < pixels.Length; i++)
            {
                if (lookup != null)
                {
                    int index = raw[i];
                    pixels[i] = index < lookup.Length ? lookup[index] : 0;
                    continue;
                }
                pixels[i] = e.BytesPerPixel switch
                {
                    1 => FromGamePalette(raw[i], gamePalette),
                    2 => From565(BitConverter.ToUInt16(raw, i * 2)),
                    _ => From32(BitConverter.ToUInt32(raw, i * 4), hasAlpha),
                };
            }
            return new SpriteImage(e.Width, e.Height, pixels);
        }

        private static uint[] DecodePalette(byte[] palette, SpriteEntry e, bool hasAlpha)
        {
            var lookup = new uint[e.PaletteCount];
            for (int i = 0; i < lookup.Length; i++)
            {
                lookup[i] = e.Format switch
                {
                    FormatPaletteRgb565 => From565(BitConverter.ToUInt16(palette, i * 2)),
                    FormatPaletteArgb8888 => From32(BitConverter.ToUInt32(palette, i * 4), hasAlpha),
                    _ => From32((uint)(palette[i * 3] | palette[i * 3 + 1] << 8 | palette[i * 3 + 2] << 16), false),
                };
            }
            return lookup;
        }

        internal static uint FromGamePalette(byte index, byte[]? palette)
        {
            if (index == 0)
                return 0;
            if (palette == null || palette.Length < 256 * 4)
                return 0xFF000000u | (uint)(index * 0x010101);
            uint c(int k) => (uint)(Math.Min(palette[index * 4 + k], (byte)63) * 255 / 63);
            return 0xFF000000u | c(0) << 16 | c(1) << 8 | c(2);
        }

        internal static uint From565(ushort v)
        {
            if (v == 0xF81F)
                return 0;
            uint r = (uint)(v >> 11) * 255 / 31, g = (uint)((v >> 5) & 63) * 255 / 63, b = (uint)(v & 31) * 255 / 31;
            return 0xFF000000u | r << 16 | g << 8 | b;
        }

        internal static uint From32(uint v, bool hasAlpha)
        {
            if (hasAlpha)
                return v;
            return (v & 0xFFFFFF) == 0xFF00FF ? 0 : v | 0xFF000000u;
        }

        internal static byte[] Decompress(byte[] data, int compression, int itemSize, int rowBytes, int rows)
        {
            int total = rowBytes * rows;
            switch (compression)
            {
                case CompressionNone:
                    if (data.Length < total)
                        throw new AgsFormatException("Sprite data is too short.");
                    return data;
                case CompressionRle:
                    return Rle.Decode(data, itemSize, total);
                case CompressionDeflate:
                {
                    var output = new byte[total];
                    using var z = new ZLibStream(new MemoryStream(data), CompressionMode.Decompress);
                    z.ReadExactly(output);
                    return output;
                }
                default:
                    throw new AgsFormatException(compression == CompressionLzw
                        ? "LZW-compressed sprites are not supported yet."
                        : $"Unknown sprite compression {compression}.");
            }
        }

        /// <summary>
        /// Writes a copy of the sprite file with some sprites replaced, plus a matching index.
        /// The copy gets a new file ID, so the engine never pairs it with the game's original index.
        /// </summary>
        /// <param name="source">The original sprite file (seekable, positioned anywhere).</param>
        /// <param name="sourceStart">Offset of the sprite file in <paramref name="source"/>.</param>
        /// <param name="replacements">New pixels per sprite number; the size may differ from the original.</param>
        /// <param name="hasAlpha">Whether the game uses the alpha channel of a sprite number.</param>
        public void WritePatched(Stream source, long sourceStart, IReadOnlyDictionary<int, SpriteImage> replacements,
            Func<int, bool> hasAlpha, byte[]? gamePalette, Stream spriteOut, Stream indexOut, uint? newFileId = null, IProgress<double>? progress = null)
        {
            foreach (int n in replacements.Keys)
                if (this[n] == null)
                    throw new ArgumentException($"Sprite {n} does not exist in the game.", nameof(replacements));

            uint id = newFileId ?? NewFileId();
            var w = new BinaryWriter(spriteOut);
            long outStart = spriteOut.Position;
            source.Position = sourceStart;
            var header = new byte[HeaderSize];
            source.ReadExactly(header);
            BitConverter.TryWriteBytes(header.AsSpan(16), id);
            w.Write(header);

            var widths = new short[SlotCount];
            var heights = new short[SlotCount];
            var offsets = new long[SlotCount];
            var buffer = new byte[1 << 20];
            for (int n = 0; n < SlotCount; n++)
            {
                if (progress != null && n % 256 == 0)
                    progress.Report((double)n / SlotCount);
                offsets[n] = spriteOut.Position - outStart;
                SpriteEntry? e = _entries[n];
                if (e == null)
                {
                    w.Write((short)0);
                    continue;
                }
                if (replacements.TryGetValue(n, out SpriteImage? image))
                {
                    WriteSprite(w, e, image, hasAlpha(n), gamePalette);
                    widths[n] = (short)image.Width;
                    heights[n] = (short)image.Height;
                    continue;
                }
                widths[n] = (short)e.Width;
                heights[n] = (short)e.Height;
                CopyRange(source, sourceStart + e.Offset, e.DataOffset + e.DataSize - e.Offset, spriteOut, buffer);
            }
            w.Flush();
            progress?.Report(1);
            WriteIndex(indexOut, id, widths, heights, offsets);
        }

        private static void CopyRange(Stream source, long offset, long count, Stream target, byte[] buffer)
        {
            source.Position = offset;
            while (count > 0)
            {
                int read = source.Read(buffer, 0, (int)Math.Min(buffer.Length, count));
                if (read <= 0)
                    throw new EndOfStreamException("The sprite file ended early.");
                target.Write(buffer, 0, read);
                count -= read;
            }
        }

        private static uint NewFileId()
        {
            Span<byte> b = stackalloc byte[4];
            System.Security.Cryptography.RandomNumberGenerator.Fill(b);
            return BitConverter.ToUInt32(b);
        }

        /// <summary>
        /// Stores the image in the bit depth of the original sprite, without a palette, compressed with RLE
        /// unless the original was uncompressed.
        /// </summary>
        private static void WriteSprite(BinaryWriter w, SpriteEntry original, SpriteImage image, bool hasAlpha, byte[]? gamePalette)
        {
            if (image.Width > ushort.MaxValue || image.Height > ushort.MaxValue)
                throw new ArgumentException($"Sprite {original.Number} is too large.");
            int bpp = original.BytesPerPixel;
            byte[] raw = new byte[image.Width * image.Height * bpp];
            for (int i = 0; i < image.Pixels.Length; i++)
            {
                uint p = image.Pixels[i];
                switch (bpp)
                {
                    case 1:
                        raw[i] = ToGamePalette(p, gamePalette);
                        break;
                    case 2:
                        BitConverter.TryWriteBytes(raw.AsSpan(i * 2), To565(p));
                        break;
                    default:
                        BitConverter.TryWriteBytes(raw.AsSpan(i * 4), To32(p, hasAlpha));
                        break;
                }
            }
            int compression = original.Compression == CompressionNone ? CompressionNone : CompressionRle;
            byte[] data = compression == CompressionRle ? Rle.Encode(raw, bpp, image.Width * bpp) : raw;
            w.Write((byte)bpp);
            w.Write((byte)FormatPlain);
            w.Write((byte)0);
            w.Write((byte)compression);
            w.Write((ushort)image.Width);
            w.Write((ushort)image.Height);
            w.Write((uint)data.Length);
            w.Write(data);
        }

        internal static uint To32(uint p, bool hasAlpha)
        {
            if (hasAlpha)
                return p;
            // The engine compares with the exact mask value, so transparent pixels need alpha 0 as well;
            // an opaque pixel in the mask color is moved by one step so it stays visible
            if (p >> 24 < 128)
                return 0x00FF00FFu;
            uint v = p | 0xFF000000u;
            return v == 0xFFFF00FFu ? 0xFFFE00FFu : v;
        }

        internal static ushort To565(uint p)
        {
            if (p >> 24 < 128)
                return 0xF81F;
            uint r = (p >> 16) & 255, g = (p >> 8) & 255, b = p & 255;
            ushort v = (ushort)(((r * 31 + 127) / 255) << 11 | ((g * 63 + 127) / 255) << 5 | ((b * 31 + 127) / 255));
            return v == 0xF81F ? (ushort)0xF81E : v;
        }

        internal static byte ToGamePalette(uint p, byte[]? palette)
        {
            if (p >> 24 < 128)
                return 0;
            int best = 1, bestDistance = int.MaxValue;
            for (int i = 1; i < 256; i++)
            {
                uint c = FromGamePalette((byte)i, palette);
                int dr = (int)((c >> 16) & 255) - (int)((p >> 16) & 255);
                int dg = (int)((c >> 8) & 255) - (int)((p >> 8) & 255);
                int db = (int)(c & 255) - (int)(p & 255);
                int d = dr * dr + dg * dg + db * db;
                if (d < bestDistance)
                {
                    bestDistance = d;
                    best = i;
                }
            }
            return (byte)best;
        }

        private static void WriteIndex(Stream output, uint id, short[] widths, short[] heights, long[] offsets)
        {
            var w = new BinaryWriter(output);
            w.Write(IndexSignature);
            w.Write(IndexVersion);
            w.Write(id);
            w.Write(widths.Length - 1);
            w.Write(widths.Length);
            foreach (short v in widths)
                w.Write(v);
            foreach (short v in heights)
                w.Write(v);
            foreach (long v in offsets)
                w.Write(v);
            w.Flush();
        }

        /// <summary>The run-length encoding of AGS sprites, with 1, 2 or 4 byte items.</summary>
        internal static class Rle
        {
            // A control byte c in 0..127 is followed by c + 1 literal items; c in -127..-1 by one item
            // that repeats 1 - c times. -128 is never written.
            public static byte[] Decode(byte[] data, int itemSize, int total)
            {
                var output = new byte[total];
                int pos = 0, outPos = 0;
                while (outPos < total)
                {
                    if (pos >= data.Length)
                        throw new AgsFormatException("RLE sprite data is too short.");
                    sbyte c = (sbyte)data[pos++];
                    if (c == -128)
                        continue;
                    int count = c >= 0 ? c + 1 : 1 - c;
                    if (outPos + count * itemSize > total)
                        throw new AgsFormatException("RLE sprite data runs past the image.");
                    if (c >= 0)
                    {
                        int bytes = count * itemSize;
                        if (pos + bytes > data.Length)
                            throw new AgsFormatException("RLE sprite data is too short.");
                        Buffer.BlockCopy(data, pos, output, outPos, bytes);
                        pos += bytes;
                        outPos += bytes;
                    }
                    else
                    {
                        if (pos + itemSize > data.Length)
                            throw new AgsFormatException("RLE sprite data is too short.");
                        for (int k = 0; k < count; k++)
                            Buffer.BlockCopy(data, pos, output, outPos + k * itemSize, itemSize);
                        pos += itemSize;
                        outPos += count * itemSize;
                    }
                }
                Consumed = pos;
                return output;
            }

            /// <summary>Bytes the last <see cref="Decode"/> on this thread used (for diagnostics).</summary>
            [ThreadStatic] internal static int Consumed;

            /// <summary>Encodes row by row, so no run crosses a row.</summary>
            public static byte[] Encode(byte[] raw, int itemSize, int rowBytes)
            {
                var output = new MemoryStream(raw.Length / 2 + 16);
                int rowItems = rowBytes / itemSize;
                for (int row = 0; row < raw.Length / rowBytes; row++)
                {
                    int rowStart = row * rowBytes;
                    int i = 0;
                    while (i < rowItems)
                    {
                        int run = 1;
                        while (i + run < rowItems && run < 128 && SameItem(raw, rowStart, i, i + run, itemSize))
                            run++;
                        if (run > 1)
                        {
                            output.WriteByte((byte)(sbyte)(1 - run));
                            output.Write(raw, rowStart + i * itemSize, itemSize);
                            i += run;
                            continue;
                        }
                        int literal = 1;
                        while (i + literal < rowItems && literal < 128
                            && !(i + literal + 1 < rowItems && SameItem(raw, rowStart, i + literal, i + literal + 1, itemSize)))
                            literal++;
                        output.WriteByte((byte)(literal - 1));
                        output.Write(raw, rowStart + i * itemSize, literal * itemSize);
                        i += literal;
                    }
                }
                return output.ToArray();
            }

            private static bool SameItem(byte[] raw, int rowStart, int a, int b, int itemSize) =>
                raw.AsSpan(rowStart + a * itemSize, itemSize).SequenceEqual(raw.AsSpan(rowStart + b * itemSize, itemSize));
        }
    }

    /// <summary>A decoded sprite: ARGB pixels (0xAARRGGBB), row by row.</summary>
    public sealed class SpriteImage
    {
        public SpriteImage(int width, int height, uint[]? pixels = null)
        {
            if (width <= 0 || height <= 0)
                throw new ArgumentOutOfRangeException(nameof(width), "A sprite needs at least one pixel.");
            Width = width;
            Height = height;
            Pixels = pixels ?? new uint[width * height];
            if (Pixels.Length != width * height)
                throw new ArgumentException("Pixel count does not match the size.", nameof(pixels));
        }

        public int Width { get; }
        public int Height { get; }
        public uint[] Pixels { get; }

        public uint this[int x, int y]
        {
            get => Pixels[y * Width + x];
            set => Pixels[y * Width + x] = value;
        }
    }
}
