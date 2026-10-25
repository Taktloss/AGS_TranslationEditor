using System;
using System.Buffers.Binary;
using System.Collections.Generic;
using System.IO;
using System.Text;

namespace AGSTools
{
    /// <summary>
    /// Minimal TrueType/OpenType reader: character map, names and raw glyph outlines. Enough to tell
    /// which characters a font can draw; rendering is left to a real font engine.
    /// </summary>
    public sealed class TtfFont
    {
        private readonly byte[] _data;
        private readonly Dictionary<string, (int Offset, int Length)> _tables = new(StringComparer.Ordinal);
        private readonly Dictionary<int, int> _cmap = new();
        private int[]? _loca;

        private TtfFont(byte[] data) => _data = data;

        /// <summary>Full font name (name ID 4), or the family name.</summary>
        public string Name { get; private set; } = "";

        public string FamilyName { get; private set; } = "";

        public int UnitsPerEm { get; private set; }

        public int GlyphCount { get; private set; }

        /// <summary>True if the font only has a Microsoft Symbol character map (codes are mapped to U+F0xx).</summary>
        public bool IsSymbolFont { get; private set; }

        /// <summary>Character codes that have a glyph.</summary>
        public IReadOnlyCollection<int> CodePoints => _cmap.Keys;

        public byte[] Data => _data;

        public static TtfFont Read(string path) => Read(File.ReadAllBytes(path));

        public static TtfFont Read(byte[] data)
        {
            var font = new TtfFont(data);
            try
            {
                font.ReadTables();
            }
            catch (ArgumentOutOfRangeException)
            {
                throw new AgsFormatException("TrueType font is truncated or corrupt.");
            }
            return font;
        }

        /// <summary>Glyph index for a character code; 0 (the "missing glyph") if there is none.</summary>
        public int GetGlyphIndex(int codePoint)
        {
            if (_cmap.TryGetValue(codePoint, out int glyph))
                return glyph;
            // FreeType maps 8-bit codes to the U+F0xx range of symbol fonts
            if (IsSymbolFont && codePoint < 0x100 && _cmap.TryGetValue(0xF000 + codePoint, out glyph))
                return glyph;
            return 0;
        }

        public bool HasGlyph(int codePoint) => GetGlyphIndex(codePoint) != 0;

        /// <summary>
        /// True if both characters are drawn with the same outline, e.g. an "ä" that is only a copy of "a".
        /// False if either character is missing.
        /// </summary>
        public bool HasSameOutline(int codePointA, int codePointB)
        {
            int a = GetGlyphIndex(codePointA), b = GetGlyphIndex(codePointB);
            if (a == 0 || b == 0)
                return false;
            if (a == b)
                return true;
            return GetGlyphData(a, out ReadOnlySpan<byte> dataA) && GetGlyphData(b, out ReadOnlySpan<byte> dataB)
                && dataA.SequenceEqual(dataB);
        }

        private bool GetGlyphData(int glyph, out ReadOnlySpan<byte> data)
        {
            data = default;
            if (_loca == null || glyph + 1 >= _loca.Length || !_tables.TryGetValue("glyf", out var glyf))
                return false;
            int start = _loca[glyph], end = _loca[glyph + 1];
            if (start > end || end > glyf.Length)
                return false;
            data = _data.AsSpan(glyf.Offset + start, end - start);
            return true;
        }

        private ushort U16(int offset) => BinaryPrimitives.ReadUInt16BigEndian(_data.AsSpan(offset, 2));
        private uint U32(int offset) => BinaryPrimitives.ReadUInt32BigEndian(_data.AsSpan(offset, 4));

        private void ReadTables()
        {
            uint version = U32(0);
            if (version != 0x00010000 && version != 0x74727565 /* true */ && version != 0x4F54544F /* OTTO */)
                throw new AgsFormatException("Not a TrueType/OpenType font.");

            int numTables = U16(4);
            for (int i = 0; i < numTables; i++)
            {
                int rec = 12 + i * 16;
                string tag = Encoding.ASCII.GetString(_data, rec, 4);
                int offset = (int)U32(rec + 8), length = (int)U32(rec + 12);
                if (offset < 0 || length < 0 || (long)offset + length > _data.Length)
                    throw new AgsFormatException($"TrueType table '{tag}' is outside the file.");
                _tables[tag] = (offset, length);
            }

            if (_tables.TryGetValue("head", out var head))
                UnitsPerEm = U16(head.Offset + 18);
            if (_tables.TryGetValue("maxp", out var maxp))
                GlyphCount = U16(maxp.Offset + 4);
            ReadNames();
            ReadCmap();
            ReadLoca(head.Length > 0 ? (short)U16(head.Offset + 50) : 0);
        }

