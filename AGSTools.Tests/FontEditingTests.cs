using System.Buffers.Binary;
using System.Diagnostics;
using System.Text;

namespace AGSTools.Tests;

/// <summary>A test that needs python3 with fontTools to validate written fonts.</summary>
public sealed class FontToolsFactAttribute : FactAttribute
{
    private static readonly Lazy<bool> Available = new(() => FontEditingTests.RunPython("import fontTools").ExitCode == 0);

    public FontToolsFactAttribute()
    {
        if (!Available.Value)
            Skip = "python3 with fontTools not found.";
    }
}

public class FontEditingTests
{
    private static WfnGlyph Bitmap(params string[] rows)
    {
        var glyph = new WfnGlyph(rows.Max(r => r.Length), rows.Length);
        for (int y = 0; y < rows.Length; y++)
            for (int x = 0; x < rows[y].Length; x++)
                glyph[x, y] = rows[y][x] == '#';
        return glyph;
    }

    private static string[] Rows(WfnGlyph glyph) =>
        Enumerable.Range(0, glyph.Height)
            .Select(y => new string(Enumerable.Range(0, glyph.Width).Select(x => glyph[x, y] ? '#' : '.').ToArray()))
            .ToArray();

    /// <summary>Rows of a glyph cropped to the rows that have ink.</summary>
    private static string[] InkRows(WfnGlyph glyph) => Rows(glyph).Where(r => r.Contains('#')).ToArray();

    private static readonly string[] Eszett = { ".##.", "#..#", "#.#.", "#..#", "#..#", "#.#." };

    private static void AssertValidChecksums(byte[] font)
    {
        // The whole file sums to the magic value, and every table matches its directory checksum
        uint sum = 0;
        for (int i = 0; i < font.Length; i += 4)
            sum = unchecked(sum + BinaryPrimitives.ReadUInt32BigEndian(font.AsSpan(i)));
        Assert.Equal(0xB1B0AFBAu, sum);
        int count = BinaryPrimitives.ReadUInt16BigEndian(font.AsSpan(4));
        for (int i = 0; i < count; i++)
        {
            int rec = 12 + i * 16;
            string tag = Encoding.ASCII.GetString(font, rec, 4);
            uint expected = BinaryPrimitives.ReadUInt32BigEndian(font.AsSpan(rec + 4));
            int offset = (int)BinaryPrimitives.ReadUInt32BigEndian(font.AsSpan(rec + 8));
            int length = (int)BinaryPrimitives.ReadUInt32BigEndian(font.AsSpan(rec + 12));
            Assert.Equal(0, offset % 4);
            byte[] table = font.AsSpan(offset, (length + 3) & ~3).ToArray();
            if (tag == "head")
                BinaryPrimitives.WriteUInt32BigEndian(table.AsSpan(8), 0);
            uint actual = 0;
            for (int k = 0; k < table.Length; k += 4)
                actual = unchecked(actual + BinaryPrimitives.ReadUInt32BigEndian(table.AsSpan(k)));
            Assert.True(expected == actual, $"Checksum of '{tag}'");
        }
    }

    [Fact]
    public void Unchanged_font_round_trips_with_the_same_glyphs()
    {
        byte[] original = PixelFontBuilder.StandardFont();
        TtfEditor editor = TtfEditor.Load(original);

        byte[] written = editor.ToArray();
        TtfEditor reread = TtfEditor.Load(written);

        Assert.False(editor.IsModified);
        AssertValidChecksums(written);
        Assert.Equal(editor.GlyphCount, reread.GlyphCount);
        Assert.Equal(editor.CodePoints.OrderBy(c => c), reread.CodePoints.OrderBy(c => c));
        foreach (int code in editor.CodePoints)
        {
            int glyph = editor.GetGlyphIndex(code);
            Assert.Equal(glyph, reread.GetGlyphIndex(code));
            Assert.Equal(editor.GetAdvance(glyph), reread.GetAdvance(glyph));
            Assert.Equal(editor.GetOutline(glyph).Contours.SelectMany(c => c), reread.GetOutline(glyph).Contours.SelectMany(c => c));
        }
        Assert.Equal("Test Pixel", TtfFont.Read(written).Name);
    }

