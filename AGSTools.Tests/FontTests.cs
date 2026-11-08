using System.Buffers.Binary;
using System.Text;

namespace AGSTools.Tests;

public class FontTests
{
    /// <summary>
    /// Builds a minimal TrueType file: cmap (format 4), glyf/loca with opaque glyph data, head, maxp and name.
    /// </summary>
    public static byte[] BuildTtf(string name, params (char Char, byte[] Glyph)[] glyphs)
    {
        static byte[] U16(params int[] values) => values.SelectMany(v => new[] { (byte)(v >> 8), (byte)v }).ToArray();

        // Glyph 0 is the empty "missing glyph"; data is padded to even length for the short loca format
        var glyf = new List<byte>();
        var loca = new List<int> { 0, 0 };
        foreach (var (_, data) in glyphs)
        {
            glyf.AddRange(data);
            if (glyf.Count % 2 != 0) glyf.Add(0);
            loca.Add(glyf.Count);
        }

        var sorted = glyphs.Select((g, i) => (Code: (int)g.Char, Glyph: i + 1)).OrderBy(g => g.Code).ToList();
        int segCount = sorted.Count + 1;
        var ends = sorted.Select(g => g.Code).Append(0xFFFF).ToArray();
        var deltas = sorted.Select(g => (g.Glyph - g.Code) & 0xFFFF).Append(1).ToArray();
        byte[] format4 = U16(4, 16 + segCount * 8, 0, segCount * 2, 0, 0, 0)
            .Concat(U16(ends)).Concat(U16(0)).Concat(U16(ends)).Concat(U16(deltas)).Concat(U16(new int[segCount])).ToArray();
        byte[] cmap = U16(0, 1, 3, 1).Concat(new byte[] { 0, 0, 0, 12 }).Concat(format4).ToArray();

        byte[] head = new byte[54];
        BinaryPrimitives.WriteUInt16BigEndian(head.AsSpan(18), 1000);
        byte[] maxp = new byte[] { 0, 0, 0x50, 0 }.Concat(U16(glyphs.Length + 1)).ToArray();
        byte[] nameBytes = Encoding.BigEndianUnicode.GetBytes(name);
        byte[] nameTable = U16(0, 1, 18, 3, 1, 0x409, 4, nameBytes.Length, 0).Concat(nameBytes).ToArray();

        var tables = new SortedDictionary<string, byte[]>(StringComparer.Ordinal)
        {
            ["cmap"] = cmap, ["glyf"] = glyf.ToArray(), ["head"] = head,
            ["loca"] = U16(loca.Select(o => o / 2).ToArray()), ["maxp"] = maxp, ["name"] = nameTable,
        };
        var file = new List<byte>(new byte[] { 0, 1, 0, 0 });
        file.AddRange(U16(tables.Count, 0, 0, 0));
        int offset = 12 + tables.Count * 16;
        var body = new List<byte>();
        foreach (var (tag, data) in tables)
        {
            file.AddRange(Encoding.ASCII.GetBytes(tag));
            file.AddRange(new byte[4]); // checksum, not verified
            file.AddRange(U16((offset + body.Count) >> 16, (offset + body.Count) & 0xFFFF, data.Length >> 16, data.Length & 0xFFFF));
            body.AddRange(data);
            while (body.Count % 4 != 0) body.Add(0);
        }
        file.AddRange(body);
        return file.ToArray();
    }

    private static readonly byte[] OutlineA = { 1, 2, 3 }, OutlineO = { 4, 5, 6, 7 }, OutlineODots = { 4, 5, 6, 8 };

    private static GameFont TestTtf() => GameFont.FromData("agsfnt3.ttf", BuildTtf("Test Font",
        ('a', OutlineA), ('ä', OutlineA), ('o', OutlineO), ('ö', OutlineODots), ('s', new byte[] { 9 }), ('"', new byte[] { 10 })));

