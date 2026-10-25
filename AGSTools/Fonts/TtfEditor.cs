using System;
using System.Buffers.Binary;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;

namespace AGSTools
{
    /// <summary>
    /// Editable TrueType font: replaces or adds glyphs for code points and writes a consistent font file.
    /// </summary>
    /// <remarks>
    /// Written from the OpenType specification. Only the tables that depend on the glyphs are rebuilt
    /// (glyf, loca, hmtx, hhea, maxp, cmap, post, head, OS/2 char range); all other tables are copied.
    /// Tables that would become stale and are optional (DSIG, hdmx, LTSH, and vhea/vmtx when glyphs are
    /// added) are dropped. Glyphs are never removed or reordered, so glyph indices in GSUB/GPOS/kern stay valid.
    /// </remarks>
    public sealed class TtfEditor
    {
        private readonly SortedDictionary<string, byte[]> _tables = new(StringComparer.Ordinal);
        private readonly List<byte[]> _glyphs = new();
        private readonly List<(int Advance, int Lsb)> _metrics = new();
        private readonly Dictionary<int, int> _cmap = new();
        private readonly int _originalGlyphCount;
        private readonly bool _hasMacCmap, _hasFullUnicodeCmap;
        private readonly List<string>? _glyphNames;

        private TtfEditor(byte[] data)
        {
            var reader = TtfFont.Read(data);
            if (BinaryPrimitives.ReadUInt32BigEndian(data) == 0x4F54544F)
                throw new AgsFormatException("OpenType fonts with CFF outlines cannot be edited, only TrueType outlines.");

            int numTables = BinaryPrimitives.ReadUInt16BigEndian(data.AsSpan(4));
            for (int i = 0; i < numTables; i++)
            {
                int rec = 12 + i * 16;
                string tag = Encoding.ASCII.GetString(data, rec, 4);
                int offset = (int)BinaryPrimitives.ReadUInt32BigEndian(data.AsSpan(rec + 8));
                int length = (int)BinaryPrimitives.ReadUInt32BigEndian(data.AsSpan(rec + 12));
                _tables[tag] = data.AsSpan(offset, length).ToArray();
            }
            foreach (string required in new[] { "head", "hhea", "hmtx", "maxp", "loca", "glyf", "cmap" })
                if (!_tables.ContainsKey(required))
                    throw new AgsFormatException($"TrueType font has no '{required}' table.");

            byte[] head = _tables["head"], hhea = _tables["hhea"], hmtx = _tables["hmtx"], loca = _tables["loca"], glyf = _tables["glyf"];
            int glyphCount = U16(_tables["maxp"], 4);
            bool longLoca = I16(head, 50) != 0;
            for (int i = 0; i < glyphCount; i++)
            {
                int start = longLoca ? (int)U32(loca, i * 4) : U16(loca, i * 2) * 2;
                int end = longLoca ? (int)U32(loca, i * 4 + 4) : U16(loca, i * 2 + 2) * 2;
                _glyphs.Add(start < end && end <= glyf.Length ? glyf.AsSpan(start, end - start).ToArray() : Array.Empty<byte>());
            }
            _originalGlyphCount = glyphCount;

            int hMetrics = Math.Clamp(U16(hhea, 34), 1, glyphCount);
            for (int i = 0; i < glyphCount; i++)
            {
                int advance = U16(hmtx, Math.Min(i, hMetrics - 1) * 4);
                int lsbAt = i < hMetrics ? i * 4 + 2 : hMetrics * 4 + (i - hMetrics) * 2;
                _metrics.Add((advance, lsbAt + 2 <= hmtx.Length ? I16(hmtx, lsbAt) : 0));
            }

            foreach (int code in reader.CodePoints)
                _cmap[code] = reader.GetGlyphIndex(code);
            IsSymbolFont = reader.IsSymbolFont;
            byte[] cmap = _tables["cmap"];
            for (int i = 0; i < U16(cmap, 2); i++)
            {
                int platform = U16(cmap, 4 + i * 8), encoding = U16(cmap, 6 + i * 8);
                int format = U16(cmap, (int)U32(cmap, 8 + i * 8));
                _hasMacCmap |= platform == 1 && encoding == 0;
                _hasFullUnicodeCmap |= format == 12;
            }

            _glyphNames = ReadPostNames();
            UnitsPerEm = U16(head, 18);
            Ascender = I16(hhea, 4);
            Descender = I16(hhea, 6);
        }