    [Fact]
    public void Composite_glyphs_are_resolved_to_their_components()
    {
        TtfEditor editor = TtfEditor.Load(PixelFontBuilder.StandardFont());

        var a = editor.GetOutline(editor.GetGlyphIndex('a'));
        var aUmlaut = editor.GetOutline(editor.GetGlyphIndex('ä'));

        Assert.NotEqual(editor.GetGlyphIndex('a'), editor.GetGlyphIndex('ä'));
        Assert.Equal(a.Contours.SelectMany(c => c), aUmlaut.Contours.SelectMany(c => c));
    }

    [Fact]
    public void Added_glyphs_are_mapped_and_other_glyphs_stay_unchanged()
    {
        byte[] original = PixelFontBuilder.StandardFont();
        TtfEditor editor = TtfEditor.Load(original);
        TtfPixelGrid grid = TtfPixelGrid.Detect(editor)!;
        int glyphsBefore = editor.GlyphCount;

        editor.SetGlyph(0xDF, grid.ToOutline(Bitmap(Eszett).Resize(4, grid.Rows)), 5 * 64);
        editor.SetGlyph(0x201E, grid.ToOutline(Bitmap(".....", ".....", ".....", ".....", ".....", ".....", ".....", "#.#..", "#.#..")), 3 * 64);
        byte[] written = editor.ToArray();

        AssertValidChecksums(written);
        var font = TtfFont.Read(written);
        Assert.True(font.HasGlyph(0xDF));
        Assert.True(font.HasGlyph(0x201E));
        Assert.Equal(glyphsBefore + 2, font.GlyphCount);

        var reread = TtfEditor.Load(written);
        var before = TtfEditor.Load(original);
        foreach (int code in before.CodePoints)
        {
            Assert.Equal(before.GetGlyphIndex(code), reread.GetGlyphIndex(code));
            Assert.Equal(before.GetOutline(before.GetGlyphIndex(code)).Contours.SelectMany(c => c),
                reread.GetOutline(reread.GetGlyphIndex(code)).Contours.SelectMany(c => c));
        }
        int eszett = reread.GetGlyphIndex(0xDF);
        Assert.Equal(5 * 64, reread.GetAdvance(eszett));
        Assert.Equal(Eszett, InkRows(grid.Rasterize(reread, eszett)).Select(r => r[..4]).ToArray());
    }

    [Fact]
    public void Glyphs_shared_with_other_characters_are_not_changed_in_place()
    {
        TtfEditor editor = TtfEditor.Load(PixelFontBuilder.StandardFont());
        TtfPixelGrid grid = TtfPixelGrid.Detect(editor)!;
        int count = editor.GlyphCount;
        int a = editor.GetGlyphIndex('a'), upperA = editor.GetGlyphIndex('A');
        var aOutline = editor.GetOutline(a);
        var upperAOutline = editor.GetOutline(upperA);
        var dot = grid.ToOutline(Bitmap("#"));

        // "a" is a component of "ä", and U+00C4 shares the glyph of "A"
        int newA = editor.SetGlyph('a', dot, 64);
        int newAUmlaut = editor.SetGlyph(0xC4, dot, 64);
        var reread = TtfEditor.Load(editor.ToArray());

        Assert.Equal(count + 2, reread.GlyphCount);
        Assert.Equal(new[] { count, count + 1 }, new[] { newA, newAUmlaut });
        Assert.Equal(aOutline.Contours.SelectMany(c => c), reread.GetOutline(reread.GetGlyphIndex('ä')).Contours.SelectMany(c => c));
        Assert.Equal(upperAOutline.Contours.SelectMany(c => c), reread.GetOutline(reread.GetGlyphIndex('A')).Contours.SelectMany(c => c));
        Assert.Equal(dot.Contours.SelectMany(c => c), reread.GetOutline(reread.GetGlyphIndex(0xC4)).Contours.SelectMany(c => c));
    }

    [Fact]
    public void Unshared_glyph_is_replaced_in_place()
    {
        TtfEditor editor = TtfEditor.Load(PixelFontBuilder.StandardFont());
        TtfPixelGrid grid = TtfPixelGrid.Detect(editor)!;
        int count = editor.GlyphCount, o = editor.GetGlyphIndex('o');

        int glyph = editor.SetGlyph('o', grid.ToOutline(Bitmap("##", "##")), 3 * 64);
        var reread = TtfEditor.Load(editor.ToArray());

        Assert.Equal(o, glyph);
        Assert.Equal(count, reread.GlyphCount);
        Assert.Equal(3 * 64, reread.GetAdvance(o));
        Assert.True(editor.IsModified);
    }