    private static WfnGlyph Glyph(int width, int height, params (int X, int Y)[] pixels)
    {
        var glyph = new WfnGlyph(width, height);
        foreach (var (x, y) in pixels)
            glyph[x, y] = true;
        return glyph;
    }

    [Fact]
    public void Ttf_character_map_names_and_outlines_are_read()
    {
        TtfFont ttf = TestTtf().Ttf!;

        Assert.Equal("Test Font", ttf.Name);
        Assert.Equal(1000, ttf.UnitsPerEm);
        Assert.True(ttf.HasGlyph('a'));
        Assert.True(ttf.HasGlyph('ö'));
        Assert.False(ttf.HasGlyph('ß'));
        Assert.True(ttf.HasSameOutline('ä', 'a'));
        Assert.False(ttf.HasSameOutline('ö', 'o'));
    }

    [Fact]
    public void Wfn_roundtrip_shares_identical_glyphs_and_avoids_zero_offsets()
    {
        var font = new WfnFont();
        font.SetGlyph('A', Glyph(5, 7, (0, 0), (4, 6)));
        font.SetGlyph('B', Glyph(5, 7, (0, 0), (4, 6)));
        font.SetGlyph(228, Glyph(7, 8, (1, 0), (2, 0), (6, 7)));
        byte[] data = font.ToArray();

        WfnFont read = WfnFont.Read(data);

        Assert.Empty(read.Problems);
        Assert.Equal(229, read.Glyphs.Count);
        Assert.Equal(font.Glyphs[228], read.Glyphs[228]);
        Assert.True(read.HasGlyph('A'));
        Assert.False(read.HasGlyph('C'));
        Assert.Equal(8, read.Height);
        int table = BinaryPrimitives.ReadUInt16LittleEndian(data.AsSpan(15));
        var offsets = Enumerable.Range(0, 229).Select(i => BinaryPrimitives.ReadUInt16LittleEndian(data.AsSpan(table + i * 2))).ToList();
        Assert.DoesNotContain((ushort)0, offsets);
        Assert.Equal(offsets['A'], offsets['B']);
    }

    [Fact]
    public void Wfn_reader_tolerates_missing_and_bad_offsets_like_the_engine()
    {
        var font = new WfnFont();
        font.SetGlyph('A', Glyph(2, 2, (1, 1)));
        font.SetGlyph('D', Glyph(2, 2, (0, 0)));
        byte[] data = font.ToArray();
        int table = BinaryPrimitives.ReadUInt16LittleEndian(data.AsSpan(15));
        BinaryPrimitives.WriteUInt16LittleEndian(data.AsSpan(table + 'B' * 2), 0);      // no glyph
        BinaryPrimitives.WriteUInt16LittleEndian(data.AsSpan(table + 'C' * 2), 60000);  // outside the data

        WfnFont read = WfnFont.Read(data);

        Assert.True(read.HasGlyph('A'));
        Assert.Null(read.Glyphs['B']);
        Assert.Null(read.Glyphs['C']);
        Assert.Single(read.Problems);
    }

    /// <summary>A shaded (4-bit) WFN file built by hand from the format description of AGS 3.6.4.</summary>
    private static byte[] ShadedWfnFile(int flags = 0, int bitsPerPixel = 4, int baseShade = 6)
    {
        static byte[] U16(params int[] v) => v.SelectMany(x => BitConverter.GetBytes((ushort)x)).ToArray();
        static byte[] U32(params int[] v) => v.SelectMany(BitConverter.GetBytes).ToArray();
        // Glyph 0 (offset 0): 3x2, shades 1 2 3 / 15 0 8; rows are 2 bytes, the high nibble is the left pixel
        // Glyph 1 (offset 8): 1x1, shade 9
        byte[] glyphs = U16(3, 2).Concat(new byte[] { 0x12, 0x30, 0xF0, 0x80 }).Concat(U16(1, 1)).Concat(new byte[] { 0x90 }).ToArray();
        int glyphAddr = 15 + 32, charAddr = glyphAddr + glyphs.Length;
        byte[] chars = U32('B', 8, 'A', 0, 300, 0); // unsorted, two codes share glyph 0
        return Encoding.ASCII.GetBytes("WGT Font File 2")
            .Concat(U32(32, flags, bitsPerPixel, glyphAddr, glyphs.Length, charAddr, chars.Length, baseShade))
            .Concat(glyphs).Concat(chars).ToArray();
    }

