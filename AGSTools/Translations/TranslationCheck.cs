using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;

namespace AGSTools
{
    public enum CheckSeverity { Error, Warning, Info }

    /// <summary>A problem found in a translation. <see cref="Key"/> is the entry's source text as written in the TRS.</summary>
    public sealed record CheckIssue(CheckSeverity Severity, string Category, string Key, string Message);

    /// <summary>
    /// Checks a translation for mistakes that otherwise only show up while playing: broken placeholders and
    /// voice prefixes, inconsistent translations, glossary terms, missing names, texts that do not fit
    /// into their GUI buttons and labels, and texts the scripts put together at runtime.
    /// </summary>
    public static class TranslationCheck
    {
        public const string Placeholders = "Placeholders";
        public const string VoicePrefix = "Voice prefix";
        public const string LineBreaks = "Line breaks";
        public const string Spaces = "Spaces";
        public const string Inconsistent = "Inconsistent";
        public const string GlossaryTerm = "Glossary";
        public const string Names = "Names";
        public const string TooWide = "Too wide";
        public const string TooManyLines = "Too many lines";
        public const string FormalAddress = "Formal address (Sie)";
        public const string BuiltAtRuntime = "Built at runtime";

        private static readonly Regex VoicePrefixPattern = new(@"^&\d+ ");
        private static readonly Regex FormatPattern = new(@"%[-+ 0#]*\d*(?:\.\d+)?[sdifcxX]");
        private static readonly Regex TokenPattern = new(@"@[A-Za-z0-9_]+@");
        private static readonly Regex FormalPattern = new(@"(?<=[\p{Ll},;:] )(?:Sie|Ihnen|Ihr|Ihre|Ihren|Ihrem|Ihrer|Ihres)\b");

        /// <param name="game">Extracted game texts; enables the name and GUI checks.</param>
        /// <param name="fonts">Game fonts; with <paramref name="game"/> enables the GUI size checks.</param>
        /// <param name="glossary">Fixed translations of terms.</param>
        public static List<CheckIssue> Run(TranslationDocument doc, ExtractionResult? game = null,
            IReadOnlyList<GameFont>? fonts = null, Glossary? glossary = null)
        {
            var issues = new List<CheckIssue>();
            List<string> names = game?.Game != null ? KeptNameWords(game.Game.CharacterNames, doc) : new List<string>();
            foreach (var (key, value) in doc.Entries)
            {
                if (string.IsNullOrWhiteSpace(value))
                    continue;
                string original = TextEscaping.ToRuntime(key), translation = TextEscaping.ToRuntime(value);
                CheckEntry(issues, key, original, translation, glossary, names);
            }
            CheckConsistency(issues, doc);
            if (game?.Game != null && fonts != null)
                CheckGuiSizes(issues, doc, game, fonts);
            if (game != null)
                CheckRuntimeFormats(issues, doc, game);
            return issues;
        }

        private static void CheckEntry(List<CheckIssue> issues, string key, string original, string translation,
            Glossary? glossary, List<string> names)
        {
            void Add(CheckSeverity severity, string category, string message) => issues.Add(new CheckIssue(severity, category, key, message));

            var formats = FormatPattern.Matches(original).Select(m => m.Value).ToList();
            var translatedFormats = FormatPattern.Matches(translation).Select(m => m.Value).ToList();
            if (!formats.SequenceEqual(translatedFormats))
                Add(CheckSeverity.Error, Placeholders, $"placeholders {List(formats)} in the original, {List(translatedFormats)} in the translation; "
                    + "the game fills them in this order");
            var tokens = TokenPattern.Matches(original).Select(m => m.Value).OrderBy(t => t).ToList();
            var translatedTokens = TokenPattern.Matches(translation).Select(m => m.Value).OrderBy(t => t).ToList();
            if (!tokens.SequenceEqual(translatedTokens))
                Add(CheckSeverity.Error, Placeholders, $"special texts {List(tokens)} in the original, {List(translatedTokens)} in the translation");

            Match prefix = VoicePrefixPattern.Match(original);
            if (prefix.Success && !translation.StartsWith(prefix.Value, StringComparison.Ordinal))
                Add(CheckSeverity.Warning, VoicePrefix, $"the translation does not start with \"{prefix.Value.TrimEnd()}\"; the game plays no speech for it");

            int breaks = CountBreaks(original), translatedBreaks = CountBreaks(translation);
            if (breaks != translatedBreaks)
                Add(CheckSeverity.Info, LineBreaks, $"{breaks} line break(s) \"[\" in the original, {translatedBreaks} in the translation");

            // Spoken lines are shown on their own; spaces matter where texts are put together or listed
            if (!prefix.Success && StartsOrEndsWithSpace(original) != StartsOrEndsWithSpace(translation))
                Add(CheckSeverity.Warning, Spaces, "spaces at the start or end differ from the original (matters for dialog options and composed texts)");

            if (glossary != null)
                foreach (Glossary.Term term in glossary.FindViolations(original, translation))
                    Add(CheckSeverity.Warning, GlossaryTerm, $"\"{term.Original}\" should be translated as {string.Join(" or ", term.Translations.Select(t => $"\"{t}\""))}");

