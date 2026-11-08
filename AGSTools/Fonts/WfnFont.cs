using System;
using System.Buffers.Binary;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;

namespace AGSTools
{
    /// <summary>
    /// A bitmap glyph of a WFN font: monochrome (1 bit per pixel) or with 16 shades (4 bits per pixel,
    /// shade 0 is transparent).
    /// </summary>
    public sealed class WfnGlyph : IEquatable<WfnGlyph>
    {
        private readonly byte[] _bits;

        /// <param name="packed">Rows of <see cref="BytesPerRow"/> bytes each in the file layout: with 1 bit per
        /// pixel the most significant bit is the leftmost pixel, with 4 bits the high nibble is.</param>
        /// <param name="bitsPerPixel">1 or 4.</param>
        public WfnGlyph(int width, int height, byte[]? packed = null, int bitsPerPixel = 1)
        {
            if (width < 0 || height < 0 || width > short.MaxValue || height > short.MaxValue)
                throw new ArgumentOutOfRangeException(nameof(width), "Invalid glyph size.");
            if (bitsPerPixel is not (1 or 4))
                throw new ArgumentOutOfRangeException(nameof(bitsPerPixel), "Only 1 and 4 bits per pixel are supported.");
            Width = width;
            Height = height;
            BitsPerPixel = bitsPerPixel;
            _bits = new byte[BytesPerRow * height];
            if (packed != null)
                Array.Copy(packed, _bits, Math.Min(packed.Length, _bits.Length));
        }

        public int Width { get; }
        public int Height { get; }
        public int BitsPerPixel { get; }
        public int BytesPerRow => (Width * BitsPerPixel + 7) / 8;

        /// <summary>Highest shade a pixel can have: 1 for monochrome glyphs, 15 for shaded ones.</summary>
        public int MaxShade => (1 << BitsPerPixel) - 1;

        public bool IsEmpty => Width == 0 || Height == 0;

        /// <summary>
        /// True if the pixel is drawn (shade above 0). Setting true keeps a shade the pixel already has and
        /// otherwise gives it the default text color shade (<see cref="WfnFont.DefaultBaseShade"/>).
        /// </summary>
        public bool this[int x, int y]
        {
            get => GetShade(x, y) != 0;
            set
            {
                if (!value)
                    SetShade(x, y, 0);
                else if (GetShade(x, y) == 0)
                    SetShade(x, y, BitsPerPixel == 1 ? 1 : WfnFont.DefaultBaseShade);
            }
        }

        public int GetShade(int x, int y)
        {
            if (BitsPerPixel == 1)
                return (_bits[y * BytesPerRow + x / 8] >> (7 - x % 8)) & 1;
            byte b = _bits[y * BytesPerRow + x / 2];
            return x % 2 == 0 ? b >> 4 : b & 0x0F;
        }

        public void SetShade(int x, int y, int shade)
        {
            if (shade < 0 || shade > MaxShade)
                throw new ArgumentOutOfRangeException(nameof(shade));
            if (BitsPerPixel == 1)
            {
                ref byte bit = ref _bits[y * BytesPerRow + x / 8];
                int mask = 0x80 >> (x % 8);
                bit = shade != 0 ? (byte)(bit | mask) : (byte)(bit & ~mask);
                return;
            }
            ref byte b = ref _bits[y * BytesPerRow + x / 2];
            b = x % 2 == 0 ? (byte)((b & 0x0F) | (shade << 4)) : (byte)((b & 0xF0) | shade);
        }

        /// <summary>The pixel rows in the file layout.</summary>
        public byte[] ToPacked() => (byte[])_bits.Clone();

        public WfnGlyph Clone() => new(Width, Height, _bits, BitsPerPixel);

        /// <summary>A copy with a different size; pixels are kept at their position, new pixels are clear.</summary>
        public WfnGlyph Resize(int width, int height)
        {
            var result = new WfnGlyph(width, height, null, BitsPerPixel);
            for (int y = 0; y < Math.Min(height, Height); y++)
                for (int x = 0; x < Math.Min(width, Width); x++)
                    result.SetShade(x, y, GetShade(x, y));
            return result;
        }

        /// <summary>
        /// A copy with another pixel depth. Monochrome pixels become <paramref name="inkShade"/>; going to
        /// monochrome, every shaded pixel becomes set.
        /// </summary>
        public WfnGlyph ToBitsPerPixel(int bitsPerPixel, int inkShade = WfnFont.DefaultBaseShade)
        {
            if (bitsPerPixel == BitsPerPixel)
                return Clone();
            var result = new WfnGlyph(Width, Height, null, bitsPerPixel);
            int ink = bitsPerPixel == 1 ? 1 : Math.Clamp(inkShade, 1, result.MaxShade);
            for (int y = 0; y < Height; y++)
                for (int x = 0; x < Width; x++)
                    if (GetShade(x, y) != 0)
                        result.SetShade(x, y, ink);
            return result;
        }

