using System;
using System.Collections.Generic;
using System.Text.RegularExpressions;

namespace AGSTools
{
    /// <summary>How an entry of the merged translation got its translation.</summary>
    public enum MergeMatch
    {
        /// <summary>Not in the existing translation (or untranslated there).</summary>
        New,
        /// <summary>Same source text.</summary>
        Exact,
        /// <summary>Source differs only in leading/trailing whitespace or the "&amp;N " voice prefix.</summary>
        Similar,
    }

    public sealed class MergeResult
    {
        public TranslationDocument Document { get; } = new();

        /// <summary>Match kind per source text of <see cref="Document"/>.</summary>
        public Dictionary<string, MergeMatch> Matches { get; } = new(StringComparer.Ordinal);

        public int ExactCount { get; internal set; }
        public int SimilarCount { get; internal set; }
        public int NewCount { get; internal set; }

        /// <summary>
        /// Translated entries of the existing translation whose source no longer exists in the game
        /// (texts without letters, which extraction skips, are not listed).
        /// </summary>
        public List<KeyValuePair<string, string>> Obsolete { get; } = new();
    }

    /// <summary>
    /// Merges an existing translation into a freshly extracted text list: the result contains exactly the
    /// game's texts in game order, with the existing translations carried over.
    /// </summary>
    public static class TranslationMerger
    {
        private static readonly Regex VoicePrefixRe = new(@"^&\d+ ", RegexOptions.Compiled);

        /// <param name="extracted">Current texts of the game (e.g. <see cref="ExtractionResult.ToTranslationDocument"/>).</param>
        /// <param name="existing">The translation to carry over.</param>
        /// <param name="keepObsolete">
        /// Append translated entries that no longer match any game text at the end of the result.
        /// The engine ignores them, but no work is lost.
        /// </param>
        public static MergeResult Merge(TranslationDocument extracted, TranslationDocument existing, bool keepObsolete = true)
        {
            var result = new MergeResult();
            TranslationDocument doc = result.Document;

            // The translator's settings stay; game identification comes from the game
            doc.Options = existing.Options.Clone();
            if (extracted.Options.GameUid != 0)
                doc.Options.GameUid = extracted.Options.GameUid;
            if (extracted.Options.GameName.Length > 0)
                doc.Options.GameName = extracted.Options.GameName;
            doc.Options.AnsiCodepage = extracted.Options.AnsiCodepage;
            if (extracted.Options.IsUtf8)
                doc.Options.Encoding = extracted.Options.Encoding;

            // Translated entries by normalized source; the first one wins
            var byNormalized = new Dictionary<string, string>(StringComparer.Ordinal);
            foreach (var (source, translation) in existing.Entries)
            {
                if (!string.IsNullOrWhiteSpace(translation))
                    byNormalized.TryAdd(Normalize(source), source);
            }

            var used = new HashSet<string>(StringComparer.Ordinal);
            foreach (string source in extracted.Entries.Keys)
            {
                MergeMatch match = MergeMatch.New;
                string translation = "";

                if (existing.Entries.TryGetValue(source, out string? exact) && !string.IsNullOrWhiteSpace(exact))
                {
                    match = MergeMatch.Exact;
                    translation = exact;
                    used.Add(source);
                }
                else if (byNormalized.TryGetValue(Normalize(source), out string? oldSource))
                {
                    match = MergeMatch.Similar;
                    translation = AdjustVoicePrefix(source, existing.Entries[oldSource]);
                    used.Add(oldSource);
                }

                doc.Entries[source] = translation;
                result.Matches[source] = match;
                switch (match)
                {
                    case MergeMatch.Exact: result.ExactCount++; break;
                    case MergeMatch.Similar: result.SimilarCount++; break;
                    default: result.NewCount++; break;
                }
            }

            foreach (var entry in existing.Entries)
            {
                if (string.IsNullOrWhiteSpace(entry.Value) || used.Contains(entry.Key) || doc.Entries.ContainsKey(entry.Key))
                    continue;
                if (keepObsolete)
                    doc.Entries[entry.Key] = entry.Value;
                // Texts without letters ("&12 ...") are left out of extractions on purpose, they are not obsolete
                if (TextFilters.HasLetters(Normalize(entry.Key)))
                    result.Obsolete.Add(entry);
            }
            return result;
        }

        /// <summary>Source text without voice prefix and surrounding whitespace.</summary>
        private static string Normalize(string source) => VoicePrefixRe.Replace(source, "").Trim();

        /// <summary>Gives the translation the voice prefix of the (new) source, or removes it if the source has none.</summary>
        private static string AdjustVoicePrefix(string source, string translation)
        {
            Match prefix = VoicePrefixRe.Match(source);
            string text = VoicePrefixRe.Replace(translation, "");
            return prefix.Success ? prefix.Value + text : text;
        }
    }
}