    [Fact]
    public void Shaded_wfn_is_read_with_nibbles_left_pixel_first_and_a_sparse_character_table()
    {
        WfnFont font = WfnFont.Read(ShadedWfnFile());

        Assert.Empty(font.Problems);
        Assert.True(font.IsShaded);
        Assert.Equal(6, font.BaseShade);
        Assert.Equal(301, font.Glyphs.Count);
        WfnGlyph a = font.GetGlyph('A')!;
        Assert.Equal(new[] { 1, 2, 3, 15, 0, 8 }, new[] { a.GetShade(0, 0), a.GetShade(1, 0), a.GetShade(2, 0), a.GetShade(0, 1), a.GetShade(1, 1), a.GetShade(2, 1) });
        Assert.Equal(a, font.GetGlyph(300));
        Assert.Equal(9, font.GetGlyph('B')!.GetShade(0, 0));
        Assert.Null(font.GetGlyph('C'));
        Assert.Equal(2, font.Height);
    }

    [Fact]
    public void Shaded_wfn_roundtrips_and_shares_glyphs()
    {
        WfnFont font = WfnFont.Read(ShadedWfnFile());
        byte[] data = font.ToArray();

        WfnFont read = WfnFont.Read(data);

        Assert.Equal("WGT Font File 2", Encoding.ASCII.GetString(data, 0, 15));
        Assert.Equal(6, read.BaseShade);
        Assert.Equal(font.Glyphs, read.Glyphs);
        // Three characters, two glyphs: header + 2 glyphs (8 + 5 bytes) + 3 table entries
        Assert.Equal(15 + 32 + 13 + 3 * 8, data.Length);
    }

    [Theory]
    [InlineData(1, 4)]
    [InlineData(0, 8)]
    public void Shaded_wfn_with_unknown_flags_or_depth_is_rejected_like_the_engine(int flags, int bitsPerPixel)
    {
        Assert.Throws<AgsFormatException>(() => WfnFont.Read(ShadedWfnFile(flags, bitsPerPixel)));
    }

    [Fact]
    public void Converting_between_monochrome_and_shades_keeps_the_drawn_pixels()
    {
        var font = new WfnFont();
        font.SetGlyph('A', Glyph(3, 2, (0, 0), (2, 1)));
        font.BaseShade = 5;

        font.SetBitsPerPixel(4);
        WfnGlyph shaded = font.GetGlyph('A')!;
        Assert.Equal((4, 5, 0), (shaded.BitsPerPixel, shaded.GetShade(0, 0), shaded.GetShade(1, 0)));
        font.SetGlyph('B', Glyph(1, 1, (0, 0)));                 // a monochrome glyph is converted on the way in
        Assert.Equal(5, font.GetGlyph('B')!.GetShade(0, 0));

        shaded.SetShade(1, 0, 12);
        font.SetGlyph('A', shaded);
        font.SetBitsPerPixel(1);
        Assert.Equal(Glyph(3, 2, (0, 0), (1, 0), (2, 1)), font.GetGlyph('A'));
        Assert.Equal("WGT Font File  ", Encoding.ASCII.GetString(font.ToArray(), 0, 15));
    }

