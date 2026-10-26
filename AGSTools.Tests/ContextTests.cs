namespace AGSTools.Tests;

public class ContextTests
{
    private static ExtractionResult Extract(int dataVersion, byte[] script)
    {
        var builder = new GameDataBuilder { DataVersion = dataVersion, Title = "Context Game", GlobalScript = script };
        builder.Characters.AddRange(new[] { "Rosa Blackwell", "Joey Mallone" });
        builder.CharacterScriptNames.AddRange(new[] { "cRosa", "cJoey" });
        using var tmp = new TempDir();
        string game = tmp.File("game28.dta");
        File.WriteAllBytes(game, builder.Build());
        return TextExtractor.Extract(game);
    }

    private static byte[] SampleScript() => new ScriptBuilder()
        .Section("GlobalScript.asc")
        .Line(1).Say("cRosa", "I should talk to the doorman.")
        .Line(2).Say("cJoey", "Hey, don't look at me like that.")
        .Line(3).Call("Display", "The door is locked.")
        .Section("Dialog 0")
        .Line(4).Call("DisplaySpeech", "Where is everybody going?", 1)
        .Line(5).Literal("This sentence is only stored.")
        .Build();

    [Theory]
    [InlineData(32)]        // AGS 2.72
    [InlineData(3060000)]   // AGS 3.6
    public void Finds_who_says_each_script_text(int dataVersion)
    {
        var texts = Extract(dataVersion, SampleScript()).Texts.ToDictionary(t => t.Text);

        Assert.Equal("cRosa", texts["I should talk to the doorman."].Speaker);
        Assert.Equal("cJoey", texts["Hey, don't look at me like that."].Speaker);
        Assert.Equal("Display", texts["The door is locked."].Speaker);
        Assert.Equal("cJoey", texts["Where is everybody going?"].Speaker); // DisplaySpeech(1, ...)
        Assert.Null(texts["This sentence is only stored."].Speaker);
    }

    [Fact]
    public void Texts_are_located_in_their_script_section()
    {
        var texts = Extract(3060000, SampleScript()).Texts.ToDictionary(t => t.Text);

        Assert.Equal("GlobalScript.asc", texts["I should talk to the doorman."].Location);
        Assert.Equal("Dialog 0", texts["Where is everybody going?"].Location);
    }

    [Fact]
    public void A_speaker_is_not_carried_over_to_the_next_line()
    {
        byte[] script = new ScriptBuilder()
            .Line(1).Literal("A sentence in a variable.")
            .Line(2).Say("cRosa", "Only this one is said.")
            .Build();
        var texts = Extract(3060000, script).Texts.ToDictionary(t => t.Text);

        Assert.Null(texts["A sentence in a variable."].Speaker);
        Assert.Equal("cRosa", texts["Only this one is said."].Speaker);
    }

    [Fact]
    public void Context_file_uses_character_names_and_round_trips()
    {
        var origins = TextContext.FromExtraction(Extract(3060000, SampleScript()));

        Assert.Equal("Rosa Blackwell", origins["I should talk to the doorman."].Speaker);
        Assert.Equal(TextContext.Narration, origins["The door is locked."].Speaker);
        Assert.Equal("Rosa Blackwell · GlobalScript.asc", origins["I should talk to the doorman."].ToString());

        using var tmp = new TempDir();
        string path = TextContext.PathFor(tmp.File("Game.trs"));
        Assert.EndsWith("Game.context.json", path);
        TextContext.Write(path, origins);
        var read = TextContext.Read(path)!;
        Assert.Equal(origins.Count, read.Count);
        Assert.Equal(origins["Where is everybody going?"], read["Where is everybody going?"]);
    }

    [Fact]
    public void Missing_or_broken_context_file_is_ignored()
    {
        using var tmp = new TempDir();
        Assert.Null(TextContext.Read(tmp.File("none.context.json")));
        File.WriteAllText(tmp.File("bad.context.json"), "{ not json");
        Assert.Null(TextContext.Read(tmp.File("bad.context.json")));
    }
}