        public static TtfEditor Load(byte[] data)
        {
            try
            {
                return new TtfEditor(data);
            }
            catch (Exception ex) when (ex is ArgumentOutOfRangeException or ArgumentException or IndexOutOfRangeException)
            {
                throw new AgsFormatException("TrueType font is truncated or corrupt.");
            }
        }

        public static TtfEditor Load(string path) => Load(File.ReadAllBytes(path));

        public int UnitsPerEm { get; }

        /// <summary>Typographic ascender and descender (hhea), in font units; the descender is negative.</summary>
        public int Ascender { get; }
        public int Descender { get; }

        public int GlyphCount => _glyphs.Count;

        public bool IsSymbolFont { get; }

        /// <summary>True after a change; <see cref="ToArray"/> then gives a different font.</summary>
        public bool IsModified { get; private set; }

        public IReadOnlyCollection<int> CodePoints => _cmap.Keys;

        public int GetGlyphIndex(int codePoint) => _cmap.TryGetValue(codePoint, out int glyph) ? glyph : 0;

        public bool HasGlyph(int codePoint) => GetGlyphIndex(codePoint) != 0;

        public int GetAdvance(int glyph) => _metrics[glyph].Advance;

        public TtfGlyphOutline GetOutline(int glyph) =>
            GlyfCodec.Decode(_glyphs[glyph], i => i >= 0 && i < _glyphs.Count ? _glyphs[i] : null);

        /// <summary>
        /// All simple glyph outlines (for analysing the font, e.g. its pixel grid).
        /// </summary>
        public IEnumerable<TtfGlyphOutline> SimpleOutlines() =>
            _glyphs.Where(g => !GlyfCodec.IsComposite(g)).Select(g => GlyfCodec.Decode(g, _ => null)).Where(o => !o.IsEmpty);

        /// <summary>
        /// Sets the glyph drawn for <paramref name="codePoint"/>. The glyph is replaced in place if only
        /// this code point uses it; otherwise (no glyph yet, shared with other code points, or a component of
        /// another glyph) a new glyph is added, so other characters keep their look.
        /// </summary>
        /// <returns>The glyph index now mapped to the code point.</returns>
        public int SetGlyph(int codePoint, TtfGlyphOutline outline, int advanceWidth)
        {
            if (codePoint < 0 || codePoint > 0x10FFFF || codePoint == 0xFFFF)
                throw new ArgumentOutOfRangeException(nameof(codePoint));
            if (advanceWidth < 0 || advanceWidth > ushort.MaxValue)
                throw new ArgumentOutOfRangeException(nameof(advanceWidth));

            byte[] data = GlyfCodec.Encode(outline);
            int glyph = GetGlyphIndex(codePoint);
            bool shared = glyph == 0
                || _cmap.Any(m => m.Value == glyph && m.Key != codePoint)
                || _glyphs.Any(g => GlyfCodec.Components(g).Contains(glyph));
            if (shared)
            {
                if (_glyphs.Count >= ushort.MaxValue)
                    throw new AgsFormatException("The font has the maximum number of glyphs.");
                glyph = _glyphs.Count;
                _glyphs.Add(data);
                _metrics.Add((0, 0));
                _glyphNames?.Add(UniqueName(codePoint));
            }
            else
                _glyphs[glyph] = data;
            _metrics[glyph] = (advanceWidth, outline.IsEmpty ? 0 : outline.XMin);
            _cmap[codePoint] = glyph;
            IsModified = true;
            return glyph;
        }

        /// <summary>Removes the code point from the character map; the glyph itself stays in the font.</summary>
        public bool RemoveCodePoint(int codePoint)
        {
            bool removed = _cmap.Remove(codePoint);
            IsModified |= removed;
            return removed;
        }

        public void Write(string path)
        {
            byte[] bytes = ToArray();
            AtomicFile.Write(path, s => s.Write(bytes));
        }

