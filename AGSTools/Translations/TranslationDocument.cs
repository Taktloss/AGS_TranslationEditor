using System.Collections.Generic;

namespace AGSTools
{
    /// <summary>
    /// A translation: its settings plus source/translation pairs in file order.
    /// Texts are kept in TRS form, i.e. with AGS escape sequences such as \" or \n unresolved.
    /// </summary>
    public sealed class TranslationDocument
    {
        public TranslationOptions Options { get; set; } = new();

        /// <summary>Source text → translation (empty if untranslated). Insertion order is file order.</summary>
        public Dictionary<string, string> Entries { get; set; } = new();

        /// <summary>Notes about how the file was interpreted while reading it, e.g. an old TRS style.</summary>
        public List<string> ReadWarnings { get; } = new();
    }
}
