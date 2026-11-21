using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using WeCantSpell.Hunspell;

namespace AGSTools
{
    /// <summary>A word the dictionary does not know, at a position in the text.</summary>
    public sealed record SpellingError(int Start, int Length, string Word);

    /// <summary>
    /// Spell checking with Hunspell dictionaries (the .dic/.aff files of LibreOffice and Firefox). The dictionaries are
    /// not shipped (their licenses differ); they are found in the user's dictionary folder, in the system folders on
    /// Linux, or in a LibreOffice installation. Words in AGS markup (placeholders, tokens, voice prefixes) are skipped,
    /// and so are words that also appear in the original text, which covers most names.
    /// </summary>
    public sealed class SpellChecker
    {
        private static readonly Regex MarkupPattern = new(@"^&\d+ |\\\[|%[-+ 0#]*\d*(?:\.\d+)?[sdifcxX]|@[A-Za-z0-9_]+@|\[");
        private static readonly Regex WordPattern = new(@"\p{L}[\p{L}\p{M}'’\-]*\p{L}|\p{L}");

        private readonly WordList _words;
        private readonly HashSet<string> _extra = new(StringComparer.Ordinal);
        private readonly ConcurrentDictionary<string, IReadOnlyList<string>> _suggestions = new(StringComparer.Ordinal);
        private readonly ConcurrentDictionary<string, bool> _known = new(StringComparer.Ordinal);

        private SpellChecker(string name, string dicPath, WordList words)
        {
            Name = name;
            DictionaryPath = dicPath;
            _words = words;
        }

        /// <summary>Dictionary name such as "de_DE".</summary>
        public string Name { get; }

        public string DictionaryPath { get; }

        /// <summary>Folder for dictionaries the user adds: ~/.config/AGSTranslationEditor/dictionaries or %APPDATA%\...</summary>
        public static string UserFolder => Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            "AGSTranslationEditor", "dictionaries");

        /// <summary>The user's own word list, shared by all translations.</summary>
        public static string UserWordsFile => Path.Combine(UserFolder, "words.txt");