        public bool Equals(WfnGlyph? other) =>
            other != null && other.Width == Width && other.Height == Height && other.BitsPerPixel == BitsPerPixel
            && other._bits.AsSpan().SequenceEqual(_bits);

        public override bool Equals(object? obj) => Equals(obj as WfnGlyph);

        public override int GetHashCode()
        {
            var hash = new HashCode();
            hash.Add(Width);
            hash.Add(Height);
            hash.Add(BitsPerPixel);
            hash.AddBytes(_bits);
            return hash.ToHashCode();
        }
    }

    /// <summary>
    /// AGS bitmap font (agsfntN.wfn), in one of two formats read by WFNFont::ReadFromFile in
    /// Common/font/wfnfont.cpp:
    /// <list type="bullet">
    /// <item>Classic, 1 bit per pixel: the signature "WGT Font File  ", a 16-bit address of the offset
    /// table, the glyph data, and the offset table at the end with one 16-bit offset per character code.</item>
    /// <item>Extended, 4 bits per pixel (AGS 3.6.4 and later): the signature "WGT Font File 2", a header of
    /// 32-bit values (header size, flags, bits per pixel, glyph table address and size, character table
    /// address and size, base shade), a glyph table and a character table of (code, glyph offset) pairs
    /// for the codes that have a glyph. Glyph offsets are relative to the glyph table.</item>
    /// </list>
    /// A glyph is a 16-bit width and height followed by its pixel rows.
    /// </summary>
    public sealed class WfnFont
    {
        private static readonly byte[] Signature = Encoding.ASCII.GetBytes("WGT Font File  ");
        private static readonly byte[] ExtendedSignature = Encoding.ASCII.GetBytes("WGT Font File 2");
        private const int SignatureSize = 15;
        private const int HeaderSize = SignatureSize + 2;
        private const int ExtendedHeaderSize = 8 * 4;
        private const int GlyphHeaderSize = 4;

        /// <summary>Base shade of a shaded font if the file gives none: the shade drawn in the exact text color.</summary>
        public const int DefaultBaseShade = 8;

        /// <summary>First AGS version whose engine reads shaded (4-bit) WFN fonts.</summary>
        public static readonly Version ShadedSince = new(3, 6, 4);

        /// <summary>Glyph per character code; null means the code has no glyph.</summary>
        public List<WfnGlyph?> Glyphs { get; } = new();

        /// <summary>Format problems found while reading (the engine tolerates them as well).</summary>
        public List<string> Problems { get; } = new();

        /// <summary>1 for a classic monochrome font, 4 for a shaded one. See <see cref="SetBitsPerPixel"/>.</summary>
        public int BitsPerPixel { get; private set; } = 1;

        /// <summary>
        /// Shaded fonts: the shade (1-15) drawn in the exact text color; lower shades are darker, higher
        /// ones lighter. The engine uses <see cref="DefaultBaseShade"/> if the value is out of range.
        /// </summary>
        public int BaseShade { get; set; } = DefaultBaseShade;

        public bool IsShaded => BitsPerPixel == 4;

        /// <summary>The base shade the engine draws with: <see cref="BaseShade"/>, or the default if it is out of range.</summary>
        public int EffectiveBaseShade => BaseShade is >= 1 and <= 15 ? BaseShade : DefaultBaseShade;

        /// <summary>Height of the tallest glyph.</summary>
        public int Height => Glyphs.Max(g => g?.Height ?? 0);

        /// <summary>True if <paramref name="code"/> has a glyph with a size (a space counts, it has a width).</summary>
        public bool HasGlyph(int code) => code >= 0 && code < Glyphs.Count && Glyphs[code] is { IsEmpty: false };

        public WfnGlyph? GetGlyph(int code) => code >= 0 && code < Glyphs.Count ? Glyphs[code] : null;

        /// <summary>
        /// Sets the glyph of <paramref name="code"/>, extending the character table if needed. A glyph with
        /// another pixel depth is converted (see <see cref="WfnGlyph.ToBitsPerPixel"/>).
        /// </summary>
        public void SetGlyph(int code, WfnGlyph? glyph)
        {
            if (code < 0 || code > ushort.MaxValue)
                throw new ArgumentOutOfRangeException(nameof(code));
            while (Glyphs.Count <= code)
                Glyphs.Add(null);
            Glyphs[code] = glyph != null && glyph.BitsPerPixel != BitsPerPixel ? glyph.ToBitsPerPixel(BitsPerPixel, BaseShade) : glyph;
        }

