using System.Text;

namespace AGSTools.Tests;

public class TrsFileTests
{
    private static TranslationDocument Parse(string content, Encoding? encoding = null) =>
        TrsFile.Parse((encoding ?? new UTF8Encoding(false)).GetBytes(content));

    [Fact]
    public void Header_options_survive_roundtrip()
    {
        var doc = new TranslationDocument();
        doc.Options.GameUid = 156899796;
        doc.Options.GameName = "Unavowed";
        doc.Options.NormalFont = 2;
        doc.Options.TextDirection = TextDirection.LeftToRight;
        doc.Options.Encoding = "UTF-8";
        doc.Options.GameEncoding = "1252";
        doc.Options.Language = "de";
        doc.Options.AutoTranslateParserSaid = true;
        doc.Options.FontOverrides[3] = "File=x.ttf;Size=12";
        doc.Entries["Hello"] = "Hallo";
        doc.Entries["Untranslated"] = "";

        var result = TrsFile.Parse(TrsFile.Format(doc, new List<string>()));

        TranslationOptions o = result.Options;
        Assert.Equal(156899796, o.GameUid);
        Assert.Equal("Unavowed", o.GameName);
        Assert.Equal(2, o.NormalFont);
        Assert.Equal(-1, o.SpeechFont);
        Assert.Equal(TextDirection.LeftToRight, o.TextDirection);
        Assert.Equal("UTF-8", o.Encoding);
        Assert.Equal("1252", o.GameEncoding);
        Assert.Equal("de", o.Language);
        Assert.True(o.AutoTranslateParserSaid);
        Assert.Equal("File=x.ttf;Size=12", o.FontOverrides[3]);
        Assert.Equal(doc.Entries, result.Entries);
    }

    [Fact]
    public void Written_header_matches_ags_editor_format()
    {
        string text = Encoding.UTF8.GetString(TrsFile.Format(new TranslationDocument(), new List<string>()));
        string[] lines = text.Split("\r\n");

        Assert.Equal("// AGS TRANSLATION SOURCE FILE", lines[0]);
        Assert.Contains("//#GameID=", lines);
        Assert.Contains("//#NormalFont=DEFAULT", lines);
        Assert.Contains("//#Encoding=ASCII", lines);
        Assert.All(lines.Where(l => l.Length > 0), l => Assert.StartsWith("//", l));
    }

    [Fact]
    public void Utf8_bom_is_removed_and_first_line_kept()
    {
        byte[] content = new byte[] { 0xEF, 0xBB, 0xBF }.Concat(Encoding.UTF8.GetBytes("Grüße\nGreetings\n")).ToArray();
        Assert.Equal("Greetings", TrsFile.Parse(content).Entries["Grüße"]);
    }

    [Fact]
    public void File_without_bom_keeps_its_first_bytes()
    {
        // Regression: an empty UTF-8 preamble must not be treated as a BOM
        Assert.Equal("Hallo", Parse("Hello\nHallo\n").Entries["Hello"]);
    }

    [Fact]
    public void Blank_lines_in_header_do_not_shift_entries()
    {
        var doc = Parse("// header\n\n//#Encoding=UTF-8\n\nv2.0.0\nv2.0.0\nMouse\nMaus\n");
        Assert.Equal(new Dictionary<string, string> { ["v2.0.0"] = "v2.0.0", ["Mouse"] = "Maus" }, doc.Entries);
    }

    [Fact]
    public void Line_after_source_is_always_its_translation()
    {
        // Like the AGS compiler: a translation may start with "//"
        var doc = Parse("Path\n//server/share\nNext\nNächste\n");
        Assert.Equal("//server/share", doc.Entries["Path"]);
        Assert.Equal("Nächste", doc.Entries["Next"]);
    }

    [Fact]
    public void Ansi_file_is_decoded_with_codepage()
    {
        byte[] content = AgsTextEncoding.ForCodepage(1252).GetBytes("//#Encoding=ASCII\nQuote\n„Zitat“\n");
        Assert.Equal("„Zitat“", TrsFile.Parse(content, 1252).Entries["Quote"]);
    }

    [Fact]
    public void Invalid_utf8_without_hint_falls_back_to_codepage()
    {
        byte[] content = Encoding.Latin1.GetBytes("Size\nGröße\n");
        Assert.Equal("Größe", TrsFile.Parse(content).Entries["Size"]);
    }

    [Fact]
    public void Characters_missing_from_codepage_produce_warning()
    {
        var doc = new TranslationDocument();
        doc.Entries["A"] = "Привет";
        var warnings = new List<string>();

        TrsFile.Format(doc, warnings);

        Assert.Single(warnings);
    }

    [Fact]
    public void Line_breaks_are_written_as_escape_sequences()
    {
        var doc = new TranslationDocument();
        doc.Entries["A"] = "one\ntwo";

        var result = TrsFile.Parse(TrsFile.Format(doc, new List<string>()));

        Assert.Equal("one\\ntwo", result.Entries["A"]);
    }
}