        /// <summary>Builds the font file.</summary>
        public byte[] ToArray()
        {
            var tables = new SortedDictionary<string, byte[]>(_tables, StringComparer.Ordinal);
            bool glyphsAdded = _glyphs.Count != _originalGlyphCount;
            foreach (string tag in new[] { "DSIG", "hdmx", "LTSH" })
                tables.Remove(tag);
            if (glyphsAdded)
            {
                tables.Remove("vhea");
                tables.Remove("vmtx");
            }

            // glyf and loca; glyphs are padded to 4 bytes
            using var glyf = new MemoryStream();
            var offsets = new List<int>();
            foreach (byte[] g in _glyphs)
            {
                offsets.Add((int)glyf.Position);
                glyf.Write(g);
                while (glyf.Position % 4 != 0)
                    glyf.WriteByte(0);
            }
            offsets.Add((int)glyf.Position);
            bool longLoca = glyf.Position > 0x1FFFE;
            var loca = new byte[offsets.Count * (longLoca ? 4 : 2)];
            for (int i = 0; i < offsets.Count; i++)
            {
                if (longLoca) BinaryPrimitives.WriteUInt32BigEndian(loca.AsSpan(i * 4), (uint)offsets[i]);
                else BinaryPrimitives.WriteUInt16BigEndian(loca.AsSpan(i * 2), (ushort)(offsets[i] / 2));
            }
            tables["glyf"] = glyf.ToArray();
            tables["loca"] = loca;

            // Metrics: one full entry per glyph
            var hmtx = new byte[_glyphs.Count * 4];
            for (int i = 0; i < _glyphs.Count; i++)
            {
                BinaryPrimitives.WriteUInt16BigEndian(hmtx.AsSpan(i * 4), (ushort)_metrics[i].Advance);
                BinaryPrimitives.WriteInt16BigEndian(hmtx.AsSpan(i * 4 + 2), (short)_metrics[i].Lsb);
            }
            tables["hmtx"] = hmtx;

            int xMin = int.MaxValue, yMin = int.MaxValue, xMax = int.MinValue, yMax = int.MinValue;
            int minLsb = int.MaxValue, minRsb = int.MaxValue, maxExtent = int.MinValue;
            int maxPoints = 0, maxContours = 0;
            for (int i = 0; i < _glyphs.Count; i++)
            {
                if (_glyphs[i].Length < 10)
                    continue;
                var b = GlyfCodec.Bounds(_glyphs[i]);
                xMin = Math.Min(xMin, b.XMin); yMin = Math.Min(yMin, b.YMin);
                xMax = Math.Max(xMax, b.XMax); yMax = Math.Max(yMax, b.YMax);
                int lsb = _metrics[i].Lsb, extent = lsb + (b.XMax - b.XMin);
                minLsb = Math.Min(minLsb, lsb);
                minRsb = Math.Min(minRsb, _metrics[i].Advance - extent);
                maxExtent = Math.Max(maxExtent, extent);
                var (points, contours) = GlyfCodec.Size(_glyphs[i]);
                maxPoints = Math.Max(maxPoints, points);
                maxContours = Math.Max(maxContours, contours);
            }
            bool anyOutline = xMin != int.MaxValue;

            byte[] head = (byte[])_tables["head"].Clone();
            BinaryPrimitives.WriteUInt32BigEndian(head.AsSpan(8), 0); // checkSumAdjustment, set below
            if (anyOutline)
            {
                W16(head, 36, xMin); W16(head, 38, yMin); W16(head, 40, xMax); W16(head, 42, yMax);
            }
            W16(head, 50, longLoca ? 1 : 0);
            tables["head"] = head;

            byte[] hhea = (byte[])_tables["hhea"].Clone();
            W16(hhea, 10, _metrics.Max(m => m.Advance));
            if (anyOutline)
            {
                W16(hhea, 12, minLsb); W16(hhea, 14, minRsb); W16(hhea, 16, maxExtent);
            }
            W16(hhea, 34, _glyphs.Count);
            tables["hhea"] = hhea;

            byte[] maxp = (byte[])_tables["maxp"].Clone();
            W16(maxp, 4, _glyphs.Count);
            if (maxp.Length >= 10 && U32(maxp, 0) == 0x00010000)
            {
                W16(maxp, 6, Math.Max(U16(maxp, 6), maxPoints));
                W16(maxp, 8, Math.Max(U16(maxp, 8), maxContours));
            }
            tables["maxp"] = maxp;

            tables["cmap"] = BuildCmap();
            if (tables.ContainsKey("post"))
                tables["post"] = BuildPost(glyphsAdded);

            if (tables.TryGetValue("OS/2", out byte[]? os2) && os2.Length >= 68)
            {
                os2 = (byte[])os2.Clone();
                var bmp = _cmap.Keys.Where(c => c <= 0xFFFF).DefaultIfEmpty(0).ToList();
                W16(os2, 64, bmp.Min());
                W16(os2, 66, _cmap.Keys.Any(c => c > 0xFFFF) ? 0xFFFF : bmp.Max());
                tables["OS/2"] = os2;
            }

            byte[] file = Assemble(tables);
            uint sum = Checksum(file);
            int headOffset = TableOffset(file, "head");
            BinaryPrimitives.WriteUInt32BigEndian(file.AsSpan(headOffset + 8), unchecked(0xB1B0AFBA - sum));
            return file;
        }