    [Fact]
    public void Supplementary_code_points_get_a_full_unicode_character_map()
    {
        TtfEditor editor = TtfEditor.Load(PixelFontBuilder.StandardFont());
        TtfPixelGrid grid = TtfPixelGrid.Detect(editor)!;

        editor.SetGlyph(0x1F600, grid.ToOutline(Bitmap("#.#", "...", "###")), 4 * 64);
        var font = TtfFont.Read(editor.ToArray());

        Assert.True(font.HasGlyph(0x1F600));
        Assert.True(font.HasGlyph('a'));
        Assert.True(font.HasGlyph(0xC4));
    }

    [Fact]
    public void Removing_a_code_point_unmaps_it()
    {
        TtfEditor editor = TtfEditor.Load(PixelFontBuilder.StandardFont());

        Assert.True(editor.RemoveCodePoint('x'));
        var font = TtfFont.Read(editor.ToArray());

        Assert.False(font.HasGlyph('x'));
        Assert.True(font.HasGlyph('o'));
    }

    [Fact]
    public void Pixel_grid_is_detected_and_glyphs_rasterize_exactly()
    {
        TtfEditor editor = TtfEditor.Load(PixelFontBuilder.StandardFont());

        TtfPixelGrid grid = TtfPixelGrid.Detect(editor)!;
        WfnGlyph o = grid.Rasterize(editor, editor.GetGlyphIndex('o'));

        Assert.Equal(64, grid.Size);
        Assert.Equal(12, grid.TopRow); // ascender 768 / 64
        Assert.Equal(16, grid.Rows);   // down to the descender -256
        Assert.Equal(4, o.Width);
        Assert.Equal(new[] { ".##.", "#..#", "#..#", "#..#", ".##." }, InkRows(o));
        // The bottom ink row sits on the baseline
        Assert.True(o[1, grid.BaselineRow]);
        Assert.False(o[1, grid.BaselineRow + 1]);
    }

    [Fact]
    public void Traced_outlines_have_holes_and_separate_diagonal_pixels()
    {
        var grid = new TtfPixelGrid(64, 8, 10);
        WfnGlyph ring = Bitmap("###", "#.#", "###");
        WfnGlyph diagonal = Bitmap("#..", ".#.", "..#");

        TtfGlyphOutline ringOutline = grid.ToOutline(ring);
        TtfGlyphOutline diagonalOutline = grid.ToOutline(diagonal);

        Assert.Equal(2, ringOutline.Contours.Count);      // outer contour and hole
        Assert.All(ringOutline.Contours, c => Assert.Equal(4, c.Count));
        Assert.Equal(3, diagonalOutline.Contours.Count);  // one square per pixel
        Assert.Equal(Rows(ring), Rows(grid.Rasterize(ringOutline, 3 * 64)).Take(3));
        Assert.Equal(Rows(diagonal), Rows(grid.Rasterize(diagonalOutline, 3 * 64)).Take(3));
        // Top-left pixel is at y = 8 px above the baseline
        Assert.Contains(new TtfPoint(0, 8 * 64), ringOutline.Contours[0]);
    }

    [Fact]
    public void Outlines_with_curves_and_large_coordinates_survive_encoding()
    {
        TtfEditor editor = TtfEditor.Load(PixelFontBuilder.StandardFont());
        // A diamond with quadratic control points and a far-away point (word-sized deltas)
        var outline = new TtfGlyphOutline(new[]
        {
            new[] { new TtfPoint(0, 0), new TtfPoint(0, 300, false), new TtfPoint(300, 300), new TtfPoint(300, 0, false) },
            new[] { new TtfPoint(1000, -500), new TtfPoint(1000, 2000), new TtfPoint(1010, 2000), new TtfPoint(1010, -500) },
        });

        int glyph = editor.SetGlyph('q', outline, 1100);
        var reread = TtfEditor.Load(editor.ToArray());

        Assert.Equal(outline.Contours.SelectMany(c => c), reread.GetOutline(reread.GetGlyphIndex('q')).Contours.SelectMany(c => c));
        Assert.False(reread.GetOutline(glyph).IsPolygonal);
        // The curved contour bulges: its center is inside, the far corner of the bounding box is not
        Assert.NotEqual(0, outline.WindingAt(150, 150));
        Assert.Equal(0, outline.WindingAt(295, 5));
    }