        private void ReadNames()
        {
            if (!_tables.TryGetValue("name", out var name))
                return;
            int count = U16(name.Offset + 2), strings = name.Offset + U16(name.Offset + 4);
            string? full = null, family = null;
            for (int i = 0; i < count; i++)
            {
                int rec = name.Offset + 6 + i * 12;
                int platform = U16(rec), nameId = U16(rec + 6), length = U16(rec + 8), offset = U16(rec + 10);
                if (nameId is not (1 or 4) || strings + offset + length > _data.Length)
                    continue;
                string? value = platform switch
                {
                    0 or 3 => Encoding.BigEndianUnicode.GetString(_data, strings + offset, length),
                    1 => Encoding.Latin1.GetString(_data, strings + offset, length),
                    _ => null,
                };
                // Prefer the Windows/Unicode names over the Mac ones
                if (value == null || (platform == 1 && (nameId == 4 ? full : family) != null))
                    continue;
                if (nameId == 4) full = value; else family = value;
            }
            FamilyName = family ?? "";
            Name = full ?? FamilyName;
        }

        private void ReadCmap()
        {
            if (!_tables.TryGetValue("cmap", out var cmap))
                return;
            int count = U16(cmap.Offset + 2);
            int best = -1, bestRank = 0;
            bool symbol = false;
            for (int i = 0; i < count; i++)
            {
                int rec = cmap.Offset + 4 + i * 8;
                int platform = U16(rec), encoding = U16(rec + 2);
                int offset = cmap.Offset + (int)U32(rec + 4);
                int format = U16(offset);
                // Same preference as FreeType: full Unicode, then BMP Unicode, then symbol
                int rank = (platform, encoding, format) switch
                {
                    (3, 10, 12) or (0, 4, 12) or (0, 6, 12) => 5,
                    (3, 1, _) or (0, _, _) when format is 4 or 12 or 6 or 0 => 4,
                    (3, 0, _) => 2,
                    (1, 0, 0 or 6) => 1,
                    _ => 0,
                };
                if (rank > bestRank)
                {
                    best = offset;
                    bestRank = rank;
                    symbol = platform == 3 && encoding == 0;
                }
            }
            if (best < 0)
                return;
            IsSymbolFont = symbol;
            ReadCmapSubtable(best);
        }

        private void ReadCmapSubtable(int offset)
        {
            switch (U16(offset))
            {
                case 0:
                    for (int c = 0; c < 256; c++)
                        Map(c, _data[offset + 6 + c]);
                    break;
                case 4:
                {
                    int segCount = U16(offset + 6) / 2;
                    int ends = offset + 14, starts = ends + segCount * 2 + 2;
                    int deltas = starts + segCount * 2, rangeOffsets = deltas + segCount * 2;
                    for (int s = 0; s < segCount; s++)
                    {
                        int end = U16(ends + s * 2), start = U16(starts + s * 2);
                        int delta = (short)U16(deltas + s * 2);
                        int rangeOffsetAt = rangeOffsets + s * 2, rangeOffset = U16(rangeOffsetAt);
                        for (int c = start; c <= end && c != 0xFFFF; c++)
                        {
                            int glyph;
                            if (rangeOffset == 0)
                                glyph = (c + delta) & 0xFFFF;
                            else
                            {
                                int at = rangeOffsetAt + rangeOffset + (c - start) * 2;
                                glyph = at + 2 <= _data.Length ? U16(at) : 0;
                                if (glyph != 0)
                                    glyph = (glyph + delta) & 0xFFFF;
                            }
                            Map(c, glyph);
                        }
                    }
                    break;
                }
                case 6:
                {
                    int first = U16(offset + 6), count = U16(offset + 8);
                    for (int i = 0; i < count; i++)
                        Map(first + i, U16(offset + 10 + i * 2));
                    break;
                }
                case 12:
                {
                    int groups = (int)U32(offset + 12);
                    for (int g = 0; g < groups; g++)
                    {
                        int rec = offset + 16 + g * 12;
                        uint start = U32(rec), end = U32(rec + 4), glyph = U32(rec + 8);
                        for (uint c = start; c <= end && c <= 0x10FFFF; c++)
                            Map((int)c, (int)(glyph + c - start));
                    }
                    break;
                }
            }
        }

        private void Map(int codePoint, int glyph)
        {
            if (glyph != 0 && (GlyphCount == 0 || glyph < GlyphCount))
                _cmap.TryAdd(codePoint, glyph);
        }

        private void ReadLoca(int indexToLocFormat)
        {
            if (GlyphCount == 0 || !_tables.TryGetValue("loca", out var loca))
                return;
            int entries = GlyphCount + 1;
            int size = indexToLocFormat == 0 ? 2 : 4;
            if (loca.Length < entries * size)
                return;
            _loca = new int[entries];
            for (int i = 0; i < entries; i++)
                _loca[i] = indexToLocFormat == 0 ? U16(loca.Offset + i * 2) * 2 : (int)U32(loca.Offset + i * 4);
        }
    }
}