        private static byte[] Assemble(SortedDictionary<string, byte[]> tables)
        {
            int count = tables.Count;
            int searchRange = 1, entrySelector = 0;
            while (searchRange * 2 <= count) { searchRange *= 2; entrySelector++; }
            searchRange *= 16;

            using var ms = new MemoryStream();
            var header = new byte[12 + count * 16];
            BinaryPrimitives.WriteUInt32BigEndian(header, 0x00010000);
            W16(header, 4, count); W16(header, 6, searchRange); W16(header, 8, entrySelector); W16(header, 10, count * 16 - searchRange);
            ms.Write(header);
            int index = 0;
            foreach (var (tag, data) in tables)
            {
                int rec = 12 + index++ * 16;
                Encoding.ASCII.GetBytes(tag.PadRight(4)).CopyTo(header, rec);
                BinaryPrimitives.WriteUInt32BigEndian(header.AsSpan(rec + 4), Checksum(data));
                BinaryPrimitives.WriteUInt32BigEndian(header.AsSpan(rec + 8), (uint)ms.Position);
                BinaryPrimitives.WriteUInt32BigEndian(header.AsSpan(rec + 12), (uint)data.Length);
                ms.Write(data);
                while (ms.Position % 4 != 0)
                    ms.WriteByte(0);
            }
            byte[] file = ms.ToArray();
            header.CopyTo(file, 0);
            return file;
        }

        private static int TableOffset(byte[] file, string tag)
        {
            int count = U16(file, 4);
            for (int i = 0; i < count; i++)
                if (Encoding.ASCII.GetString(file, 12 + i * 16, 4) == tag)
                    return (int)U32(file, 12 + i * 16 + 8);
            throw new InvalidOperationException($"Table '{tag}' not written.");
        }

        /// <summary>TrueType checksum: sum of big-endian 32-bit words, the data zero-padded to 4 bytes.</summary>
        internal static uint Checksum(byte[] data)
        {
            uint sum = 0;
            int full = data.Length & ~3;
            for (int i = 0; i < full; i += 4)
                sum = unchecked(sum + BinaryPrimitives.ReadUInt32BigEndian(data.AsSpan(i)));
            if (full < data.Length)
            {
                Span<byte> last = stackalloc byte[4];
                data.AsSpan(full).CopyTo(last);
                sum = unchecked(sum + BinaryPrimitives.ReadUInt32BigEndian(last));
            }
            return sum;
        }