        /// <summary>Converts the font and all its glyphs to 1 (classic) or 4 (shaded) bits per pixel.</summary>
        public void SetBitsPerPixel(int bitsPerPixel)
        {
            if (bitsPerPixel is not (1 or 4))
                throw new ArgumentOutOfRangeException(nameof(bitsPerPixel), "Only 1 and 4 bits per pixel are supported.");
            BitsPerPixel = bitsPerPixel;
            for (int code = 0; code < Glyphs.Count; code++)
                if (Glyphs[code] is { } glyph && glyph.BitsPerPixel != bitsPerPixel)
                    Glyphs[code] = glyph.ToBitsPerPixel(bitsPerPixel, BaseShade);
        }

        public static WfnFont Read(string path) => Read(File.ReadAllBytes(path));

        public static WfnFont Read(byte[] data)
        {
            if (data.Length >= SignatureSize && data.AsSpan(0, SignatureSize).SequenceEqual(ExtendedSignature))
                return ReadExtended(data);
            if (data.Length < HeaderSize || !data.AsSpan(0, SignatureSize).SequenceEqual(Signature))
                throw new AgsFormatException("Not a WFN font (signature missing).");

            int tableAddr = BinaryPrimitives.ReadUInt16LittleEndian(data.AsSpan(SignatureSize));
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
                font.Glyphs.Add(ReadGlyph(font, data.AsSpan(0, tableAddr), offset, code, 1));
            }
            return font;
        }

        private static WfnFont ReadExtended(byte[] data)
        {
            if (data.Length < SignatureSize + ExtendedHeaderSize)
                throw new AgsFormatException("WFN font header is incomplete.");
            uint Field(int index) => BinaryPrimitives.ReadUInt32LittleEndian(data.AsSpan(SignatureSize + index * 4));
            uint headerSize = Field(0), flags = Field(1), bitsPerPixel = Field(2);
            ulong glyphAddr = Field(3), glyphSize = Field(4), charAddr = Field(5), charSize = Field(6);
            uint baseShade = Field(7);

            ulong headerEnd = SignatureSize + (ulong)headerSize;
            if (headerSize < ExtendedHeaderSize || headerEnd > (ulong)data.Length)
                throw new AgsFormatException($"WFN font has a bad header size ({headerSize}).");
            if (bitsPerPixel != 4)
                throw new AgsFormatException($"WFN font has {bitsPerPixel} bits per pixel; only 1 and 4 are supported.");
            if (flags != 0)
                throw new AgsFormatException($"WFN font has unknown header flags ({flags}).");
            if (glyphAddr < headerEnd || glyphAddr + glyphSize > (ulong)data.Length
                || charAddr < headerEnd || charAddr + charSize > (ulong)data.Length)
                throw new AgsFormatException("WFN font has a bad table address.");
            if (glyphSize != 0 && charSize != 0 && glyphAddr < charAddr + charSize && charAddr < glyphAddr + glyphSize)
                throw new AgsFormatException("WFN font tables overlap.");
            if (charSize % 8 != 0)
                throw new AgsFormatException("WFN font has a bad character table size.");

            var font = new WfnFont { BitsPerPixel = 4, BaseShade = (int)Math.Min(baseShade, int.MaxValue) };
            if (headerSize > ExtendedHeaderSize)
                font.Problems.Add("The header has fields this version does not know; they are dropped when saving.");
            if (baseShade is < 1 or > 15)
                font.Problems.Add($"Base shade {baseShade} is out of range; the engine uses {DefaultBaseShade}.");

            ReadOnlySpan<byte> glyphs = data.AsSpan((int)glyphAddr, (int)glyphSize);
            var entries = new SortedDictionary<int, int>();
            for (int i = 0; i < (int)(charSize / 8); i++)
            {
                uint code = BinaryPrimitives.ReadUInt32LittleEndian(data.AsSpan((int)charAddr + i * 8));
                uint offset = BinaryPrimitives.ReadUInt32LittleEndian(data.AsSpan((int)charAddr + i * 8 + 4));
                if (code > ushort.MaxValue)
                    throw new AgsFormatException($"WFN font has character code {code}; at most {ushort.MaxValue} are supported.");
                if (!entries.TryAdd((int)code, (int)Math.Min(offset, int.MaxValue)))
                    throw new AgsFormatException($"WFN font lists character {code} twice.");
            }

            // A glyph's data ends where the next glyph starts (the engine's rule for short data)
            int glyphLength = glyphs.Length;
            var starts = entries.Values.Distinct().Where(o => (long)o + GlyphHeaderSize <= glyphLength).OrderBy(o => o).ToList();
            var read = new Dictionary<int, WfnGlyph?>();
            for (int i = 0; i < starts.Count; i++)
            {
                int end = i + 1 < starts.Count ? starts[i + 1] : glyphs.Length;
                if (starts[i] + GlyphHeaderSize > end)
                    throw new AgsFormatException("WFN font glyphs overlap.");
                read[starts[i]] = ReadGlyph(font, glyphs[..end], starts[i], -1, 4);
            }
            foreach (var (code, offset) in entries)
            {
                if (!read.TryGetValue(offset, out WfnGlyph? glyph))
                {
                    font.Problems.Add($"Character {code}: bad glyph offset {offset}.");
                    continue;
                }
                font.Glyphs.AddRange(Enumerable.Repeat<WfnGlyph?>(null, code + 1 - font.Glyphs.Count));
                font.Glyphs[code] = glyph;
            }
            return font;
        }

