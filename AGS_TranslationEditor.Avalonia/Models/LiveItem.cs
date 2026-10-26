using AGSFontEditor.Localization;
using CommunityToolkit.Mvvm.ComponentModel;

namespace AGS_TranslationEditor.Models
{
    /// <summary>A text the running game displayed, in the live panel.</summary>
    public partial class LiveItem : ObservableObject
    {
        public LiveItem(string original, TranslationEntry? entry)
        {
            Original = original;
            Entry = entry;
        }

        /// <summary>Original text as the game uses it (runtime text, the translation key).</summary>
        public string Original { get; }

        /// <summary>The entry of the loaded translation, or null if the file has no such text.</summary>
        public TranslationEntry? Entry { get; }

        [ObservableProperty]
        [NotifyPropertyChangedFor(nameof(Note))]
        private string _shown = string.Empty;

        [ObservableProperty]
        [NotifyPropertyChangedFor(nameof(Note))]
        private bool _isCorrected;

        public string Note => Entry == null ? Loc.T("not in this translation file")
            : IsCorrected ? Loc.T("corrected live")
            : string.IsNullOrEmpty(Entry.Value) ? Loc.T("untranslated")
            : string.Empty;
    }
}
