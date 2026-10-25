using System.Buffers.Binary;
using System.Text;

namespace AGSTools.Tests;

/// <summary>
/// Builds a small but complete TrueType pixel font (head, hhea, maxp, hmtx, loca, glyf, cmap with
/// Unicode and Mac subtables, post format 2, name, OS/2) for tests. Each pixel is one square contour.
/// </summary>
internal sealed class PixelFontBuilder
{
    public const int UnitsPerEm = 1024, Grid = 64, Ascender = 768, Descender = -256;

    private readonly List<(string Name, byte[] Data, int Advance, int Lsb)> _glyphs = new();
    private readonly List<(int Code, int Glyph)> _cmap = new();

    public PixelFontBuilder()
    {
        // .notdef: a 4x8 box outline
        AddGlyph(".notdef", new[] { "####", "#..#", "#..#", "#..#", "#..#", "#..#", "#..#", "####" }, topRow: 8);
    }

    /// <summary>Adds a glyph drawn by rows of '#' (ink) and '.'; the first row's top edge is <paramref name="topRow"/> pixels above the baseline.</summary>
    public int AddGlyph(string name, string[] rows, int topRow = 8, params int[] codes)
    {
        var squares = new List<(int X, int Y)>();
        for (int r = 0; r < rows.Length; r++)
            for (int c = 0; c < rows[r].Length; c++)
                if (rows[r][c] == '#')
                    squares.Add((c, topRow - r - 1));
        int width = rows.Max(r => r.Length);
        int index = _glyphs.Count;
        _glyphs.Add((name, SimpleGlyph(squares), width * Grid, squares.Count == 0 ? 0 : squares.Min(s => s.X) * Grid));
        foreach (int code in codes)
            _cmap.Add((code, index));
        return index;
    }

    /// <summary>Adds a composite glyph made of one component.</summary>
    public int AddComposite(string name, int component, int dx, int dy, params int[] codes)
    {
        var data = new List<byte>();
        var bounds = _glyphs[component].Data.AsSpan(2, 8).ToArray();
        data.AddRange(U16(0xFFFF)); // numberOfContours = -1
        for (int i = 0; i < 4; i++)
            data.AddRange(U16(BinaryPrimitives.ReadInt16BigEndian(bounds.AsSpan(i * 2)) + (i % 2 == 0 ? dx : dy)));
        data.AddRange(U16(0x0001 | 0x0002)); // ARG_1_AND_2_ARE_WORDS | ARGS_ARE_XY_VALUES
        data.AddRange(U16(component));
        data.AddRange(U16(dx));
        data.AddRange(U16(dy));
        int index = _glyphs.Count;
        _glyphs.Add((name, data.ToArray(), _glyphs[component].Advance, _glyphs[component].Lsb + dx));
        foreach (int code in codes)
            _cmap.Add((code, index));
        return index;
    }

    /// <summary>A simple glyph with one clockwise square per pixel, coordinates as words (no compression).</summary>
    private static byte[] SimpleGlyph(List<(int X, int Y)> squares)
    {
        if (squares.Count == 0)
            return Array.Empty<byte>();
        var points = new List<(int X, int Y)>();
        foreach (var (x, y) in squares)
        {
            int x0 = x * Grid, y0 = y * Grid, x1 = x0 + Grid, y1 = y0 + Grid;
            points.AddRange(new[] { (x0, y0), (x0, y1), (x1, y1), (x1, y0) });
        }
        var data = new List<byte>();
        data.AddRange(U16(squares.Count));
        data.AddRange(U16(points.Min(p => p.X), points.Min(p => p.Y), points.Max(p => p.X), points.Max(p => p.Y)));
        for (int i = 0; i < squares.Count; i++)
            data.AddRange(U16(i * 4 + 3));
        data.AddRange(U16(0));
        data.AddRange(Enumerable.Repeat((byte)1, points.Count)); // on curve, word deltas
        int last = 0;
        foreach (var p in points) { data.AddRange(U16(p.X - last)); last = p.X; }
        last = 0;
        foreach (var p in points) { data.AddRange(U16(p.Y - last)); last = p.Y; }
        return data.ToArray();
    }

