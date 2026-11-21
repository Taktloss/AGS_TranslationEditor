using AGSFontEditor.Rendering;

namespace AGSTools.Tests;

public class TextWrapTests
{
    // Every character 2 px wide
    private static int Measure(string s) => s.Length * 2;

    [Fact]
    public void Lines_wrap_at_spaces_within_the_width()
    {
        // "01 02 03" is 16 px, "01 02 03 04" 22 px
        Assert.Equal(new[] { "01 02 03", "04 05 06", "07" }, TextPreview.Wrap("01 02 03 04 05 06 07", 20, Measure));
    }

    [Fact]
    public void A_word_longer_than_the_width_stays_whole_and_short_lines_are_kept()
    {
        Assert.Equal(new[] { "a", "Donaudampfschifffahrt", "b" }, TextPreview.Wrap("a Donaudampfschifffahrt b", 10, Measure));
        Assert.Equal(new[] { "kurz" }, TextPreview.Wrap("kurz", 100, Measure));
        Assert.Equal(new[] { "" }, TextPreview.Wrap("", 100, Measure));
    }
}
