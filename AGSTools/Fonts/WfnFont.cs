using System;
using System.Buffers.Binary;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;

namespace AGSTools
{
    /// <summary>A monochrome bitmap glyph of a WFN font.</summary>
    public sealed class WfnGlyph : IEquatable<WfnGlyph>
    {
        private readonly byte[] _bits;

        /// <param name="packed">Rows of <c>(width + 7) / 8</c> bytes each, most significant bit is the leftmost pixel.</param>
        public WfnGlyph(int width, int height, byte[]? packed = null)
        {
            if (width < 0 || height < 0 || width > short.MaxValue || height > short.MaxValue)
                throw new ArgumentOutOfRangeException(nameof(width), "Invalid glyph size.");
            Width = width;
            Height = height;
            _bits = new byte[BytesPerRow * height];
            if (packed != null)
                Array.Copy(packed, _bits, Math.Min(packed.Length, _bits.Length));
        }

        public int Width { get; }
        public int Height { get; }
        public int BytesPerRow => (Width + 7) / 8;

        public bool IsEmpty => Width == 0 || Height == 0;

        public bool this[int x, int y]
        {
            get => (_bits[y * BytesPerRow + x / 8] & (0x80 >> (x % 8))) != 0;
            set
            {
                ref byte b = ref _bits[y * BytesPerRow + x / 8];
                b = value ? (byte)(b | (0x80 >> (x % 8))) : (byte)(b & ~(0x80 >> (x % 8)));
            }
        }

        /// <summary>The pixel rows in the file layout.</summary>
        public byte[] ToPacked() => (byte[])_bits.Clone();

        public WfnGlyph Clone() => new(Width, Height, _bits);

        /// <summary>A copy with a different size; pixels are kept at their position, new pixels are clear.</summary>
        public WfnGlyph Resize(int width, int height)
        {
            var result = new WfnGlyph(width, height);
            for (int y = 0; y < Math.Min(height, Height); y++)
                for (int x = 0; x < Math.Min(width, Width); x++)
                    result[x, y] = this[x, y];
            return result;
        }

        public bool Equals(WfnGlyph? other) =>
            other != null && other.Width == Width && other.Height == Height && other._bits.AsSpan().SequenceEqual(_bits);

        public override bool Equals(object? obj) => Equals(obj as WfnGlyph);

        public override int GetHashCode()
        {
            var hash = new HashCode();
            hash.Add(Width);
            hash.Add(Height);
            hash.AddBytes(_bits);
            return hash.ToHashCode();
        }
    }

    /// <summary>
    /// AGS bitmap font (agsfntN.wfn). Port of the format read by WFNFont::ReadFromFile in
    /// Common/font/wfnfont.cpp: a signature, the glyph data, and an offset table at the end
    /// with one 16-bit offset per character code.
    /// </summary>
    public sealed class WfnFont
    {
        private static readonly byte[] Signature = Encoding.ASCII.GetBytes("WGT Font File  ");
        private const int HeaderSize = 15 + 2;
        private const int GlyphHeaderSize = 4;

        /// <summary>Glyph per character code; null means the code has no glyph.</summary>
        public List<WfnGlyph?> Glyphs { get; } = new();

        /// <summary>Format problems found while reading (the engine tolerates them as well).</summary>
        public List<string> Problems { get; } = new();

        /// <summary>Height of the tallest glyph.</summary>
        public int Height => Glyphs.Max(g => g?.Height ?? 0);

        /// <summary>True if <paramref name="code"/> has a glyph with a size (a space counts, it has a width).</summary>
        public bool HasGlyph(int code) => code >= 0 && code < Glyphs.Count && Glyphs[code] is { IsEmpty: false };

        public WfnGlyph? GetGlyph(int code) => code >= 0 && code < Glyphs.Count ? Glyphs[code] : null;

        /// <summary>Sets the glyph of <paramref name="code"/>, extending the character table if needed.</summary>
        public void SetGlyph(int code, WfnGlyph? glyph)
        {
            if (code < 0 || code > ushort.MaxValue)
                throw new ArgumentOutOfRangeException(nameof(code));
            while (Glyphs.Count <= code)
                Glyphs.Add(null);
            Glyphs[code] = glyph;
        }

