namespace AGSTools.Tests;

public class FormatTests
{
    private static readonly Dictionary<string, string> Tricky = new()
    {
        ["Hello"] = "Hallo",
        ["He said \"hi\"."] = "Er sagte \"hallo\".",
        ["\"Quoted at start\""] = "\"Am Anfang zitiert\"",
        ["Back\\slash"] = "Rück\\strich",
        ["Semi;colon, comma"] = "Semi;kolon, Komma",
        ["Tab\there"] = "Tab\thier",
        ["Line\nbreak"] = "Zeilen\numbruch",
        ["&5 Voiced line"] = "&5 Gesprochene Zeile",
        ["Umlaute äöüß"] = "",
    };

    [Fact]
    public void Po_roundtrip_preserves_special_characters()
    {
        using var tmp = new TempDir();
        string po = tmp.File("test.po");

        POFormat.CreatePO(po, Tricky);

        Assert.Equal(Tricky, POFormat.OpenPO(po));
    }

    [Fact]
    public void Po_reader_handles_foreign_headers_and_multiline_strings()
    {
        using var tmp = new TempDir();
        string po = tmp.File("poedit.po");
        File.WriteAllText(po, """
            # Translator comment
            msgid ""
            msgstr ""
            "Project-Id-Version: Something\n"
            "Language: de\n"
            "Plural-Forms: nplurals=2; plural=(n != 1);\n"

            #: script.asc:12
            #, fuzzy
            msgid "First"
            msgstr "Erste"

            msgid ""
            "Long text that "
            "spans lines"
            msgstr ""
            "Langer Text, der "
            "umbricht"

            msgctxt "menu"
            msgid "Open"
            msgstr "Öffnen"

            msgid "Empty"
            msgstr ""

            #~ msgid "Obsolete"
            #~ msgstr "Veraltet"
            """);

        var result = POFormat.OpenPO(po);

        Assert.Equal(new Dictionary<string, string>
        {
            ["First"] = "Erste",
            ["Long text that spans lines"] = "Langer Text, der umbricht",
            ["Open"] = "Öffnen",
            ["Empty"] = "",
        }, result);
    }

    [Fact]
    public void Csv_roundtrip_preserves_special_characters()
    {
        using var tmp = new TempDir();
        string csv = tmp.File("test.csv");

        CSVFormat.CreateCSV(csv, Tricky);

        Assert.Equal(Tricky, CSVFormat.OpenCSV(csv));
    }

    [Fact]
    public void Tsv_roundtrip_uses_tab_separator()
    {
        using var tmp = new TempDir();
        string tsv = tmp.File("test.tsv");

        CSVFormat.CreateCSV(tsv, Tricky, '\t');

        Assert.Contains("Hello\tHallo", File.ReadAllText(tsv));
        Assert.Equal(Tricky, CSVFormat.OpenCSV(tsv));
    }

    [Fact]
    public void Csv_reader_tolerates_missing_translation_column_and_blank_lines()
    {
        using var tmp = new TempDir();
        string csv = tmp.File("loose.csv");
        File.WriteAllText(csv, "Only source\r\n\r\nHello;Hallo\r\nHello;Duplicate\n");

        var result = CSVFormat.OpenCSV(csv);

        Assert.Equal(new Dictionary<string, string>
        {
            ["Only source"] = "",
            ["Hello"] = "Hallo",
        }, result);
    }
}
