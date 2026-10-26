namespace AGSTools.Tests;

public class TranslationCheckTests
{
    private static TranslationDocument Doc(params (string Key, string Value)[] entries)
    {
        var doc = new TranslationDocument();
        foreach (var (key, value) in entries)
            doc.Entries[key] = value;
        return doc;
    }

    private static List<CheckIssue> Issues(TranslationDocument doc, string category, Glossary? glossary = null) =>
        TranslationCheck.Run(doc, glossary: glossary).Where(i => i.Category == category).ToList();

    // Monospaced WFN font: every character 5 px wide, 8 px high
    private static GameFont MonoFont()
    {
        var wfn = new WfnFont();
        for (int code = 32; code < 256; code++)
            wfn.SetGlyph(code, new WfnGlyph(5, 8));
        return GameFont.FromData("agsfnt0.wfn", wfn.ToArray());
    }

    private static ExtractionResult Game(GameDataBuilder builder)
    {
        using var tmp = new TempDir();
        string game = tmp.File("game28.dta");
        File.WriteAllBytes(game, builder.Build());
        return TextExtractor.Extract(game);
    }

    [Fact]
    public void Reports_missing_or_reordered_placeholders()
    {
        var doc = Doc(("You have %d coins and %s.", "Du hast %s und %d Münzen."), ("Score: @SCORE@", "Punkte:"), ("%d items", "%d Dinge"));
        var issues = Issues(doc, TranslationCheck.Placeholders);
        Assert.Equal(new[] { "You have %d coins and %s.", "Score: @SCORE@" }, issues.Select(i => i.Key));
        Assert.All(issues, i => Assert.Equal(CheckSeverity.Error, i.Severity));
    }

    [Fact]
    public void Reports_lost_voice_prefix()
    {
        var doc = Doc(("&12 Hello.", "Hallo."), ("&13 Bye.", "&13 Tschüss."));
        Assert.Equal("&12 Hello.", Assert.Single(Issues(doc, TranslationCheck.VoicePrefix)).Key);
    }

    [Fact]
    public void Reports_changed_outer_spaces_only_for_unspoken_texts()
    {
        var doc = Doc(("Ask about the photo ", "Nach dem Foto fragen"), ("&5 Hi. ", "&5 Hallo."));
        Assert.Equal("Ask about the photo ", Assert.Single(Issues(doc, TranslationCheck.Spaces)).Key);
    }

    [Fact]
    public void Reports_texts_translated_in_different_ways()
    {
        var doc = Doc(
            ("Look at the old door", "Die alte Tür ansehen"),
            ("Look at the old  door", "Die alte Türe ansehen"),
            ("&1 All right.", "Na gut."),
            ("&2 All right.", "Also gut."), // short spoken lines may differ
            ("&3 Can you tell me about her?", "Kannst du mir was über sie erzählen?"),
            ("&4 Can you tell me about her?", "Kannst du mir was über sie erzählen?"));
        var issues = Issues(doc, TranslationCheck.Inconsistent);
        Assert.Equal(2, issues.Count);
        Assert.Contains("\"Die alte Tür ansehen\" (1×)", issues[0].Message);
    }

    [Fact]
    public void Glossary_terms_must_use_one_of_their_translations()
    {
        var glossary = Glossary.Parse(new[] { "# names", "Auntie = Tante", "spirit guide = Geisterführer | Geistführer", "broken line" });
        Assert.Equal(2, glossary.Terms.Count);
        var doc = Doc(
            ("Bye, Auntie.", "Tschüss, liebe Tante."),
            ("My spirit guide is here.", "Mein Geistführer ist hier."),
            ("Where is my Spirit Guide?", "Wo ist mein Begleiter?"),
            ("A spirited talk.", "Ein lebhaftes Gespräch."));
        var issue = Assert.Single(Issues(doc, TranslationCheck.GlossaryTerm, glossary));
        Assert.Equal("Where is my Spirit Guide?", issue.Key);
        Assert.Contains("\"Geisterführer\" or \"Geistführer\"", issue.Message);
    }