        /// <summary>
        /// Character map with the subtables FreeType and Windows use: Unicode BMP (format 4), full Unicode
        /// (format 12) if the font had one or needs one, and the Mac Roman table if the font had one.
        /// </summary>
        private byte[] BuildCmap()
        {
            byte[] format4 = BuildFormat4(_cmap.Where(m => m.Key < 0xFFFF).OrderBy(m => m.Key).ToList());
            bool needsFull = _hasFullUnicodeCmap || _cmap.Keys.Any(c => c > 0xFFFF);
            byte[]? format12 = needsFull ? BuildFormat12(_cmap.OrderBy(m => m.Key).ToList()) : null;
            byte[]? mac = _hasMacCmap && !IsSymbolFont ? BuildMacRoman() : null;

            var subtables = new List<(int Platform, int Encoding, byte[] Data)>();
            if (IsSymbolFont)
            {
                subtables.Add((3, 0, format4));
            }
            else
            {
                subtables.Add((0, 3, format4));
                if (format12 != null) subtables.Add((0, 4, format12));
                if (mac != null) subtables.Add((1, 0, mac));
                subtables.Add((3, 1, format4));
                if (format12 != null) subtables.Add((3, 10, format12));
            }

            using var ms = new MemoryStream();
            var header = new byte[4 + subtables.Count * 8];
            W16(header, 2, subtables.Count);
            ms.Write(header);
            var written = new Dictionary<byte[], int>(ReferenceEqualityComparer.Instance);
            for (int i = 0; i < subtables.Count; i++)
            {
                var (platform, encoding, data) = subtables[i];
                if (!written.TryGetValue(data, out int offset))
                {
                    offset = (int)ms.Position;
                    ms.Write(data);
                    written[data] = offset;
                }
                W16(header, 4 + i * 8, platform);
                W16(header, 6 + i * 8, encoding);
                BinaryPrimitives.WriteUInt32BigEndian(header.AsSpan(8 + i * 8), (uint)offset);
            }
            byte[] result = ms.ToArray();
            header.CopyTo(result, 0);
            return result;
        }

        /// <summary>
        /// Format 4: one segment per run of consecutive codes; runs with consecutive glyphs use a delta,
        /// others the glyph id array.
        /// </summary>
        private static byte[] BuildFormat4(List<KeyValuePair<int, int>> map)
        {
            var segments = new List<(int Start, int End, List<int> Glyphs)>();
            foreach (var (code, glyph) in map)
            {
                if (segments.Count > 0 && segments[^1].End == code - 1)
                {
                    var s = segments[^1];
                    s.Glyphs.Add(glyph);
                    segments[^1] = (s.Start, code, s.Glyphs);
                }
                else
                    segments.Add((code, code, new List<int> { glyph }));
            }
            segments.Add((0xFFFF, 0xFFFF, new List<int> { 0 }));

            int segCount = segments.Count;
            var arrays = new List<int>();
            var deltas = new int[segCount];
            var rangeOffsets = new int[segCount];
            for (int i = 0; i < segCount; i++)
            {
                var (start, _, glyphs) = segments[i];
                if (start == 0xFFFF)
                    deltas[i] = 1;
                else if (glyphs.Select((g, k) => g - k).Distinct().Count() == 1)
                    deltas[i] = (glyphs[0] - start) & 0xFFFF;
                else
                {
                    rangeOffsets[i] = 2 * (segCount - i + arrays.Count);
                    arrays.AddRange(glyphs);
                }
            }

            int length = 16 + segCount * 8 + arrays.Count * 2;
            if (length > ushort.MaxValue)
                throw new AgsFormatException("The character map is too large for a format 4 table.");
            int searchRange = 1, entrySelector = 0;
            while (searchRange * 2 <= segCount) { searchRange *= 2; entrySelector++; }
            searchRange *= 2;

            var t = new byte[length];
            W16(t, 0, 4); W16(t, 2, length);
            W16(t, 6, segCount * 2); W16(t, 8, searchRange); W16(t, 10, entrySelector); W16(t, 12, segCount * 2 - searchRange);
            int ends = 14, starts = ends + segCount * 2 + 2, deltaAt = starts + segCount * 2, rangeAt = deltaAt + segCount * 2;
            for (int i = 0; i < segCount; i++)
            {
                W16(t, ends + i * 2, segments[i].End);
                W16(t, starts + i * 2, segments[i].Start);
                W16(t, deltaAt + i * 2, deltas[i]);
                W16(t, rangeAt + i * 2, rangeOffsets[i]);
            }
            for (int i = 0; i < arrays.Count; i++)
                W16(t, rangeAt + segCount * 2 + i * 2, arrays[i]);
            return t;
        }