    [Fact]
    public void Font_with_curves_has_no_pixel_grid()
    {
        TtfEditor editor = TtfEditor.Load(PixelFontBuilder.StandardFont());
        var round = new TtfGlyphOutline(new[] { new[] { new TtfPoint(0, 0), new TtfPoint(0, 384, false), new TtfPoint(384, 384), new TtfPoint(384, 0, false) } });
        for (int code = 0x100; code < 0x200; code++)
            editor.SetGlyph(code, round, 448);

        Assert.NotNull(TtfPixelGrid.Detect(TtfEditor.Load(PixelFontBuilder.StandardFont())));
        Assert.Null(TtfPixelGrid.Detect(editor));
    }

    [Fact]
    public void Game_fonts_are_exported_unchanged_and_never_overwritten_silently()
    {
        using var tmp = new TempDir();
        var wfn = new WfnFont();
        wfn.SetGlyph('A', Bitmap("#.#", ".#."));
        var fonts = new[]
        {
            GameFont.FromData("agsfnt0.ttf", PixelFontBuilder.StandardFont()),
            GameFont.FromData("agsfnt1.wfn", wfn.ToArray()),
        };

        var written = GameFonts.Export(fonts, tmp.Path);

        Assert.Equal(new[] { tmp.File("agsfnt0.ttf"), tmp.File("agsfnt1.wfn") }, written);
        Assert.Equal(fonts[0].Data, File.ReadAllBytes(written[0]));
        Assert.Equal(fonts[1].Data, File.ReadAllBytes(written[1]));
        Assert.Throws<IOException>(() => GameFonts.Export(fonts, tmp.Path));
        GameFonts.Export(fonts, tmp.Path, overwrite: true);
    }

    [FontToolsFact]
    public void Edited_font_is_valid_for_fonttools()
    {
        using var tmp = new TempDir();
        TtfEditor editor = TtfEditor.Load(PixelFontBuilder.StandardFont());
        TtfPixelGrid grid = TtfPixelGrid.Detect(editor)!;
        editor.SetGlyph(0xDF, grid.ToOutline(Bitmap(Eszett)), 5 * 64);
        editor.SetGlyph('a', grid.ToOutline(Bitmap("#")), 64);
        editor.SetGlyph(0x201C, grid.ToOutline(Bitmap("#.#", "#.#")), 4 * 64);
        string path = tmp.File("edited.ttf");
        editor.Write(path);

        // Load every table, compile it again and check the character maps and glyph names
        var result = RunPython(
            "import sys; from fontTools.ttLib import TTFont\n" +
            "f = TTFont(sys.argv[1])\n" +
            "for t in f.keys(): f[t]\n" +
            "f['glyf'].compile(f); f['cmap'].compile(f); f['post'].compile(f)\n" +
            "best = f.getBestCmap(); mac = f['cmap'].getcmap(1, 0).cmap\n" +
            "print(best[0xDF], best[0x201C], best[ord('a')], best[0xE4], mac[ord('a')], len(f.getGlyphOrder()), f['maxp'].numGlyphs, f['post'].formatType)",
            path);

        Assert.True(result.ExitCode == 0, result.Output);
        Assert.Equal("uni00DF uni201C uni0061 adieresis uni0061 10 10 2.0", result.Output.Trim());
    }

    internal static (int ExitCode, string Output) RunPython(string script, params string[] args)
    {
        try
        {
            var psi = new ProcessStartInfo("python3") { RedirectStandardOutput = true, RedirectStandardError = true };
            psi.ArgumentList.Add("-I");
            psi.ArgumentList.Add("-c");
            psi.ArgumentList.Add(script);
            foreach (string a in args)
                psi.ArgumentList.Add(a);
            using var process = Process.Start(psi)!;
            string output = process.StandardOutput.ReadToEnd() + process.StandardError.ReadToEnd();
            process.WaitForExit();
            return (process.ExitCode, output);
        }
        catch (System.ComponentModel.Win32Exception)
        {
            return (-1, "python3 not found");
        }
    }
}
