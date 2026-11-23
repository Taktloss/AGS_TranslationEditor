using System;
using System.Collections.Generic;
using AGSFontEditor.Localization;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace AGS_TranslationEditor.ViewModels
{
    // Search in source and translation, and jumping to untranslated entries
    public partial class MainWindowViewModel
    {
        [ObservableProperty]
        private string _searchText = string.Empty;

        private List<int> _foundIndices = new();
        private int _currentFindIndex = -1;
        private string _lastSearch = "";

        /// <summary>Indexes of the entries whose source or translation contains <paramref name="text"/> (ignoring case).</summary>
        public static List<int> FindMatches(IReadOnlyList<Models.TranslationEntry> entries, string text)
        {
            var found = new List<int>();
            if (string.IsNullOrEmpty(text))
                return found;
            for (int i = 0; i < entries.Count; i++)
            {
                if (entries[i].Key.Contains(text, StringComparison.OrdinalIgnoreCase) ||
                    entries[i].Value.Contains(text, StringComparison.OrdinalIgnoreCase))
                    found.Add(i);
            }
            return found;
        }

        [RelayCommand(CanExecute = nameof(HasEntries))]
        private void Find()
        {
            if (string.IsNullOrEmpty(SearchText))
                return;
            _foundIndices = FindMatches(Entries, SearchText);
            // Searching for the same text again (Enter again) goes on after the selected entry
            bool again = SearchText == _lastSearch;
            _lastSearch = SearchText;
            int selected = again && SelectedEntry != null ? Entries.IndexOf(SelectedEntry) : -1;
            _currentFindIndex = selected < 0 ? -1 : _foundIndices.FindLastIndex(i => i <= selected);
            FileStatusText = Loc.F("Found {0} matches", _foundIndices.Count);
            if (_foundIndices.Count > 0)
                FindNext();
        }

        [RelayCommand(CanExecute = nameof(HasEntries))]
        private void FindNext()
        {
            if (_foundIndices.Count == 0)
                return;
            _currentFindIndex = (_currentFindIndex + 1) % _foundIndices.Count;
            ShowFound();
        }

        [RelayCommand(CanExecute = nameof(HasEntries))]
        private void FindPrevious()
        {
            if (_foundIndices.Count == 0)
                return;
            _currentFindIndex = (_currentFindIndex - 1 + _foundIndices.Count) % _foundIndices.Count;
            ShowFound();
        }

        private void ShowFound()
        {
            int index = _foundIndices[_currentFindIndex];
            if (index < Entries.Count)
                Reveal(Entries[index]);
        }

        [RelayCommand(CanExecute = nameof(HasEntries))]
        private void JumpToNextUntranslated()
        {
            var next = GetNextUntranslated();
            if (next != null)
            {
                Reveal(next);
                FileStatusText = Loc.T("Jumped to next untranslated entry.");
            }
            else
            {
                FileStatusText = Loc.T("No untranslated entries remaining.");
            }
        }
    }
}
