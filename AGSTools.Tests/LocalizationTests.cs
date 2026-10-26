using System.Text.RegularExpressions;
using AGSFontEditor.Localization;

namespace AGSTools.Tests;

public class LocalizationTests
{
    // Texts that stay the same in every language
    private static readonly HashSet<string> Untranslated = new()
    {
        "AGS Translation Editor", "Version 2.0 (Avalonia)", "© 2015 Bernd Keilmann", "auto",
        "ä, U+201E, 0x84", "a, U+0061",
    };

    private static readonly Regex CodeKey = new(@"Loc\.[TF]\(((?:\s*(?:\w+ == \w+ \?|:)?\s*""(?:[^""\\]|\\.)*""\s*\+?)+)");
    private static readonly Regex Literal = new(@"""((?:[^""\\]|\\.)*)""");
    private static readonly Regex XamlKey = new(@"\{l:T '([^']*)'\}");
    private static readonly Regex Placeholder = new(@"\{(\d+)[^}]*\}");

    private static string RepoRoot()
    {
        for (var dir = new DirectoryInfo(AppContext.BaseDirectory); dir != null; dir = dir.Parent)
            if (File.Exists(Path.Combine(dir.FullName, "AGS_TranslationEditor.sln")))
                return dir.FullName;
        throw new DirectoryNotFoundException("Repository root not found");
    }

    /// <summary>The English keys used in the UI sources, with C# escapes resolved.</summary>
    private static IEnumerable<(string Key, string File)> UsedKeys()
    {
        string root = RepoRoot();
        foreach (string project in new[] { "AGS_TranslationEditor.Avalonia", "AGSFontEditor" })
        {
            foreach (string file in Directory.EnumerateFiles(Path.Combine(root, project), "*.*", SearchOption.AllDirectories))
            {
                if (file.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}")
                    || file.Contains($"{Path.DirectorySeparatorChar}bin{Path.DirectorySeparatorChar}")
                    || file.Contains("Localization"))
                    continue;
                string text = File.ReadAllText(file);
                if (file.EndsWith(".cs"))
                {
                    foreach (Match call in CodeKey.Matches(text))
                    {
                        // A ternary chooses between two keys; otherwise concatenated parts form one key
                        var parts = Literal.Matches(call.Groups[1].Value).Select(m => Regex.Unescape(m.Groups[1].Value)).ToList();
                        if (Literal.Replace(call.Groups[1].Value, "").Contains('?'))
                            foreach (string part in parts) yield return (part, file);
                        else
                            yield return (string.Concat(parts), file);
                    }
                }
                else if (file.EndsWith(".axaml"))
                {
                    foreach (Match m in XamlKey.Matches(text))
                        yield return (System.Net.WebUtility.HtmlDecode(m.Groups[1].Value), file);
                }
            }
        }
    }

    [Fact]
    public void Every_UI_text_has_a_German_translation()
    {
        var missing = UsedKeys()
            .Where(k => !Untranslated.Contains(k.Key) && !StringsDe.Table.ContainsKey(k.Key))
            .Select(k => $"{Path.GetFileName(k.File)}: {k.Key}")
            .Distinct()
            .ToList();
        Assert.True(missing.Count == 0, "Missing German texts:\n" + string.Join("\n", missing));
    }

    [Fact]
    public void German_texts_keep_the_placeholders()
    {
        foreach (var (english, german) in StringsDe.Table)
        {
            var expected = Placeholder.Matches(english).Select(m => m.Value).Order();
            var actual = Placeholder.Matches(german).Select(m => m.Value).Order();
            Assert.True(expected.SequenceEqual(actual), $"Placeholders differ: \"{english}\" → \"{german}\"");
            Assert.Equal(english.Count(c => c == '\n'), german.Count(c => c == '\n'));
        }
    }

    [Fact]
    public void Language_falls_back_to_English()
    {
        try
        {
            Loc.SetLanguage(Loc.German);
            Assert.Equal("_Datei", Loc.T("_File"));
            Assert.Equal("3 Einträge mit DeepL übersetzt", Loc.F("Translated {0} entries with {1}", 3, "DeepL"));
            Assert.Equal("Not in the table", Loc.T("Not in the table"));
            Loc.SetLanguage("fr");
            Assert.Equal("_File", Loc.T("_File"));
        }
        finally
        {
            Loc.SetLanguage(Loc.English);
        }
    }
}