        /// <summary>Reads the glyph at <paramref name="offset"/>; its pixel data may not go past the end of <paramref name="data"/>.</summary>
        private static WfnGlyph? ReadGlyph(WfnFont font, ReadOnlySpan<byte> data, int offset, int code, int bitsPerPixel)
        {
            string what = code >= 0 ? $"Character {code}" : $"Glyph at {offset}";
            int width = BinaryPrimitives.ReadUInt16LittleEndian(data[offset..]);
            int height = BinaryPrimitives.ReadUInt16LittleEndian(data[(offset + 2)..]);
            if (width > short.MaxValue || height > short.MaxValue)
            {
                font.Problems.Add($"{what}: bad glyph size {width}x{height}.");
                return null;
            }
            var glyph = new WfnGlyph(width, height, null, bitsPerPixel);
            int available = Math.Max(0, data.Length - offset - GlyphHeaderSize);
            int needed = glyph.BytesPerRow * height;
            if (needed > available)
                font.Problems.Add($"{what}: glyph data exceeds the available data.");
            return new WfnGlyph(width, height, data.Slice(offset + GlyphHeaderSize, Math.Min(needed, available)).ToArray(), bitsPerPixel);
        }

        /// <summary>
        /// Writes the font: a classic font as a classic WFN, a shaded font in the extended format.
        /// Identical glyphs share their data. In a classic font, codes without a glyph point to a shared
        /// empty 0x0 glyph (engines since 3.6.2 accept this silently; 3.6.1 logs a harmless format warning
        /// for any empty glyph, as it does for offset 0); the extended format just leaves them out.
        /// </summary>
        public byte[] ToArray() => IsShaded ? ToExtendedArray() : ToClassicArray();

        private static void WriteGlyph(Stream data, WfnGlyph glyph)
        {
            Span<byte> header = stackalloc byte[GlyphHeaderSize];
            BinaryPrimitives.WriteUInt16LittleEndian(header, (ushort)glyph.Width);
            BinaryPrimitives.WriteUInt16LittleEndian(header[2..], (ushort)glyph.Height);
            data.Write(header);
            data.Write(glyph.ToPacked());
        }

        private byte[] ToClassicArray()
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
                WriteGlyph(data, glyph);
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
            BinaryPrimitives.WriteUInt16LittleEndian(result.AsSpan(SignatureSize), (ushort)tableAddr);
            return result;
        }

        private byte[] ToExtendedArray()
        {
            using var glyphs = new MemoryStream();
            var offsets = new Dictionary<WfnGlyph, int>();
            var characters = new List<(int Code, int Offset)>();
            for (int code = 0; code < Glyphs.Count; code++)
            {
                if (Glyphs[code] is not { } glyph)
                    continue;
                if (!offsets.TryGetValue(glyph, out int offset))
                {
                    offset = (int)glyphs.Position;
                    WriteGlyph(glyphs, glyph);
                    offsets[glyph] = offset;
                }
                characters.Add((code, offset));
            }

            int glyphAddr = SignatureSize + ExtendedHeaderSize;
            int charAddr = glyphAddr + (int)glyphs.Length;
            using var data = new MemoryStream();
            data.Write(ExtendedSignature);
            var value = new byte[4];
            void Write(int v)
            {
                BinaryPrimitives.WriteInt32LittleEndian(value, v);
                data.Write(value);
            }
            foreach (int field in new[] { ExtendedHeaderSize, 0, 4, glyphAddr, (int)glyphs.Length, charAddr, characters.Count * 8, BaseShade })
                Write(field);
            glyphs.Position = 0;
            glyphs.CopyTo(data);
            foreach (var (code, offset) in characters)
            {
                Write(code);
                Write(offset);
            }
            return data.ToArray();
        }

        public void Write(string path)
        {
            byte[] bytes = ToArray();
            AtomicFile.Write(path, s => s.Write(bytes));
        }
    }
}