    public byte[] Build()
    {
        var glyf = new List<byte>();
        var loca = new List<int>();
        foreach (var g in _glyphs)
        {
            loca.Add(glyf.Count);
            glyf.AddRange(g.Data);
            if (glyf.Count % 2 != 0) glyf.Add(0);
        }
        loca.Add(glyf.Count);

        // The last glyph shares the advance of the one before it (short hmtx tail)
        int hMetrics = _glyphs.Count - 1;
        var hmtx = new List<byte>();
        for (int i = 0; i < _glyphs.Count; i++)
        {
            if (i < hMetrics) hmtx.AddRange(U16(_glyphs[i].Advance));
            hmtx.AddRange(U16(_glyphs[i].Lsb));
        }
        if (_glyphs[^1].Advance != _glyphs[^2].Advance)
            throw new InvalidOperationException("The last two glyphs must have the same advance.");

        byte[] head = new byte[54];
        BinaryPrimitives.WriteUInt32BigEndian(head, 0x00010000);
        BinaryPrimitives.WriteUInt32BigEndian(head.AsSpan(12), 0x5F0F3CF5);
        BinaryPrimitives.WriteUInt16BigEndian(head.AsSpan(18), UnitsPerEm);
        BinaryPrimitives.WriteInt64BigEndian(head.AsSpan(20), 3_700_000_000); // created/modified: 2021
        BinaryPrimitives.WriteInt64BigEndian(head.AsSpan(28), 3_700_000_000);
        BinaryPrimitives.WriteInt16BigEndian(head.AsSpan(38), Descender);
        BinaryPrimitives.WriteInt16BigEndian(head.AsSpan(40), 1024);
        BinaryPrimitives.WriteInt16BigEndian(head.AsSpan(42), Ascender);
        BinaryPrimitives.WriteInt16BigEndian(head.AsSpan(48), 2);

        byte[] hhea = new byte[36];
        BinaryPrimitives.WriteUInt32BigEndian(hhea, 0x00010000);
        BinaryPrimitives.WriteInt16BigEndian(hhea.AsSpan(4), Ascender);
        BinaryPrimitives.WriteInt16BigEndian(hhea.AsSpan(6), Descender);
        BinaryPrimitives.WriteInt16BigEndian(hhea.AsSpan(18), 1);
        BinaryPrimitives.WriteUInt16BigEndian(hhea.AsSpan(34), (ushort)hMetrics);

        byte[] maxp = new byte[32];
        BinaryPrimitives.WriteUInt32BigEndian(maxp, 0x00010000);
        BinaryPrimitives.WriteUInt16BigEndian(maxp.AsSpan(4), (ushort)_glyphs.Count);
        BinaryPrimitives.WriteUInt16BigEndian(maxp.AsSpan(6), 200);
        BinaryPrimitives.WriteUInt16BigEndian(maxp.AsSpan(8), 50);
        BinaryPrimitives.WriteUInt16BigEndian(maxp.AsSpan(14), 2);
        BinaryPrimitives.WriteUInt16BigEndian(maxp.AsSpan(28), 1);
        BinaryPrimitives.WriteUInt16BigEndian(maxp.AsSpan(30), 1);

        var tables = new SortedDictionary<string, byte[]>(StringComparer.Ordinal)
        {
            ["OS/2"] = new byte[78],
            ["cmap"] = Cmap(),
            ["glyf"] = glyf.ToArray(),
            ["head"] = head,
            ["hhea"] = hhea,
            ["hmtx"] = hmtx.ToArray(),
            ["loca"] = U16(loca.Select(o => o / 2).ToArray()),
            ["maxp"] = maxp,
            ["name"] = Name("Test Pixel"),
            ["post"] = Post(),
        };
        return Assemble(tables);
    }

