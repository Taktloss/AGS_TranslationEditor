namespace AGSTools.Tests;

public class TranslationMergerTests
{
    private static TranslationDocument Doc(params (string Source, string Translation)[] entries)
    {
        var doc = new TranslationDocument();
        foreach (var (s, t) in entries)
            doc.Entries.Add(s, t);
        return doc;
    }

    [Fact]
    public void Result_has_game_texts_in_game_order_with_translations()
    {
        var extracted = Doc(("B", ""), ("A", ""), ("New", ""));
        var existing = Doc(("A", "a"), ("B", "b"));

        MergeResult result = TranslationMerger.Merge(extracted, existing);

        Assert.Equal(new[] { "B", "A", "New" }, result.Document.Entries.Keys);
        Assert.Equal(new[] { "b", "a", "" }, result.Document.Entries.Values);
        Assert.Equal(2, result.ExactCount);
        Assert.Equal(1, result.NewCount);
        Assert.Equal(MergeMatch.New, result.Matches["New"]);
    }

    [Fact]
    public void Texts_differing_in_surrounding_spaces_keep_their_translation()
    {
        var result = TranslationMerger.Merge(Doc(("We read the journal. ", "")), Doc(("We read the journal.", "Wir lasen das Tagebuch.")));

        Assert.Equal("Wir lasen das Tagebuch.", result.Document.Entries["We read the journal. "]);
        Assert.Equal(MergeMatch.Similar, result.Matches["We read the journal. "]);
        Assert.Empty(result.Obsolete);
    }

    [Theory]
    [InlineData("&32 Spit it out.", "Spit it out.", "Raus damit.", "&32 Raus damit.")]       // prefix added
    [InlineData("Spit it out.", "&32 Spit it out.", "&32 Raus damit.", "Raus damit.")]       // prefix removed
    [InlineData("&40 Spit it out.", "&32 Spit it out.", "&32 Raus damit.", "&40 Raus damit.")] // number changed
    public void Voice_prefix_of_translation_follows_the_game(string newSource, string oldSource, string oldTranslation, string expected)
    {
        var result = TranslationMerger.Merge(Doc((newSource, "")), Doc((oldSource, oldTranslation)));
        Assert.Equal(expected, result.Document.Entries[newSource]);
    }

    [Fact]
    public void Same_text_voiced_and_unvoiced_both_get_the_translation()
    {
        var result = TranslationMerger.Merge(Doc(("Yes.", ""), ("&5 Yes.", "")), Doc(("Yes.", "Ja.")));

        Assert.Equal("Ja.", result.Document.Entries["Yes."]);
        Assert.Equal("&5 Ja.", result.Document.Entries["&5 Yes."]);
    }

    [Fact]
    public void Obsolete_translations_are_reported_and_kept_unless_disabled()
    {
        var extracted = Doc(("A", ""));
        var existing = Doc(("A", "a"), ("Removed line", "Entfernt"), ("Untranslated", ""), ("&3 ...", "&3 ..."));

        MergeResult kept = TranslationMerger.Merge(extracted, existing);
        MergeResult dropped = TranslationMerger.Merge(extracted, existing, keepObsolete: false);

        Assert.Equal("Removed line", Assert.Single(kept.Obsolete).Key); // untranslated and letterless are not obsolete
        Assert.Equal("Entfernt", kept.Document.Entries["Removed line"]);
        Assert.False(kept.Document.Entries.ContainsKey("Untranslated"));
        Assert.Equal(new[] { "A" }, dropped.Document.Entries.Keys);
    }

    [Fact]
    public void Translator_settings_are_kept_and_game_identification_updated()
    {
        var extracted = Doc(("A", ""));
        extracted.Options.GameUid = 42;
        extracted.Options.GameName = "Game";
        extracted.Options.AnsiCodepage = 1250;
        var existing = Doc(("A", "a"));
        existing.Options.Encoding = "UTF-8";
        existing.Options.Language = "de";
        existing.Options.NormalFont = 3;

        TranslationOptions o = TranslationMerger.Merge(extracted, existing).Document.Options;

        Assert.Equal(42, o.GameUid);
        Assert.Equal("Game", o.GameName);
        Assert.Equal(1250, o.AnsiCodepage);
        Assert.True(o.IsUtf8);
        Assert.Equal("de", o.Language);
        Assert.Equal(3, o.NormalFont);
        Assert.NotSame(existing.Options, o);
    }
}
