using AGSFontEditor.Localization;
using AGSTools;
using CommunityToolkit.Mvvm.ComponentModel;

namespace AGS_TranslationEditor.Models
{
    /// <summary>A finding of the translation check or of an AI review, in the check panel.</summary>
    public sealed partial class CheckItem : ObservableObject
    {
        public CheckItem(CheckIssue issue, TranslationEntry? entry, string? suggestion = null)
        {
            Issue = issue;
            Entry = entry;
            Suggestion = suggestion;
        }

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

        public string Color => Issue.Severity switch
        {
            CheckSeverity.Error => "#F48771",
            CheckSeverity.Warning => "#CCA700",
            _ => "#75BEFF",
        };
    }
}