    private byte[] Cmap()
    {
        var map = _cmap.OrderBy(m => m.Code).ToList();
        // Format 4 with one segment per code
        int segCount = map.Count + 1;
        var ends = map.Select(m => m.Code).Append(0xFFFF).ToArray();
        var deltas = map.Select(m => (m.Glyph - m.Code) & 0xFFFF).Append(1).ToArray();
        byte[] format4 = U16(4, 16 + segCount * 8, 0, segCount * 2, 0, 0, 0)
            .Concat(U16(ends)).Concat(U16(0)).Concat(U16(ends)).Concat(U16(deltas)).Concat(U16(new int[segCount])).ToArray();
        // Mac Roman format 6 for ASCII
        var mac = new int[128];
        foreach (var (code, glyph) in map.Where(m => m.Code < 128))
            mac[code] = glyph;
        byte[] format6 = U16(6, 10 + 256, 0, 0, 128).Concat(U16(mac)).ToArray();

        int o4 = 4 + 3 * 8, o6 = o4 + format4.Length;
        return U16(0, 3)
            .Concat(U16(0, 3, 0, o4)).Concat(U16(1, 0, 0, o6)).Concat(U16(3, 1, 0, o4))
            .Concat(format4).Concat(format6).ToArray();
    }

    private byte[] Post()
    {
        var data = new List<byte>(new byte[32]);
        BinaryPrimitives.WriteUInt32BigEndian(System.Runtime.InteropServices.CollectionsMarshal.AsSpan(data), 0x00020000);
        data.AddRange(U16(_glyphs.Count));
        var custom = new List<string>();
        foreach (var g in _glyphs)
        {
            // A few standard Mac names, the rest custom
            int standard = g.Name switch { ".notdef" => 0, "space" => 3, "a" => 68, "o" => 82, _ => -1 };
            if (standard < 0)
            {
                standard = 258 + custom.Count;
                custom.Add(g.Name);
            }
            data.AddRange(U16(standard));
        }
        foreach (string name in custom)
        {
            data.Add((byte)name.Length);
            data.AddRange(Encoding.ASCII.GetBytes(name));
        }
        return data.ToArray();
    }

    private static byte[] Name(string name)
    {
        byte[] s = Encoding.BigEndianUnicode.GetBytes(name);
        return U16(0, 2, 6 + 24).Concat(U16(3, 1, 0x409, 1, s.Length, 0)).Concat(U16(3, 1, 0x409, 4, s.Length, 0)).Concat(s).ToArray();
    }

    private static byte[] Assemble(SortedDictionary<string, byte[]> tables)
    {
        var file = new List<byte>(U16(1, 0, tables.Count, 64, 2, tables.Count * 16 - 64));
        int offset = 12 + tables.Count * 16;
        var body = new List<byte>();
        foreach (var (tag, data) in tables)
        {
            file.AddRange(Encoding.ASCII.GetBytes(tag));
            file.AddRange(new byte[4]);
            int at = offset + body.Count;
            file.AddRange(U16(at >> 16, at & 0xFFFF, data.Length >> 16, data.Length & 0xFFFF));
            body.AddRange(data);
            while (body.Count % 4 != 0) body.Add(0);
        }
        file.AddRange(body);
        return file.ToArray();
    }

    private static byte[] U16(params int[] values) => values.SelectMany(v => new[] { (byte)(v >> 8), (byte)v }).ToArray();

    /// <summary>
    /// The standard test font: "a" (glyph 1), "o" with a hole, two pixels touching diagonally ("x"),
    /// "A" also mapped to U+00C4 (shared glyph), "ä" as a composite of "a", and a space.
    /// </summary>
    public static byte[] StandardFont()
    {
        var b = new PixelFontBuilder();
        b.AddGlyph("a", new[] { ".##.", "...#", ".###", "#..#", ".###" }, topRow: 5, 'a');
        b.AddGlyph("o", new[] { ".##.", "#..#", "#..#", "#..#", ".##." }, topRow: 5, 'o');
        b.AddGlyph("x", new[] { "#...", ".#..", "..#.", "...#" }, topRow: 4, 'x');
        b.AddGlyph("A", new[] { ".##.", "#..#", "#..#", "####", "#..#", "#..#", "#..#" }, topRow: 7, 'A', 0xC4);
        b.AddComposite("adieresis", 1, 0, 0, 0xE4);
        b.AddGlyph("space", new[] { "...." }, topRow: 1, ' ');
        return b.Build();
    }
}