        /// <summary>Folders searched for dictionaries, the user folder first.</summary>
        public static IEnumerable<string> SearchFolders()
        {
            yield return UserFolder;
            foreach (string folder in new[] { "/usr/share/hunspell", "/usr/share/myspell", "/usr/share/myspell/dicts", "/usr/local/share/hunspell",
                         "/Library/Spelling", Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "Library", "Spelling") })
                yield return folder;
            foreach (string programs in new[] { Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles),
                         Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86) }.Where(p => p.Length > 0))
            {
                string extensions = Path.Combine(programs, "LibreOffice", "share", "extensions");
                if (Directory.Exists(extensions))
                    foreach (string dir in SafeDirectories(extensions))
                        yield return dir;
            }
        }

        /// <summary>The dictionaries found: name (e.g. "de_DE") → path of the .dic file.</summary>
        public static SortedDictionary<string, string> FindDictionaries()
        {
            var result = new SortedDictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            foreach (string folder in SearchFolders().Where(Directory.Exists))
            {
                IEnumerable<string> files;
                try
                {
                    files = Directory.EnumerateFiles(folder, "*.dic").ToList();
                }
                catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
                {
                    continue;
                }
                foreach (string dic in files)
                {
                    string name = Path.GetFileNameWithoutExtension(dic);
                    if (File.Exists(Path.ChangeExtension(dic, ".aff")) && !result.ContainsKey(name) && !name.StartsWith("hyph", StringComparison.OrdinalIgnoreCase))
                        result[name] = dic;
                }
            }
            return result;
        }

        /// <summary>
        /// The dictionary for a name ("de_DE") or language code ("de" → de_DE, else the first de_*), or null if none is installed.
        /// </summary>
        public static string? FindDictionary(string language)
        {
            if (string.IsNullOrWhiteSpace(language))
                return null;
            var all = FindDictionaries();
            string code = language.Trim().Replace('-', '_');
            if (all.TryGetValue(code, out string? exact))
                return exact;
            string lang = code.Split('_')[0].ToLowerInvariant();
            string preferred = lang switch { "en" => "en_US", "pt" => "pt_BR", "sv" => "sv_SE", "da" => "da_DK", "cs" => "cs_CZ", _ => $"{lang}_{lang.ToUpperInvariant()}" };
            if (all.TryGetValue(preferred, out string? main))
                return main;
            return all.FirstOrDefault(d => d.Key.Equals(lang, StringComparison.OrdinalIgnoreCase)
                || d.Key.StartsWith(lang + "_", StringComparison.OrdinalIgnoreCase)).Value;
        }

        /// <summary>Loads a dictionary (.dic with its .aff next to it).</summary>
        /// <exception cref="IOException">The files cannot be read.</exception>
        public static SpellChecker Load(string dicPath)
        {
            string aff = Path.ChangeExtension(dicPath, ".aff");
            if (!File.Exists(dicPath) || !File.Exists(aff))
                throw new FileNotFoundException($"Dictionary '{dicPath}' or its .aff file is missing.");
            WordList words;
            try
            {
                words = WordList.CreateFromFiles(dicPath, aff);
            }
            catch (Exception ex) when (ex is not IOException and not OutOfMemoryException)
            {
                throw new IOException($"Dictionary '{Path.GetFileName(dicPath)}' cannot be read: {ex.Message}", ex);
            }
            var checker = new SpellChecker(Path.GetFileNameWithoutExtension(dicPath), dicPath, words);
            checker.AddWordsFrom(UserWordsFile);
            return checker;
        }

        /// <summary>The dictionary for a language, or null if none is installed.</summary>
        public static SpellChecker? ForLanguage(string language) => FindDictionary(language) is { } dic ? Load(dic) : null;

        /// <summary>Accepts the words of a word list file (one per line, # comments); a missing file is fine.</summary>
        public void AddWordsFrom(string path)
        {
            if (!File.Exists(path))
                return;
            foreach (string line in File.ReadLines(path))
            {
                string word = line.Trim();
                if (word.Length > 0 && !word.StartsWith('#'))
                    AddWord(word);
            }
        }

        public void AddWord(string word)
        {
            lock (_extra)
                _extra.Add(word);
            _known.TryRemove(word, out _);
        }

        /// <summary>Adds a word to the user's word list file and to this checker.</summary>
        public void AddToUserWords(string word)
        {
            AddWord(word);
            Directory.CreateDirectory(UserFolder);
            File.AppendAllLines(UserWordsFile, new[] { word });
        }

        public bool IsKnown(string word) => _known.GetOrAdd(word, w =>
        {
            lock (_extra)
                if (_extra.Contains(w) || _extra.Contains(w.ToLowerInvariant()))
                    return true;
            if (_words.Check(w))
                return true;
            // Words with an apostrophe or hyphen ("Joey's", "Hotel-Lobby") count if every part is known
            string[] parts = w.Split(new[] { '\'', '’', '-' }, StringSplitOptions.RemoveEmptyEntries);
            return parts.Length > 1 && parts.All(p => p.Length < 2 || _words.Check(p) || _extra.Contains(p));
        });

        public IReadOnlyList<string> Suggest(string word, int max = 5) =>
            _suggestions.GetOrAdd(word, w => Rank(w, _words.Suggest(w))).Take(max).ToList();

        /// <summary>
        /// Orders Hunspell's suggestions by how close they are to the misspelled word (edit distance,
        /// ignoring case, ß counting as ss), keeping Hunspell's order for equal distances. Hunspell's own
        /// order can put the obvious fix behind odd compounds ("Vieleicht": Vielteich, ..., Vielleicht).
        /// A missing or extra letter of a double counts half, so "Vielleicht" also beats "Vieleiche".
        /// </summary>
        public static List<string> Rank(string word, IEnumerable<string> suggestions)
        {
            string w = Normalize(word);
            // Capitals don't count: in German the fix is often just that ("mitte" -> "Mitte")
            return suggestions.Select((s, i) => (s, i, d: Distance(w, Normalize(s))))
                .OrderBy(x => x.d).ThenBy(x => x.i).Select(x => x.s).ToList();
        }

        private static string Normalize(string s) => s.ToLowerInvariant().Replace("ß", "ss");

        // Optimal string alignment distance (insert, delete, replace, swap of neighbours). Adding or
        // dropping one letter of a double ("Vieleicht", "verschlosen") is the most common slip and costs half.
        private static double Distance(string a, string b)
        {
            static bool Doubled(string s, int k) => (k > 0 && s[k - 1] == s[k]) || (k + 1 < s.Length && s[k + 1] == s[k]);
            var d = new double[a.Length + 1, b.Length + 1];
            for (int i = 1; i <= a.Length; i++) d[i, 0] = d[i - 1, 0] + 1;
            for (int j = 1; j <= b.Length; j++) d[0, j] = d[0, j - 1] + 1;
            for (int i = 1; i <= a.Length; i++)
                for (int j = 1; j <= b.Length; j++)
                {
                    double delete = d[i - 1, j] + (Doubled(a, i - 1) ? 0.5 : 1);
                    double insert = d[i, j - 1] + (Doubled(b, j - 1) ? 0.5 : 1);
                    double replace = d[i - 1, j - 1] + (a[i - 1] == b[j - 1] ? 0 : 1);
                    d[i, j] = Math.Min(Math.Min(delete, insert), replace);
                    if (i > 1 && j > 1 && a[i - 1] == b[j - 2] && a[i - 2] == b[j - 1])
                        d[i, j] = Math.Min(d[i, j], d[i - 2, j - 2] + 1);
                }
            return d[a.Length, b.Length];
        }

        /// <summary>
        /// The unknown words of a translation. Markup is skipped; words that also occur in <paramref name="original"/>
        /// (names, places, made-up words) and words written in capitals (abbreviations) count as known.
        /// </summary>
        public List<SpellingError> Check(string text, string? original = null)
        {
            var errors = new List<SpellingError>();
            HashSet<string>? fromOriginal = null;
            string masked = MarkupPattern.Replace(text, m => new string(' ', m.Length));
            foreach (Match m in WordPattern.Matches(masked))
            {
                string word = m.Value.Trim('\'', '’', '-');
                if (word.Length < 2 || word.All(c => !char.IsLetter(c) || char.IsUpper(c)))
                    continue;
                if (IsKnown(word))
                    continue;
                fromOriginal ??= original != null
                    ? new HashSet<string>(WordPattern.Matches(original).Select(o => o.Value), StringComparer.OrdinalIgnoreCase)
                    : new HashSet<string>();
                if (fromOriginal.Contains(word))
                    continue;
                int start = m.Index + m.Value.IndexOf(word, StringComparison.Ordinal);
                errors.Add(new SpellingError(start, word.Length, word));
            }
            return errors;
        }

        private static IEnumerable<string> SafeDirectories(string root)
        {
            try
            {
                return Directory.EnumerateDirectories(root, "*", SearchOption.AllDirectories).ToList();
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                return Array.Empty<string>();
            }
        }
    }
}
