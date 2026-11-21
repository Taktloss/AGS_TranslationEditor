namespace AGSTools.Tests;

public class GlyphComposerTests
{
    private static WfnGlyph Glyph(params string[] rows) => Shaded(1, rows);

    // '#' = ink with the given shade, '.' = empty
    private static WfnGlyph Shaded(int bitsPerPixel, string[] rows, int shade = 1)
    {
        var glyph = new WfnGlyph(rows[0].Length, rows.Length, null, bitsPerPixel);
        for (int y = 0; y < rows.Length; y++)
            for (int x = 0; x < rows[y].Length; x++)
                if (rows[y][x] == '#')
                    glyph.SetShade(x, y, shade);
        return glyph;
    }

    private static string[] Rows(WfnGlyph glyph) =>
        Enumerable.Range(0, glyph.Height)
            .Select(y => new string(Enumerable.Range(0, glyph.Width).Select(x => glyph.GetShade(x, y) != 0 ? '#' : '.').ToArray()))
            .ToArray();

    [Fact]
    public void A_lowercase_letter_gets_two_dots_above_with_one_empty_row()
    {
        WfnGlyph a = Glyph(
            ".....",
            ".....",
            ".....",
            ".###.",
            "....#",
            ".####",
            "#...#",
            ".####");

        Assert.Equal(new[]
        {
            ".....",
            ".#.#.",
            ".....",
            ".###.",
            "....#",
            ".####",
            "#...#",
            ".####",
        }, Rows(GlyphComposer.AddDiaeresis(a)!));
    }

    [Fact]
    public void A_capital_without_room_is_made_shorter_and_keeps_its_baseline()
    {
        WfnGlyph o = Glyph(
            ".###.",
            "#...#",
            "#...#",
            "#...#",
            "#...#",
            ".###.",
            ".....");

        // Two rows are needed (dot and gap); two repeating rows of the middle are left out
        Assert.Equal(new[]
        {
            ".#.#.",
            ".....",
            ".###.",
            "#...#",
            "#...#",
            ".###.",
            ".....",
        }, Rows(GlyphComposer.AddDiaeresis(o)!));
    }

    [Fact]
    public void Bold_letters_get_bigger_dots_and_shaded_fonts_their_shade()
    {
        WfnGlyph u = Shaded(4, new[]
        {
            "........",
            "........",
            "........",
            "........",
            "##....##",
            "##....##",
            "##....##",
            "##....##",
            ".######.",
        }, shade: 11);

        WfnGlyph result = GlyphComposer.AddDiaeresis(u)!;

        Assert.Equal(new[]
        {
            "........",
            ".##..##.",
            ".##..##.",
            "........",
            "##....##",
            "##....##",
            "##....##",
            "##....##",
            ".######.",
        }, Rows(result));
        Assert.Equal(11, result.GetShade(1, 1));
    }

    [Fact]
    public void Empty_or_too_small_glyphs_are_refused()
    {
        Assert.Null(GlyphComposer.AddDiaeresis(Glyph("...", "...")));
        Assert.Null(GlyphComposer.AddDiaeresis(Glyph("##", "##", "##")));
    }

    [Fact]
    public void The_base_letter_is_not_changed()
    {
        WfnGlyph a = Glyph("....", "....", ".##.", "#..#", ".###");
        string[] before = Rows(a);
        GlyphComposer.AddDiaeresis(a);
        Assert.Equal(before, Rows(a));
    }

    [Fact]
    public void HasInk_tells_empty_glyphs_apart()
    {
        Assert.False(GlyphComposer.HasInk(null));
        Assert.False(GlyphComposer.HasInk(Glyph("...", "...")));
        Assert.True(GlyphComposer.HasInk(Glyph("...", ".#.")));
    }
}