            foreach (string name in names)
                if (Regex.IsMatch(original, $@"(?<!\w){Regex.Escape(name)}(?!\w)") && !translation.Contains(name, StringComparison.Ordinal))
                    Add(CheckSeverity.Info, Names, $"the name \"{name}\" is missing in the translation");

            foreach (Match m in FormalPattern.Matches(translation).Take(1))
                Add(CheckSeverity.Info, FormalAddress, $"uses \"{m.Value}\": check that this character is addressed formally everywhere");
        }

        // Same source text (ignoring voice prefixes and spaces) translated in different ways. Short spoken
        // lines like "All right." may well differ, so only lines of more than three words count there.
        private static void CheckConsistency(List<CheckIssue> issues, TranslationDocument doc)
        {
            var groups = doc.Entries
                .Where(e => !string.IsNullOrWhiteSpace(e.Value))
                .GroupBy(e => Normalize(e.Key))
                .Where(g => g.Any(e => !VoicePrefixPattern.IsMatch(e.Key)) || g.Key.Split(' ').Length > 3);
            foreach (var group in groups)
            {
                var variants = group.GroupBy(e => Normalize(e.Value)).OrderByDescending(v => v.Count()).ToList();
                if (variants.Count < 2)
                    continue;
                string list = string.Join(", ", variants.Select(v => $"\"{TextEscaping.ToRuntime(v.Key)}\" ({v.Count()}×)"));
                foreach (var entry in group)
                    issues.Add(new CheckIssue(CheckSeverity.Info, Inconsistent, entry.Key, $"translated in {variants.Count} ways: {list}"));
            }
        }

        private static void CheckGuiSizes(List<CheckIssue> issues, TranslationDocument doc, ExtractionResult game, IReadOnlyList<GameFont> fonts)
        {
            var metrics = new Dictionary<int, FontMetrics?>();
            FontMetrics? Metrics(int number)
            {
                if (!metrics.TryGetValue(number, out FontMetrics? m))
                    metrics[number] = m = fonts.FirstOrDefault(f => f.Number == number) is { } font ? FontMetrics.For(font) : null;
                return m;
            }
            string? Translate(string runtimeText) =>
                doc.Entries.TryGetValue(TextEscaping.ToTrs(runtimeText), out string? value) && !string.IsNullOrWhiteSpace(value)
                    ? TextEscaping.ToRuntime(value)
                    : null;

            // Names of what is under the mouse, shown by labels with @OVERHOTSPOT@
            var hoverNames = game.Texts
                .Where(t => t.Source is TextSource.Hotspot or TextSource.RoomObject or TextSource.CharacterName or TextSource.InventoryItem)
                .Select(t => t.Text).Distinct().ToList();

            foreach (GuiText control in game.Game!.GuiTexts)
            {
                if (control.Width <= 0 || control.Height <= 0 || Metrics(control.Font) is not { } font)
                    continue;
                string where = $"{control.Kind} {(control.ControlName.Length > 0 ? $"\"{control.ControlName}\"" : "")} (font {control.Font}, {control.Width}×{control.Height} px)".Replace("  ", " ");
                string approx = font.IsApproximate ? " (TrueType: estimated)" : "";

                if (control.Text.Contains("@OVERHOTSPOT@", StringComparison.Ordinal))
                {
                    foreach (string name in hoverNames)
                        if (Translate(name) is { } translatedName)
                            CheckText(issues, TextEscaping.ToTrs(name), control,
                                control.Text.Replace("@OVERHOTSPOT@", name), control.Text.Replace("@OVERHOTSPOT@", translatedName),
                                font, doc.Options, $"shown in {where} when under the mouse", approx);
                    continue;
                }
                if (TokenPattern.IsMatch(control.Text) || Translate(control.Text) is not { } translation)
                    continue;
                CheckText(issues, TextEscaping.ToTrs(control.Text), control, control.Text, translation, font, doc.Options, where, approx);
            }
        }

        private static void CheckText(List<CheckIssue> issues, string key, GuiText control, string original, string translation,
            FontMetrics font, TranslationOptions options, string where, string approx)
        {
            if (control.Kind == GuiTextKind.Label)
            {
                // Labels wrap their text; it is cut off when the lines need more height than the label has
                int lines = font.Wrap(translation, control.Width, options).Count;
                int originalLines = font.Wrap(original, control.Width, options).Count;
                int needed = (lines - 1) * font.LineHeight + font.Height;
                if (needed > control.Height && lines > originalLines)
                    issues.Add(new CheckIssue(CheckSeverity.Warning, TooManyLines, key,
                        $"{where}: needs {lines} lines ({needed} px), only {control.Height} px high{approx}"));
                return;
            }
            // Buttons and list boxes show one line and cut off the rest
            int width = font.MeasureWidth(translation, options);
            if (width > control.Width && width > font.MeasureWidth(original, options))
                issues.Add(new CheckIssue(CheckSeverity.Warning, TooWide, key,
                    $"{where}: text is {width} px wide, {width - control.Width} px too wide{approx}"));
        }

        // A text like String.Format("%s[[%s", title, text) shown on a label: the engine looks up the finished
        // text, which is in no TRS, so it stays in the original language although the parts are translated.
        private static void CheckRuntimeFormats(List<CheckIssue> issues, TranslationDocument doc, ExtractionResult game)
        {
            if (game.RuntimeFormats.Count == 0)
                return;
            var gameTexts = new HashSet<string>(game.Texts.Select(t => TextEscaping.ToTrs(t.Text)), StringComparer.Ordinal);
            foreach (var group in game.RuntimeFormats.GroupBy(f => f.Format))
            {
                var places = group.Select(f => $"{f.Location} line {f.Line}").Distinct().ToList();
                string where = string.Join(", ", places.Take(3)) + (places.Count > 3 ? $" and {places.Count - 3} more" : "");
                string key = TextEscaping.ToTrs(group.Key);
                string example = FormatPattern.Replace(group.Key, "…");
                string? shownBy = group.Select(f => f.ShownBy).FirstOrDefault(s => s != null);

                // Entries for the finished texts may already be there ("Title[[Text" → "Titel[[Text"): added by hand,
                // so they are not among the game's texts
                var finished = new Regex("^" + string.Join(".+", FormatPattern.Split(group.Key).Select(Regex.Escape)) + "$", RegexOptions.Singleline);
                int covered = doc.Entries.Count(e => !string.IsNullOrWhiteSpace(e.Value) && !gameTexts.Contains(e.Key)
                    && FormatPattern.Split(group.Key).Any(part => part.Length > 0) && finished.IsMatch(TextEscaping.ToRuntime(e.Key)));
                if (covered > 0)
                {
                    issues.Add(new CheckIssue(CheckSeverity.Info, BuiltAtRuntime, key,
                        $"{where}: the script builds texts with String.Format; {covered} translated entries for finished texts like \"{example}\" exist. "
                        + "Make sure every combination the game can show is among them."));
                    continue;
                }
                string fix = $"add entries for the finished texts (key: the original result, like \"{example}\") or have the parts translated with GetTranslation()";
                issues.Add(shownBy != null
                    ? new CheckIssue(CheckSeverity.Warning, BuiltAtRuntime, key,
                        $"{where}: the script builds this text with String.Format and shows it with {shownBy}. The engine looks up "
                        + $"the finished text, so the translations of the parts are not used and it stays in the original language; {fix}")
                    : new CheckIssue(CheckSeverity.Info, BuiltAtRuntime, key,
                        $"{where}: the script builds a text with String.Format. If it is shown to the player, the engine looks up "
                        + $"the finished text, not its parts; {fix}"));
            }
        }

        // Words of character names that the translation keeps in the translated name ("Rosa Blackwell" →
        // "Rosa Blackwell"), so they should appear in translated lines too. "The Deacon" → "Der Diakon" adds none.
        private static List<string> KeptNameWords(IEnumerable<string> characterNames, TranslationDocument doc) =>
            characterNames
                .Where(n => doc.Entries.TryGetValue(TextEscaping.ToTrs(n), out string? t) && !string.IsNullOrWhiteSpace(t))
                .SelectMany(n => n.Split(' ', StringSplitOptions.RemoveEmptyEntries)
                    .Where(w => doc.Entries[TextEscaping.ToTrs(n)]!.Split(' ').Contains(w, StringComparer.Ordinal)))
                .Where(w => w.Length >= 3 && char.IsUpper(w[0]) && w.All(char.IsLetter) && !TextFilters.IsPlaceholderName(w))
                .Distinct()
                .ToList();

        private static string Normalize(string text) =>
            Regex.Replace(VoicePrefixPattern.Replace(TextEscaping.ToRuntime(text), ""), @"\s+", " ").Trim();

        private static int CountBreaks(string text) => Regex.Matches(text, @"(?<!\\)\[").Count;

        private static bool StartsOrEndsWithSpace(string text) => text.Length > 0 && (text[0] == ' ' || text[^1] == ' ');

        private static string List(List<string> items) => items.Count == 0 ? "none" : string.Join(" ", items);
    }
}
