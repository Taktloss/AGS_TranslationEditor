using AGSFontEditor.Localization;
using AGSTools;
using CommunityToolkit.Mvvm.ComponentModel;

namespace AGS_TranslationEditor.Models
{
    /// <summary>A finding of the translation check or of an AI review, in the check panel.</summary>
    public sealed partial class CheckItem : ObservableObject
    {
        /// <param name="fix">Makes the corrected text from the entry's current text (null if that is no longer possible);
        /// without it the entry gets <paramref name="suggestion"/>.</param>
        public CheckItem(CheckIssue issue, TranslationEntry? entry, string? suggestion = null, System.Func<string, string?>? fix = null)
        {
            Issue = issue;
            Entry = entry;
            Suggestion = suggestion;
            Fix = fix ?? (_ => suggestion);
        }

        /// <summary>The corrected text for the entry's current text, or null.</summary>
        public System.Func<string, string?> Fix { get; }

        public CheckIssue Issue { get; }

        /// <summary>The entry the finding is about, or null if it is no longer in the list.</summary>
        public TranslationEntry? Entry { get; }

        /// <summary>A corrected translation proposed by the AI, or null.</summary>
        public string? Suggestion { get; }

        public bool HasSuggestion => Suggestion != null && Entry != null;

        /// <summary>True once the suggestion was put into the entry.</summary>
        [ObservableProperty]
        [NotifyPropertyChangedFor(nameof(CanApply))]
        private bool _isApplied;

        public bool CanApply => HasSuggestion && !IsApplied;

        public string Header => $"{Loc.T(Issue.Severity.ToString())}: {Loc.T(Issue.Category)}";

        public string Text => Issue.Key;

        public string Message => Issue.Message;

        public Avalonia.Media.IBrush Color => AGSFontEditor.Theming.AppTheme.Brush(Issue.Severity switch
        {
            CheckSeverity.Error => "Error",
            CheckSeverity.Warning => "Warning",
            _ => "Info",
        });
    }
}