        private static byte[] BuildFormat12(List<KeyValuePair<int, int>> map)
        {
            var groups = new List<(int Start, int End, int Glyph)>();
            foreach (var (code, glyph) in map)
            {
                if (groups.Count > 0 && groups[^1].End == code - 1 && groups[^1].Glyph + (code - groups[^1].Start) == glyph)
                    groups[^1] = (groups[^1].Start, code, groups[^1].Glyph);
                else
                    groups.Add((code, code, glyph));
            }
            var t = new byte[16 + groups.Count * 12];
            W16(t, 0, 12);
            BinaryPrimitives.WriteUInt32BigEndian(t.AsSpan(4), (uint)t.Length);
            BinaryPrimitives.WriteUInt32BigEndian(t.AsSpan(12), (uint)groups.Count);
            for (int i = 0; i < groups.Count; i++)
            {
                BinaryPrimitives.WriteUInt32BigEndian(t.AsSpan(16 + i * 12), (uint)groups[i].Start);
                BinaryPrimitives.WriteUInt32BigEndian(t.AsSpan(20 + i * 12), (uint)groups[i].End);
                BinaryPrimitives.WriteUInt32BigEndian(t.AsSpan(24 + i * 12), (uint)groups[i].Glyph);
            }
            return t;
        }

        /// <summary>Mac Roman table (format 6) derived from the Unicode map, so both agree.</summary>
        private byte[] BuildMacRoman()
        {
            Encoding macRoman = AgsTextEncoding.ForCodepage(10000);
            var t = new byte[10 + 256 * 2];
            W16(t, 0, 6); W16(t, 2, t.Length); W16(t, 8, 256);
            for (int b = 0; b < 256; b++)
            {
                string s = macRoman.GetString(new[] { (byte)b });
                int glyph = s.Length == 1 ? GetGlyphIndex(s[0]) : 0;
                W16(t, 10 + b * 2, glyph);
            }
            return t;
        }

        /// <summary>Glyph names of a format 2 'post' table, or null for other formats.</summary>
        private List<string>? ReadPostNames()
        {
            if (!_tables.TryGetValue("post", out byte[]? post) || post.Length < 34 || U32(post, 0) != 0x00020000)
                return null;
            int count = U16(post, 32);
            var strings = new List<string>();
            int pos = 34 + count * 2;
            while (pos < post.Length)
            {
                int len = post[pos];
                if (pos + 1 + len > post.Length)
                    return null;
                strings.Add(Encoding.ASCII.GetString(post, pos + 1, len));
                pos += 1 + len;
            }
            var names = new List<string>();
            for (int i = 0; i < count && i < _glyphs.Count; i++)
            {
                int index = U16(post, 34 + i * 2);
                if (index < 258)
                    names.Add(MacGlyphNames.Standard[index]);
                else if (index - 258 < strings.Count)
                    names.Add(strings[index - 258]);
                else
                    return null;
            }
            while (names.Count < _glyphs.Count)
                names.Add(UniqueName(-1, names));
            return names;
        }

        private string UniqueName(int codePoint, List<string>? names = null)
        {
            names ??= _glyphNames!;
            string name = codePoint < 0 ? $"glyph{names.Count}" : codePoint <= 0xFFFF ? $"uni{codePoint:X4}" : $"u{codePoint:X}";
            string candidate = name;
            for (int n = 1; names.Contains(candidate); n++)
                candidate = $"{name}.{n}";
            return candidate;
        }