    [Fact]
    public void Notes_formal_address_inside_a_sentence()
    {
        var doc = Doc(("I'll wait until she's finished.", "Ich warte, bis Sie fertig ist."), ("They left.", "Sie sind gegangen."));
        Assert.Equal("I'll wait until she's finished.", Assert.Single(Issues(doc, TranslationCheck.FormalAddress)).Key);
    }

    [Fact]
    public void Font_metrics_measure_and_wrap_like_the_engine()
    {
        FontMetrics metrics = FontMetrics.For(MonoFont())!;
        var options = new TranslationOptions();
        Assert.False(metrics.IsApproximate);
        Assert.Equal(8, metrics.Height);
        Assert.Equal(25, metrics.MeasureWidth("Hallo", options));
        Assert.Equal(new[] { "one two", "three", "four" }, metrics.Wrap("one two three[four", 40, options));
        Assert.Equal(new[] { "a[b" }, metrics.Wrap(@"a\[b", 40, options));
    }

    [Fact]
    public void Reports_gui_texts_that_do_not_fit()
    {
        var builder = new GameDataBuilder { ControlSize = (40, 10, 0) };
        builder.Buttons.AddRange(new[] { "Save", "Load" });
        builder.Labels.Add("Hint text");
        ExtractionResult game = Game(builder);
        var doc = Doc(("Save", "Speichern"), ("Load", "Laden"), ("Hint text", "Ein langer Hinweis"));

        var issues = TranslationCheck.Run(doc, game, new[] { MonoFont() });
        CheckIssue wide = Assert.Single(issues, i => i.Category == TranslationCheck.TooWide);
        Assert.Equal("Save", wide.Key);
        Assert.Contains("45 px wide, 5 px too wide", wide.Message);
        CheckIssue lines = Assert.Single(issues, i => i.Category == TranslationCheck.TooManyLines);
        Assert.Equal("Hint text", lines.Key);
        Assert.Contains("needs 3 lines", lines.Message);
    }

    private static ExtractionResult FormatGame() => Game(new GameDataBuilder
    {
        DataVersion = 3060000,
        GlobalScript = new ScriptBuilder()
            .Line(10).Format("%s[[%s")
            .Line(11).Call("Label::set_Text^1", "unused")
            .Line(20).Format("Score: %d")
            .Line(40).Call("GetTranslation", "Day").Format("%s %d")
            .Line(41).Call("Display", "unused")
            .Build(),
    });

    [Fact]
    public void Reports_texts_built_at_runtime_and_shown_on_a_label()
    {
        var issues = TranslationCheck.Run(Doc(("Obituary", "Nachruf")), FormatGame())
            .Where(i => i.Category == TranslationCheck.BuiltAtRuntime).ToList();

        CheckIssue shown = Assert.Single(issues, i => i.Severity == CheckSeverity.Warning);
        Assert.Equal("%s[[%s", shown.Key);
        Assert.Contains("GlobalScript.asc line 10", shown.Message);
        Assert.Contains("Label::set_Text", shown.Message);
        // Not shown nearby: only a note. Translated with GetTranslation(): not reported
        Assert.Equal("Score: %d", Assert.Single(issues, i => i.Severity == CheckSeverity.Info).Key);
    }

    [Fact]
    public void Entries_for_finished_texts_turn_the_warning_into_a_note()
    {
        var doc = Doc(("Obituary[[We found an obituary.", "Nachruf[[Wir fanden einen Nachruf."));
        CheckIssue issue = Assert.Single(TranslationCheck.Run(doc, FormatGame()),
            i => i.Category == TranslationCheck.BuiltAtRuntime && i.Key == "%s[[%s");
        Assert.Equal(CheckSeverity.Info, issue.Severity);
        Assert.Contains("1 translated entries", issue.Message);
    }
}
