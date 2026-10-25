namespace AGSTools.Tests;

public class TranslationTests
{
    private static readonly GameInfo Info = new() { GameTitle = "Test Game", GameUID = "1A2B3C4D", Version = "3.6.0.50" };

    [Fact]
    public void Tra_roundtrip_keeps_translated_entries()
    {
        using var tmp = new TempDir();
        string tra = tmp.File("test.tra");
        var entries = new Dictionary<string, string>
        {
            ["Hello"] = "Hallo",
            ["&12 Where is the key?"] = "&12 Wo ist der Schlüssel?",
            ["Größe & Gewicht"] = "Size & weight",
            ["Untranslated"] = "",
        };

        Translation.CreateTRA_File(Info, tra, entries);
        var result = Translation.ParseTRA_Translation(tra);

        var expected = entries.Where(e => e.Value != "").ToDictionary(e => e.Key, e => e.Value);
        Assert.Equal(expected, result);
    }

    [Fact]
    public void Tra_with_no_entries_is_valid_and_reads_back_empty()
    {
        using var tmp = new TempDir();
        string tra = tmp.File("empty.tra");

        Translation.CreateTRA_File(Info, tra, new Dictionary<string, string>());

        byte[] bytes = File.ReadAllBytes(tra);
        // Blocks end with the -1 end-of-data marker
        Assert.EndsWith("FFFFFFFF", Convert.ToHexString(bytes));
        Assert.Empty(Translation.ParseTRA_Translation(tra));
    }

    [Fact]
    public void Tra_writes_game_uid_in_file_byte_order()
    {
        using var tmp = new TempDir();
        string tra = tmp.File("uid.tra");

        Translation.CreateTRA_File(Info, tra, new Dictionary<string, string> { ["a b"] = "c d" });

        // "AGSTranslation\0" (15) + block type (4) + block size (4) = UID offset
        byte[] uid = File.ReadAllBytes(tra).AsSpan(23, 4).ToArray();
        Assert.Equal(new byte[] { 0x1A, 0x2B, 0x3C, 0x4D }, uid);
    }

    [Fact]
    public void Real_tra_file_parses_and_survives_rewrite()
    {
        using var tmp = new TempDir();
        var original = Translation.ParseTRA_Translation(TestData.Path("Deutsch.tra"));
        Assert.NotEmpty(original);
        Assert.DoesNotContain("", original.Keys);
        Assert.All(original.Values, v => Assert.DoesNotContain('\uFFFD', v));

        string rewritten = tmp.File("rewritten.tra");
        Translation.CreateTRA_File(Info, rewritten, original);

        Assert.Equal(original, Translation.ParseTRA_Translation(rewritten));
    }

    [Fact]
    public void Trs_parser_skips_comments_and_pairs_lines()
    {
        using var tmp = new TempDir();
        string trs = tmp.File("test.trs");
        File.WriteAllLines(trs, new[]
        {
            "// Translation file",
            "//#NormalFont=DEFAULT",
            "Hello",
            "Hallo",
            "Untranslated",
            "",
            "Last line without translation",
        }, System.Text.Encoding.Latin1);

        var result = Translation.ParseTRS_Translation(trs);

        Assert.Equal(new Dictionary<string, string>
        {
            ["Hello"] = "Hallo",
            ["Untranslated"] = "",
            ["Last line without translation"] = "",
        }, result);
    }

    [Fact]
    public void Real_trs_file_parses()
    {
        var result = Translation.ParseTRS_Translation(TestData.Path("Epiphany.trs"));
        Assert.Equal("", result["Voice and Text"]);
        Assert.True(result.Count > 100);
    }
}
