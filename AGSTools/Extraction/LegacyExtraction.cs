using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace AGSTools
{
    /// <summary>
    /// Heuristic text extraction for game data the structural reader does not support (AGS 2.x).
    /// Scripts are located by signature; names and messages are found by scanning for
    /// arrays of fixed-size, null-padded strings and length-prefixed strings.
    /// </summary>
    internal static class LegacyExtraction
    {
        // AGS 2.x games have no codepage setting; they used the system ANSI codepage
        private static readonly Encoding Ansi = AgsTextEncoding.ForCodepage(0);

        public static IEnumerable<string> Extract(byte[] gameData, GameFileSource source, IProgress<string>? progress)
        {
            var scripts = new List<CompiledScript>(TextExtractor.ScanForScripts(gameData, Ansi));

            progress?.Report("Scanning rooms...");
            foreach (var (_, read) in source.EnumerateRooms())
                scripts.AddRange(TextExtractor.ScanForScripts(read(), Ansi));

            var voiceNumbers = new Dictionary<string, int>(StringComparer.Ordinal);
            foreach (CompiledScript script in scripts)
                foreach (var (text, number) in script.FindVoicedStrings())
                    voiceNumbers.TryAdd(text, number);

            foreach (CompiledScript script in scripts)
            {
                foreach (var (_, text) in script.Strings.Where(s => TextFilters.IsLikelyDisplayText(s.Text)))
                {
                    bool alreadyVoiced = text.Length > 1 && text[0] == '&' && char.IsDigit(text[1]);
                    yield return !alreadyVoiced && voiceNumbers.TryGetValue(text, out int n) ? $"&{n} {text}" : text;
                }
            }

            // Character names (41), inventory names (26), dialog options (151), cursor names (11)
            foreach (int slotSize in new[] { 41, 26, 151, 11 })
                foreach (string s in FindStringArrays(gameData, slotSize, minCount: 4))
                    yield return s;

            foreach (string s in FindLengthPrefixedStrings(gameData))
                yield return s;
        }

        /// <summary>Runs of at least <paramref name="minCount"/> fixed-size, null-padded text slots.</summary>
        private static IEnumerable<string> FindStringArrays(byte[] data, int slotSize, int minCount)
        {
            var pending = new List<string>();
            int run = 0;
            for (long pos = 0; pos <= data.Length - slotSize;)
            {
                if (IsNullPaddedText(data, pos, slotSize))
                {
                    run++;
                    string s = ReadNullTerminated(data, pos, slotSize);
                    if (s.Length > 0)
                        pending.Add(s);
                    pos += slotSize;
                    continue;
                }

                if (run >= minCount)
                    foreach (string s in pending.Where(TextFilters.IsLikelyDisplayText))
                        yield return s;
                pending.Clear();
                run = 0;
                pos++;
            }
            if (run >= minCount)
                foreach (string s in pending.Where(TextFilters.IsLikelyDisplayText))
                    yield return s;
        }

        private static IEnumerable<string> FindLengthPrefixedStrings(byte[] data)
        {
            for (long pos = 0; pos < data.Length - 4;)
            {
                int len = BitConverter.ToInt32(data, (int)pos);
                if (len > 4 && len < 1000 && pos + 4 + len <= data.Length &&
                    Array.IndexOf(data, (byte)0, (int)pos + 4, len) < 0)
                {
                    string s = Ansi.GetString(data, (int)pos + 4, len);
                    if (TextFilters.IsLikelyDisplayText(s))
                    {
                        yield return s;
                        pos += 4 + len;
                        continue;
                    }
                }
                pos++;
            }
        }

        private static bool IsNullPaddedText(byte[] data, long offset, int size)
        {
            bool foundNull = false, foundText = false;
            for (int i = 0; i < size; i++)
            {
                byte b = data[offset + i];
                if (b == 0)
                    foundNull = true;
                else if (foundNull)
                    return false; // data after the terminator
                else if (b >= 0x20 && b <= 0x7E || b >= 0xA0)
                    foundText = true;
                else
                    return false;
            }
            return foundNull && foundText;
        }

        private static string ReadNullTerminated(byte[] data, long offset, int maxLen)
        {
            int len = 0;
            while (len < maxLen && data[offset + len] != 0)
                len++;
            return Ansi.GetString(data, (int)offset, len);
        }
    }
}
