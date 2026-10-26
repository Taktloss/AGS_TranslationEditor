namespace AGSTools.Tests;

public class ExtractionTests
{
    private static ExtractionResult ExtractFrom(GameDataBuilder builder)
    {
        using var tmp = new TempDir();
        string game = tmp.File("game28.dta");
        File.WriteAllBytes(game, builder.Build());
        return TextExtractor.Extract(game);
    }

    private static GameDataBuilder SampleGame()
    {
        var b = new GameDataBuilder { Title = "Sample", UniqueIdBytes = new byte[] { 1, 0, 0, 0 } };
        b.Characters.AddRange(new[] { "Roger", "Narrator" });
        b.InventoryItems.Add("A pair of old scissors t"); // legacy field holds 24 characters
        b.GlobalMessages.Add("You can't do that.");
        b.Dialogs.Add(new[] { "Hello there ", "//not storable", "Bye" });
        b.Buttons.AddRange(new[] { "Save", "5" });
        b.Labels.Add("Score: @SCORE@");
        return b;
    }

    [Fact]
    public void Extracts_texts_from_all_game_data_sections()
    {
        ExtractionResult result = ExtractFrom(SampleGame());
        var texts = result.Texts.ToDictionary(t => t.Text, t => t.Source);

        Assert.Equal(TextSource.CharacterName, texts["Roger"]);
        Assert.Equal(TextSource.InventoryItem, texts["A pair of old scissors t"]);
        Assert.Equal(TextSource.GlobalMessage, texts["You can't do that."]);
        Assert.Equal(TextSource.DialogOption, texts["Hello there "]); // the engine looks up the exact text
        Assert.Equal(TextSource.Gui, texts["Save"]);
        Assert.Equal(TextSource.Gui, texts["Score: @SCORE@"]);
        Assert.Empty(result.Warnings);
    }

    [Fact]
    public void Skips_texts_that_cannot_or_need_not_be_translated()
    {
        var texts = ExtractFrom(SampleGame()).Texts.Select(t => t.Text).ToList();

        Assert.DoesNotContain("//not storable", texts); // would be read as a TRS comment
        Assert.DoesNotContain("5", texts);
        Assert.DoesNotContain("", texts);
    }

    [Fact]
    public void Full_names_from_extension_replace_truncated_ones()
    {
        var builder = SampleGame();
        builder.ObjectNames = ("Sample", new[] { "Roger Wilco", "Narrator" }, new[] { "", "A pair of old scissors that cut paper" });

        var texts = ExtractFrom(builder).Texts.Select(t => t.Text).ToList();

        Assert.Contains("Roger Wilco", texts);
        Assert.Contains("A pair of old scissors that cut paper", texts);
        Assert.DoesNotContain("A pair of old scissors t", texts);
    }

    [Fact]
    public void Translation_document_identifies_the_game()
    {
        TranslationDocument doc = ExtractFrom(SampleGame()).ToTranslationDocument();

        Assert.Equal(1, doc.Options.GameUid);
        Assert.Equal("Sample", doc.Options.GameName);
        Assert.All(doc.Entries.Values, v => Assert.Equal("", v));
    }

    [Fact]
    public void Unicode_game_produces_utf8_translation()
    {
        var builder = SampleGame();
        builder.Codepage = 65001;
        builder.Characters.Add("Ёжик");

        ExtractionResult result = ExtractFrom(builder);

        Assert.Contains(result.Texts, t => t.Text == "Ёжик");
        Assert.True(result.ToTranslationDocument().Options.IsUtf8);
    }

    [Fact]
    public void Old_wrapper_writes_trs_file()
    {
        using var tmp = new TempDir();
        string game = tmp.File("game28.dta");
        File.WriteAllBytes(game, SampleGame().Build());
        string trs = tmp.File("out.trs");

        Extraction.ParseAGSFile(game, trs);

        var doc = TrsFile.Read(trs);
        Assert.Equal("Sample", doc.Options.GameName);
        Assert.Contains("Roger", doc.Entries.Keys);
    }

    [Fact]
    public void Missing_game_file_throws()
    {
        Assert.Throws<FileNotFoundException>(() => TextExtractor.Extract("/does/not/exist.exe"));
    }

    [Theory]
    [InlineData(19, "2.51")] // plain messages and dialog lines, fixed sprite flags
    [InlineData(26, "2.61")] // encrypted messages and dialog lines
    [InlineData(32, "2.72")] // script modules, no savegame info
    public void Reads_AGS_2x_game_data_and_rooms(int dataVersion, string engineVersion)
    {
        var builder = new GameDataBuilder { DataVersion = dataVersion, EngineVersion = engineVersion, Title = "Old Game" };
        builder.Characters.Add("Roger");
        builder.InventoryItems.Add("Key");
        builder.GlobalMessages.Add("You can't do that.");
        builder.Dialogs.Add(new[] { "Hello", "Bye" });
        builder.OldDialogLines.AddRange(new[] { "Hello yourself.", "See you later." });
        builder.Buttons.Add("Quit");
        int roomVersion = dataVersion <= 19 ? 18 : dataVersion <= 26 ? 22 : 25;
        var room = new RoomBuilder { Version = roomVersion };
        room.Hotspots.AddRange(new[] { "Door", "Hotspot 1" });
        room.Objects.Add("Rusty key");
        room.Messages.Add("The door is locked.");

        using var tmp = new TempDir();
        string game = tmp.File("game28.dta");
        File.WriteAllBytes(game, builder.Build());
        File.WriteAllBytes(tmp.File("room1.crm"), room.Build());
        ExtractionResult result = TextExtractor.Extract(game);
        var texts = result.Texts.ToDictionary(t => t.Text, t => t.Source);

        Assert.Empty(result.Warnings);
        Assert.Equal("Old Game", result.Game!.Title);
        Assert.Equal(TextSource.CharacterName, texts["Roger"]);
        Assert.Equal(TextSource.InventoryItem, texts["Key"]);
        Assert.Equal(TextSource.GlobalMessage, texts["You can't do that."]);
        Assert.Equal(TextSource.DialogOption, texts["Bye"]);
        Assert.Equal(TextSource.DialogLine, texts["See you later."]);
        Assert.Equal(TextSource.Gui, texts["Quit"]);
        Assert.Equal(TextSource.Hotspot, texts["Door"]);
        Assert.Equal(TextSource.RoomObject, texts["Rusty key"]);
        Assert.Equal(TextSource.RoomMessage, texts["The door is locked."]);
        Assert.DoesNotContain("Hotspot 1", texts.Keys);
    }

    [Fact]
    public void Games_before_AGS_2_5_fall_back_to_the_heuristic_scan()
    {
        var builder = new GameDataBuilder { DataVersion = 12, EngineVersion = "2.3" };
        using var tmp = new TempDir();
        string game = tmp.File("game28.dta");
        File.WriteAllBytes(game, builder.Build());

        ExtractionResult result = TextExtractor.Extract(game);

        Assert.Null(result.Game);
        Assert.Contains(result.Warnings, w => w.Contains("older than AGS 2.5"));
    }
}
