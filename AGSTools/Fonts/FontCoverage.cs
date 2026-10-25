using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;

namespace AGSTools
{
    /// <summary>
    /// Checks whether the game fonts can draw the non-ASCII characters of a translation.
    /// </summary>
    /// <remarks>
    /// The engine looks glyphs up by a character code that depends on the translation's encoding
    /// (Engine/ac/translation.cpp switches the whole text mode): with a UTF-8 translation the code is
    /// the Unicode code point ("„" = U+201E), with an 8-bit translation it is the byte in the game's
    /// codepage ("„" = 0x84 in 1252, so a TTF font needs a glyph at U+0084).
    /// </remarks>
    public static class FontCoverage
    {
        /// <summary>A character missing (or drawn wrongly) in a font.</summary>
        public sealed record Issue(GameFont Font, string Character, int Code, int Count, bool LooksLikeBaseLetter, string BaseLetter);

        /// <summary>Character code the engine looks up for <paramref name="character"/>, or -1 if it cannot be encoded.</summary>
        public static int EngineCode(string character, TranslationOptions options)
        {
            if (options.IsUtf8)
                return char.ConvertToUtf32(character, 0);
            byte[] bytes = options.GetTextEncoding().GetBytes(character);
            return bytes.Length == 1 && options.GetTextEncoding().GetString(bytes) == character ? bytes[0] : -1;
        }

        public static List<Issue> FindIssues(TranslationDocument doc, IEnumerable<GameFont> fonts)
        {
            // Non-ASCII characters of all translations with their number of uses
            var counts = new Dictionary<string, int>(StringComparer.Ordinal);
            foreach (string translation in doc.Entries.Values)
            {
                if (string.IsNullOrWhiteSpace(translation))
                    continue;
                foreach (Rune rune in TextEscaping.ToRuntime(translation).EnumerateRunes())
                {
                    // Invisible format characters (zero-width space, ...) are not drawn
                    if (rune.Value < 0x80 || Rune.GetUnicodeCategory(rune) == UnicodeCategory.Format)
                        continue;
                    string s = rune.ToString();
                    counts[s] = counts.GetValueOrDefault(s) + 1;
                }
            }

            var issues = new List<Issue>();
            foreach (GameFont font in fonts.Where(f => f.Error == null))
            {
                foreach (var (character, count) in counts.OrderByDescending(c => c.Value).ThenBy(c => c.Key, StringComparer.Ordinal))
                {
                    int code = EngineCode(character, doc.Options);
                    if (code < 0)
                        continue; // not encodable, the TRA writer reports this
                    string baseLetter = BaseLetter(character);
                    int baseCode = baseLetter[0];
                    // A font without the base letter (e.g. caps only) is not used for such text
                    if (!font.HasGlyph(baseCode))
                        continue;

                    if (!font.HasGlyph(code))
                        issues.Add(new Issue(font, character, code, count, false, baseLetter));
                    else if (baseLetter != character && char.IsLetter(baseLetter[0]) && font.HasSameGlyph(code, baseCode))
                        issues.Add(new Issue(font, character, code, count, true, baseLetter));
                }
            }
            return issues;
        }

        /// <summary>Loads the fonts of the game at <paramref name="gamePath"/> and checks them; never throws.</summary>
        public static List<string> CheckGame(TranslationDocument doc, string gamePath)
        {
            try
            {
                return Check(doc, GameFonts.Load(gamePath));
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                return new List<string> { $"The game fonts could not be checked: {ex.Message}" };
            }
        }

        /// <summary>Warnings for <see cref="FindIssues"/>, one line per font.</summary>
        public static List<string> Check(TranslationDocument doc, IEnumerable<GameFont> fonts)
        {
            var warnings = new List<string>();
            List<Issue> issues = FindIssues(doc, fonts);
            foreach (var group in issues.GroupBy(i => i.Font))
            {
                var missing = group.Where(i => !i.LooksLikeBaseLetter).ToList();
                var same = group.Where(i => i.LooksLikeBaseLetter).ToList();
                if (missing.Count > 0)
                    warnings.Add($"{group.Key.DisplayName}: no glyph for {string.Join(", ", missing.Select(Describe))}");
                if (same.Count > 0)
                    warnings.Add($"{group.Key.DisplayName}: {string.Join(" ", same.Select(i => i.Character))} look identical to "
                        + $"{string.Join(" ", same.Select(i => i.BaseLetter))} (accents or umlaut dots missing in the font)");
            }
            if (!doc.Options.IsUtf8 && issues.Any(i => i.Font.Kind == FontKind.Ttf && i.Code is >= 0x80 and < 0xA0))
                warnings.Add("In an 8-bit translation the engine looks up characters like „ “ – … by their codepage byte (0x80-0x9F), "
                    + "which TrueType fonts do not map. A UTF-8 translation (//#Encoding=UTF-8) uses the real Unicode characters.");
            if (warnings.Count > 0)
                warnings.Add("Not every font is used for translated text; fonts that only show e.g. symbols or titles can be ignored.");
            return warnings;

            static string Describe(Issue i) =>
                i.Code >= 0x80 && i.Code < 0x100 && i.Code != char.ConvertToUtf32(i.Character, 0)
                    ? $"{i.Character} (code {i.Code}, {i.Count}×)"
                    : $"{i.Character} ({i.Count}×)";
        }

        /// <summary>
        /// The ASCII character a font needs for this character to be relevant: the base letter of an
        /// accented letter, a quote for quotation marks, and so on.
        /// </summary>
        internal static string BaseLetter(string character)
        {
            switch (character)
            {
                case "ß": case "ẞ": return "s";
                case "„": case "“": case "”": case "«": case "»": return "\"";
                case "‚": case "‘": case "’": case "‹": case "›": return "'";
                case "–": case "—": return "-";
                case "…": return ".";
            }
            string decomposed = character.Normalize(NormalizationForm.FormD);
            return decomposed.Length > 0 && decomposed[0] < 0x80 && char.IsLetter(decomposed[0])
                && decomposed.Skip(1).All(c => CharUnicodeInfo.GetUnicodeCategory(c) == UnicodeCategory.NonSpacingMark)
                ? decomposed[0].ToString()
                : "a";
        }
    }
}
