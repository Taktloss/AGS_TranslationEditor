using System;
using System.IO;
using AGS_TranslationEditor.Models;
using AGSTools;

namespace AGS_TranslationEditor.Services
{
    /// <summary>
    /// The Hunspell dictionary the settings ask for, loaded on first use, with the own words (words.txt)
    /// of the translation's folder. Thread-safe: the check runs in the background.
    /// </summary>
    public sealed class SpellingService
    {
        private readonly Func<AppSettings> _settings;
        private readonly object _lock = new();
        private SpellChecker? _spelling;
        private string? _key;
        private string? _wordsFolder;
        private string? _wantedCache;
        private string? _wantedCacheFor;

        public SpellingService(Func<AppSettings> settings) => _settings = settings;

        /// <summary>Path of the dictionary the settings ask for, or null for none.</summary>
        public string? WantedDictionary()
        {
            AppSettings s = _settings();
            return s.SpellDictionary switch
            {
                "off" => null,
                "" => SpellChecker.FindDictionary(s.MtTargetLanguage),
                string name => SpellChecker.FindDictionaries().TryGetValue(name, out string? path) ? path : null,
            };
        }

        /// <summary>Like <see cref="WantedDictionary"/>, remembered per settings value (finding dictionaries reads folders).</summary>
        public string? WantedDictionaryCached()
        {
            AppSettings s = _settings();
            string id = s.SpellDictionary + "|" + s.MtTargetLanguage;
            lock (_lock)
            {
                if (_wantedCacheFor != id)
                {
                    _wantedCache = WantedDictionary();
                    _wantedCacheFor = id;
                }
                return _wantedCache;
            }
        }

        /// <summary>The dictionary if it is already loaded for the current settings, without loading it.</summary>
        public SpellChecker? IfLoaded()
        {
            string? wanted = WantedDictionaryCached();
            lock (_lock)
                return _key != null && _key == wanted ? _spelling : null;
        }

        /// <summary>
        /// The dictionary, loaded on first use (any thread), with the word list in <paramref name="folder"/>;
        /// null if switched off or none is installed.
        /// </summary>
        public SpellChecker? Get(string? folder)
        {
            string? path = WantedDictionary();
            lock (_lock)
            {
                if (path == null)
                    return null;
                if (_spelling == null || _key != path)
                {
                    try
                    {
                        _spelling = SpellChecker.Load(path);
                    }
                    catch (IOException)
                    {
                        _spelling = null;
                        return null;
                    }
                    _key = path;
                    _wordsFolder = null;
                }
                if (folder != null && folder != _wordsFolder)
                {
                    _spelling.AddWordsFrom(Path.Combine(folder, "words.txt"));
                    _wordsFolder = folder;
                }
                return _spelling;
            }
        }

        /// <summary>The settings changed: load again when next needed.</summary>
        public void Reset()
        {
            lock (_lock)
            {
                _key = null;
                _wantedCacheFor = null;
            }
        }
    }
}
