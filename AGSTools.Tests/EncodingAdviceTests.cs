namespace AGSTools.Tests;

public class EncodingAdviceTests
{
    private static TranslationDocument Doc(string encoding, string translation)
    {
        var doc = new TranslationDocument();
        doc.Options.Encoding = encoding;
        doc.Options.AnsiCodepage = 1252;
        doc.Entries["Hello"] = translation;
        return doc;
    }

    [Fact]
    public void Utf8_translation_of_an_8bit_game_suggests_8bit_if_all_characters_fit()
    {
        string warning = Assert.Single(EncodingAdvice.Check(Doc("UTF-8", "Grüße"), "3.6.1.35", gameIsUnicode: false));

        Assert.Contains("Linux and macOS", warning);
        Assert.Contains("an 8-bit translation works everywhere", warning);
    }

    [Fact]
    public void Utf8_translation_with_characters_outside_the_codepage_is_windows_only()
    {
        string warning = Assert.Single(EncodingAdvice.Check(Doc("UTF-8", "Cześć „Grüße“"), "3.6.1.35", gameIsUnicode: false));

        Assert.Contains("ś", warning);
        Assert.DoesNotContain("„", warning); // exists in 1252
        Assert.Contains("meant for Windows", warning);
    }

    [Fact]
    public void Utf8_translation_for_an_engine_before_3_6_is_rejected()
    {
        string warning = Assert.Single(EncodingAdvice.Check(Doc("UTF-8", "Grüße"), "3.5.1.14", gameIsUnicode: false));

        Assert.Contains("AGS 3.6 or later", warning);
    }

    [Theory]
    [InlineData("UTF-8", true)]  // the game is UTF-8 itself: no key conversion
    [InlineData("", false)]      // 8-bit translation
    public void No_warning_when_no_key_conversion_is_needed(string encoding, bool gameIsUnicode) =>
        Assert.Empty(EncodingAdvice.Check(Doc(encoding, "Grüße"), "3.6.1.35", gameIsUnicode));
}
