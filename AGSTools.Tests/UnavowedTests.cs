namespace AGSTools.Tests;

/// <summary>
/// A test that needs the Unavowed game files in the "Unavowed" folder of the repository.
/// The game is commercial and not part of the repository, so these tests are skipped without it.
/// </summary>
public sealed class GameFileFactAttribute : FactAttribute
{
    public GameFileFactAttribute()
    {
        if (!File.Exists(UnavowedTests.GamePath("unavowed.ags")))
            Skip = "Unavowed game files not found (place them in the 'Unavowed' folder of the repository).";
    }
}

public class UnavowedTests
{
    private const int UnavowedUid = 156899796;

    internal static string GamePath(string file)
    {
        // Walk up from the test output folder to the repository root
        for (var dir = new DirectoryInfo(AppContext.BaseDirectory); dir != null; dir = dir.Parent)
        {
            if (File.Exists(Path.Combine(dir.FullName, "AGS_TranslationEditor.sln")))
                return Path.Combine(dir.FullName, "Unavowed", file);
        }
        return Path.Combine("Unavowed", file);
    }

    private static readonly Lazy<ExtractionResult> Extracted = new(() => TextExtractor.Extract(GamePath("Unavowed.exe")));

    [GameFileFact]
    public void Game_info_is_read_through_the_engine_exe()
    {
        GameInfo info = GameInfo.GetGameInfo(GamePath("Unavowed.exe"))!;

        Assert.Equal("Unavowed", info.GameTitle);
        Assert.Equal(UnavowedUid, info.UniqueId);
        Assert.Equal("3.6.1.35", info.Version);
        Assert.Equal(1252, info.TextCodepage);
        Assert.EndsWith("unavowed.ags", info.DataPath, StringComparison.OrdinalIgnoreCase);
    }

    [GameFileFact]
    public void Game_data_and_all_rooms_are_read_completely()
    {
        ExtractionResult result = Extracted.Value;

        Assert.NotNull(result.Game);
        Assert.True(result.Game.IsComplete, result.Game.Error);
        Assert.Equal(95, result.Game.CharacterNames.Count);
        Assert.Equal(73, result.Game.InventoryNames.Count);
        Assert.Equal(232, result.Game.Dialogs.Count);
        Assert.Equal(125, result.RoomCount);
        Assert.Empty(result.Warnings);
    }

    [GameFileFact]
    public void Extraction_covers_room_texts_and_full_inventory_names()
    {
        var texts = Extracted.Value.Texts;

        // Hotspot and object names, which the old heuristic extractor missed
        Assert.Contains(texts, t => t.Text == "Crate" && t.Source is TextSource.Hotspot or TextSource.RoomObject);
        Assert.Contains(texts, t => t.Text == "Toucan");
        // Inventory names are not truncated to the legacy 25-character field
        Assert.DoesNotContain(texts, t => t.Text == "A pair of old scissors. ");
        Assert.Contains(texts, t => t.Source == TextSource.InventoryItem && t.Text.Length > 25);
        // Voice-prefixed dialog lines
        Assert.Contains(texts, t => t.Text.StartsWith("&32 Spit it out, Morey.", StringComparison.Ordinal));
    }

    [GameFileFact]
    public void Extraction_covers_existing_translation()
    {
        var extracted = Extracted.Value.ToTranslationDocument().Entries.Keys.ToHashSet();
        var existing = TrsFile.Read(GamePath("Deutsch.trs")).Entries;

        // Every translated text of the existing translation must be found, apart from
        // differences in trailing spaces (which the old extractor trimmed)
        var normalized = extracted.Select(k => k.TrimEnd()).ToHashSet();
        var missing = existing.Where(e => e.Value.Length > 0 && !extracted.Contains(e.Key) && !normalized.Contains(e.Key.TrimEnd()))
            .Select(e => e.Key)
            .ToList();

        Assert.True(missing.Count < 50, $"{missing.Count} translated texts not extracted, e.g. {string.Join(" | ", missing.Take(5))}");
    }

    [GameFileFact]
    public void Merge_carries_over_every_translation_of_existing_translation()
    {
        var existing = TrsFile.Read(GamePath("Deutsch.trs"), 1252);

        MergeResult merge = TranslationMerger.Merge(Extracted.Value.ToTranslationDocument(), existing);

        Assert.Empty(merge.Obsolete);
        Assert.True(merge.SimilarCount > 0); // dialog options with trailing spaces, voice prefix changes
        Assert.Equal(Extracted.Value.Texts.Count, merge.ExactCount + merge.SimilarCount + merge.NewCount);
    }

    [GameFileFact]
    public void Existing_translation_compiles_and_reads_back()
    {
        using var tmp = new TempDir();
        GameInfo info = GameInfo.GetGameInfo(GamePath("Unavowed.exe"))!;
        TranslationDocument doc = TrsFile.Read(GamePath("Deutsch.trs"), info.TextCodepage);
        doc.Options.GameUid = info.UniqueId;
        doc.Options.GameName = info.GameTitle;
        string tra = tmp.File("Deutsch.tra");

        var warnings = TraFile.Write(tra, doc);
        var compiled = TraFile.Read(tra, info.TextCodepage);

        Assert.Empty(warnings);
        Assert.Equal(UnavowedUid, compiled.Options.GameUid);
        Assert.Equal(doc.Entries.Count(e => !string.IsNullOrWhiteSpace(e.Value)), compiled.Entries.Count);
        foreach (var (key, value) in compiled.Entries.Take(500))
            Assert.Equal(doc.Entries[key], value);
    }
}
