using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;

namespace AGSTools
{
    /// <summary>
    /// Fixed translations of terms and names, so they are translated the same way everywhere.
    /// </summary>
    /// <remarks>
    /// Text file (UTF-8), one term per line: <c>original = translation</c>. Several accepted translations
    /// are separated by <c>|</c>; a term that stays untranslated is written as <c>Deacon = Deacon</c>.
    /// Lines starting with <c>#</c> are comments. Terms match whole words, ignoring case.
    /// </remarks>
    public sealed class Glossary
    {
        public sealed record Term(string Original, IReadOnlyList<string> Translations)
        {
            internal Regex Pattern { get; } = new(@"(?<!\w)" + Regex.Escape(Original) + @"(?!\w)", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
        }

        public List<Term> Terms { get; } = new();

        public static Glossary Read(string path) => Parse(File.ReadAllLines(path));

        public static Glossary Parse(IEnumerable<string> lines)
        {
            var glossary = new Glossary();
            foreach (string raw in lines)
            {
                string line = raw.Trim();
                int eq = line.IndexOf('=');
                if (line.Length == 0 || line.StartsWith('#') || eq <= 0)
                    continue;
                string original = line[..eq].Trim();
                var translations = line[(eq + 1)..].Split('|').Select(t => t.Trim()).Where(t => t.Length > 0).ToList();
                if (original.Length > 0 && translations.Count > 0)
                    glossary.Terms.Add(new Term(original, translations));
            }
            return glossary;
        }

        /// <summary>Terms that occur in <paramref name="original"/> but none of whose translations is in <paramref name="translation"/>.</summary>
        public IEnumerable<Term> FindViolations(string original, string translation) =>
            Terms.Where(t => t.Pattern.IsMatch(original)
                && !t.Translations.Any(tr => translation.Contains(tr, StringComparison.OrdinalIgnoreCase)));
    }
}