        public static WfnFont Read(string path) => Read(File.ReadAllBytes(path));

        public static WfnFont Read(byte[] data)
        {
            if (data.Length < HeaderSize || !data.AsSpan(0, Signature.Length).SequenceEqual(Signature))
                throw new AgsFormatException("Not a WFN font (signature missing).");

            int tableAddr = BinaryPrimitives.ReadUInt16LittleEndian(data.AsSpan(15));
            if (tableAddr < HeaderSize || tableAddr >= data.Length)
                throw new AgsFormatException($"WFN font has a bad offset table address ({tableAddr}).");

            var font = new WfnFont();
            int count = (data.Length - tableAddr) / 2;
            for (int code = 0; code < count; code++)
            {
                int offset = BinaryPrimitives.ReadUInt16LittleEndian(data.AsSpan(tableAddr + code * 2));
                if (offset == 0)
                {
                    font.Glyphs.Add(null);
                    continue;
                }
                if (offset < HeaderSize || offset + GlyphHeaderSize > tableAddr)
                {
                    font.Problems.Add($"Character {code}: bad glyph offset {offset}.");
                    font.Glyphs.Add(null);
                    continue;
                }

                int width = BinaryPrimitives.ReadUInt16LittleEndian(data.AsSpan(offset));
                int height = BinaryPrimitives.ReadUInt16LittleEndian(data.AsSpan(offset + 2));
                if (width > short.MaxValue || height > short.MaxValue)
                {
                    font.Problems.Add($"Character {code}: bad glyph size {width}x{height}.");
                    font.Glyphs.Add(null);
                    continue;
                }
                var glyph = new WfnGlyph(width, height);
                int available = Math.Max(0, tableAddr - offset - GlyphHeaderSize);
                int needed = glyph.BytesPerRow * height;
                if (needed > available)
                    font.Problems.Add($"Character {code}: glyph data exceeds the available data.");
                font.Glyphs.Add(new WfnGlyph(width, height, data.AsSpan(offset + GlyphHeaderSize, Math.Min(needed, available)).ToArray()));
            }
            return font;
        }

        /// <summary>
        /// Writes the font. Identical glyphs share their data; codes without a glyph point to a shared
        /// empty 0x0 glyph. Engines since 3.6.2 accept this silently; 3.6.1 logs a harmless format
        /// warning for any empty glyph (as it does for offset 0).
        /// </summary>
        public byte[] ToArray()
        {
            using var data = new MemoryStream();
            data.Write(Signature);
            data.Write(new byte[2]); // offset table address, patched below

            var offsets = new Dictionary<WfnGlyph, int>();
            int Add(WfnGlyph glyph)
            {
                if (offsets.TryGetValue(glyph, out int existing))
                    return existing;
                int offset = (int)data.Position;
                Span<byte> header = stackalloc byte[GlyphHeaderSize];
                BinaryPrimitives.WriteUInt16LittleEndian(header, (ushort)glyph.Width);
                BinaryPrimitives.WriteUInt16LittleEndian(header[2..], (ushort)glyph.Height);
                data.Write(header);
                data.Write(glyph.ToPacked());
                offsets[glyph] = offset;
                return offset;
            }

            var empty = new WfnGlyph(0, 0);
            var table = new int[Glyphs.Count];
            for (int code = 0; code < Glyphs.Count; code++)
                table[code] = Add(Glyphs[code] ?? empty);

            if (data.Position > ushort.MaxValue)
                throw new AgsFormatException($"WFN font glyph data is too large ({data.Position} bytes, at most {ushort.MaxValue}).");

            int tableAddr = (int)data.Position;
            Span<byte> entry = stackalloc byte[2];
            foreach (int offset in table)
            {
                BinaryPrimitives.WriteUInt16LittleEndian(entry, (ushort)offset);
                data.Write(entry);
            }

            byte[] result = data.ToArray();
            BinaryPrimitives.WriteUInt16LittleEndian(result.AsSpan(15), (ushort)tableAddr);
            return result;
        }

        public void Write(string path)
        {
            byte[] bytes = ToArray();
            AtomicFile.Write(path, s => s.Write(bytes));
        }
    }
}
