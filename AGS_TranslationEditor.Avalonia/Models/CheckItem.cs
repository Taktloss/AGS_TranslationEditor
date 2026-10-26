using AGSFontEditor.Localization;
using AGSTools;

namespace AGS_TranslationEditor.Models
{
    /// <summary>A finding of the translation check, in the check panel.</summary>
    public sealed class CheckItem
    {
        public CheckItem(CheckIssue issue, TranslationEntry? entry)
        {
            Issue = issue;
            Entry = entry;
        }

        public CheckIssue Issue { get; }

        /// <summary>The entry the finding is about, or null if it is no longer in the list.</summary>
        public TranslationEntry? Entry { get; }

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