    [Fact]
    public void Shaded_glyph_pixels_can_be_set_resized_and_cleared()
    {
        var glyph = new WfnGlyph(3, 1, null, 4);
        glyph.SetShade(0, 0, 15);
        glyph.SetShade(1, 0, 3);
        glyph[2, 0] = true;                                      // a new pixel gets the default text color shade
        glyph[0, 0] = true;                                      // an existing one keeps its shade

        Assert.Equal(new byte[] { 0xF3, 0x80 }, glyph.ToPacked());
        WfnGlyph larger = glyph.Resize(4, 2);
        Assert.Equal((15, 3, 8, 0), (larger.GetShade(0, 0), larger.GetShade(1, 0), larger.GetShade(2, 0), larger.GetShade(3, 0)));
        glyph[1, 0] = false;
        Assert.Equal(0, glyph.GetShade(1, 0));
        Assert.Throws<ArgumentOutOfRangeException>(() => glyph.SetShade(0, 0, 16));
    }

    [Fact]
    public void Glyph_editing_and_resizing_keeps_pixels()
    {
        WfnGlyph glyph = Glyph(9, 3, (8, 2), (0, 0));
        WfnGlyph larger = glyph.Resize(12, 4);

        Assert.True(larger[8, 2] && larger[0, 0]);
        Assert.False(larger[11, 3]);
        Assert.Equal(new byte[] { 0x80, 0, 0, 0, 0, 0x80 }, glyph.ToPacked());
    }

    [Fact]
    public void Coverage_reports_missing_glyphs_and_umlauts_without_dots()
    {
        var doc = new TranslationDocument();
        doc.Options.Encoding = "UTF-8";
        doc.Entries.Add("Greetings", "Grüße, „Dämon“! Schön.​");

        var issues = FontCoverage.FindIssues(doc, new[] { TestTtf() });

        // ü is skipped (the font has no "u", so it is not used for such text), ö has its own glyph
        Assert.Equal(new[] { "ß", "“", "„", "ä" }, issues.Select(i => i.Character).OrderBy(c => c == "ä").ToArray());
        Assert.True(issues.Single(i => i.Character == "ä").LooksLikeBaseLetter);
        Assert.Equal(0x201E, issues.Single(i => i.Character == "„").Code);
    }

    [Fact]
    public void Engine_code_depends_on_the_translation_encoding()
    {
        var options = new TranslationOptions { AnsiCodepage = 1252 };
        Assert.Equal(0x84, FontCoverage.EngineCode("„", options));
        Assert.Equal(0xE4, FontCoverage.EngineCode("ä", options));
        Assert.Equal(-1, FontCoverage.EngineCode("ł", options));

        options.Encoding = "UTF-8";
        Assert.Equal(0x201E, FontCoverage.EngineCode("„", options));
    }

    [Fact]
    public void Wfn_font_cannot_show_characters_beyond_its_table_in_a_utf8_translation()
    {
        var wfn = new WfnFont();
        wfn.SetGlyph('"', Glyph(3, 3, (0, 0)));
        wfn.SetGlyph('s', Glyph(3, 3, (1, 1)));
        wfn.SetGlyph(0xDF, Glyph(3, 3, (2, 2)));
        var doc = new TranslationDocument();
        doc.Options.Encoding = "UTF-8";
        doc.Entries.Add("x", "„groß“");

        var warnings = FontCoverage.Check(doc, new[] { GameFont.FromData("agsfnt2.wfn", wfn.ToArray()) });

        Assert.StartsWith("Font 2 (agsfnt2.wfn): no glyph for “ (1×), „ (1×)", warnings[0]);
    }

    [Fact]
    public void Eight_bit_translation_gets_a_hint_about_utf8()
    {
        var doc = new TranslationDocument();
        doc.Options.AnsiCodepage = 1252;
        doc.Entries.Add("x", "„Hallo“");

        var warnings = FontCoverage.Check(doc, new[] { TestTtf() });

        Assert.Contains(warnings, w => w.Contains("code 132"));
        Assert.Contains(warnings, w => w.Contains("UTF-8 translation"));
    }
}