        /// <summary>
        /// 'post' table: glyph names are kept (format 2, names of added glyphs appended); other formats
        /// switch to format 3 (no names) when glyphs are added, as format 1 lists exactly the standard glyphs.
        /// </summary>
        private byte[] BuildPost(bool glyphsAdded)
        {
            byte[] original = _tables["post"];
            if (_glyphNames == null)
            {
                if (!glyphsAdded || U32(original, 0) == 0x00030000)
                    return original;
                byte[] format3 = original.AsSpan(0, Math.Min(32, original.Length)).ToArray();
                Array.Resize(ref format3, 32);
                BinaryPrimitives.WriteUInt32BigEndian(format3, 0x00030000);
                return format3;
            }

            using var ms = new MemoryStream();
            ms.Write(original, 0, 32);
            var counts = new byte[2];
            BinaryPrimitives.WriteUInt16BigEndian(counts, (ushort)_glyphNames.Count);
            ms.Write(counts);
            var standard = new Dictionary<string, int>(StringComparer.Ordinal);
            for (int i = 0; i < MacGlyphNames.Standard.Length; i++)
                standard.TryAdd(MacGlyphNames.Standard[i], i);
            var custom = new List<string>();
            foreach (string name in _glyphNames)
            {
                int index;
                if (!standard.TryGetValue(name, out index))
                {
                    index = 258 + custom.Count;
                    custom.Add(name);
                }
                BinaryPrimitives.WriteUInt16BigEndian(counts, (ushort)index);
                ms.Write(counts);
            }
            foreach (string name in custom)
            {
                byte[] bytes = Encoding.ASCII.GetBytes(name.Length > 63 ? name[..63] : name);
                ms.WriteByte((byte)bytes.Length);
                ms.Write(bytes);
            }
            return ms.ToArray();
        }

        private static void W16(byte[] data, int offset, int value) =>
            BinaryPrimitives.WriteUInt16BigEndian(data.AsSpan(offset), unchecked((ushort)value));
        private static ushort U16(byte[] data, int offset) => BinaryPrimitives.ReadUInt16BigEndian(data.AsSpan(offset, 2));
        private static short I16(byte[] data, int offset) => BinaryPrimitives.ReadInt16BigEndian(data.AsSpan(offset, 2));
        private static uint U32(byte[] data, int offset) => BinaryPrimitives.ReadUInt32BigEndian(data.AsSpan(offset, 4));
    }

    /// <summary>The 258 standard Macintosh glyph names used by 'post' tables (OpenType specification).</summary>
    internal static class MacGlyphNames
    {
        public static readonly string[] Standard = (
            ".notdef .null nonmarkingreturn space exclam quotedbl numbersign dollar percent ampersand quotesingle " +
            "parenleft parenright asterisk plus comma hyphen period slash zero one two three four five six seven eight " +
            "nine colon semicolon less equal greater question at A B C D E F G H I J K L M N O P Q R S T U V W X Y Z " +
            "bracketleft backslash bracketright asciicircum underscore grave a b c d e f g h i j k l m n o p q r s t u " +
            "v w x y z braceleft bar braceright asciitilde Adieresis Aring Ccedilla Eacute Ntilde Odieresis Udieresis " +
            "aacute agrave acircumflex adieresis atilde aring ccedilla eacute egrave ecircumflex edieresis iacute " +
            "igrave icircumflex idieresis ntilde oacute ograve ocircumflex odieresis otilde uacute ugrave ucircumflex " +
            "udieresis dagger degree cent sterling section bullet paragraph germandbls registered copyright trademark " +
            "acute dieresis notequal AE Oslash infinity plusminus lessequal greaterequal yen mu partialdiff summation " +
            "product pi integral ordfeminine ordmasculine Omega ae oslash questiondown exclamdown logicalnot radical " +
            "florin approxequal Delta guillemotleft guillemotright ellipsis nonbreakingspace Agrave Atilde Otilde OE oe " +
            "endash emdash quotedblleft quotedblright quoteleft quoteright divide lozenge ydieresis Ydieresis fraction " +
            "currency guilsinglleft guilsinglright fi fl daggerdbl periodcentered quotesinglbase quotedblbase " +
            "perthousand Acircumflex Ecircumflex Aacute Edieresis Egrave Iacute Icircumflex Idieresis Igrave Oacute " +
            "Ocircumflex apple Ograve Uacute Ucircumflex Ugrave dotlessi circumflex tilde macron breve dotaccent ring " +
            "cedilla hungarumlaut ogonek caron Lslash lslash Scaron scaron Zcaron zcaron brokenbar Eth eth Yacute " +
            "yacute Thorn thorn minus multiply onesuperior twosuperior threesuperior onehalf onequarter threequarters " +
            "franc Gbreve gbreve Idotaccent Scedilla scedilla Cacute cacute Ccaron ccaron dcroat").Split(' ');
    }
}
