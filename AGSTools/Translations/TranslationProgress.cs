using System;
using System.Collections.Generic;
using System.Linq;

namespace AGSTools
{
    /// <summary>How many texts of a room, script or character are translated.</summary>
    /// <param name="Name">Room or script (e.g. "room22.crm"), source kind for texts without a place (e.g. "Gui"),
    /// or the speaking character; empty for texts without one.</param>
    public sealed record ProgressGroup(string Name, int Total, int Translated)
    {
        public int Open => Total - Translated;
        public double Percent => Total == 0 ? 100 : 100.0 * Translated / Total;
    }

    public enum ProgressGrouping { Place, Speaker }

    /// <summary>Translation progress grouped by place or speaker, from the origins of the context file.</summary>
    public static class TranslationProgress
    {
        /// <summary>
        /// The group a text belongs to; empty if its origin is unknown or it has no speaker. GUI texts form one
        /// group (their location is the control name, one per button).
        /// </summary>
        public static string GroupOf(TextOrigin? origin, ProgressGrouping grouping) =>
            origin == null ? ""
            : grouping == ProgressGrouping.Speaker ? origin.Speaker ?? ""
            : origin.Location.Length > 0 && origin.Source != nameof(TextSource.Gui) ? origin.Location : origin.Source;

        /// <summary>Groups in natural order ("room2" before "room10"), the empty group last.</summary>
        public static List<ProgressGroup> Compute(IEnumerable<(TextOrigin? Origin, bool Translated)> texts, ProgressGrouping grouping)
        {
            var groups = new Dictionary<string, (int Total, int Translated)>(StringComparer.Ordinal);
            foreach (var (origin, translated) in texts)
            {
                string name = GroupOf(origin, grouping);
                var (total, done) = groups.GetValueOrDefault(name);
                groups[name] = (total + 1, done + (translated ? 1 : 0));
            }
            return groups
                .Select(g => new ProgressGroup(g.Key, g.Value.Total, g.Value.Translated))
                .OrderBy(g => g.Name.Length == 0)
                .ThenBy(g => g.Name, NaturalComparer.Instance)
                .ToList();
        }

        /// <summary>Compares text with numbers by their value, ignoring case.</summary>
        private sealed class NaturalComparer : IComparer<string>
        {
            public static readonly NaturalComparer Instance = new();

            public int Compare(string? a, string? b)
            {
                a ??= "";
                b ??= "";
                int i = 0, j = 0;
                while (i < a.Length && j < b.Length)
                {
                    if (char.IsAsciiDigit(a[i]) && char.IsAsciiDigit(b[j]))
                    {
                        int si = i, sj = j;
                        while (i < a.Length && char.IsAsciiDigit(a[i])) i++;
                        while (j < b.Length && char.IsAsciiDigit(b[j])) j++;
                        string na = a[si..i].TrimStart('0'), nb = b[sj..j].TrimStart('0');
                        int byLength = na.Length.CompareTo(nb.Length);
                        if (byLength != 0)
                            return byLength;
                        int byDigits = string.CompareOrdinal(na, nb);
                        if (byDigits != 0)
                            return byDigits;
                        continue;
                    }
                    int c = char.ToUpperInvariant(a[i]).CompareTo(char.ToUpperInvariant(b[j]));
                    if (c != 0)
                        return c;
                    i++;
                    j++;
                }
                int rest = (a.Length - i).CompareTo(b.Length - j);
                return rest != 0 ? rest : string.CompareOrdinal(a, b);
            }
        }
    }
}
