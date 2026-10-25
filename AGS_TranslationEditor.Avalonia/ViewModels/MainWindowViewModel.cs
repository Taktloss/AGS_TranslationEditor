using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using AGS_TranslationEditor.Models;
using AGSTools;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace AGS_TranslationEditor.ViewModels
{
    public partial class MainWindowViewModel : ViewModelBase
    {
        [ObservableProperty]
        private ObservableCollection<TranslationEntry> _entries = new();

        [ObservableProperty]
        private ObservableCollection<TranslationEntry> _displayedEntries = new();

        [ObservableProperty]
        private TranslationEntry? _selectedEntry;

        [ObservableProperty]
        private string _sourceText = string.Empty;

        [ObservableProperty]
        private string _translationText = string.Empty;

        [ObservableProperty]
        private string _fileStatusText = "Ready";

        [ObservableProperty]
        private string _entriesCountText = "0 entries";

        [ObservableProperty]
        private int _progressValue = 0;

        [ObservableProperty]
        private string _windowTitle = "AGS Translation Editor";

        [ObservableProperty]
        private bool _filterUntranslated = false;

        [ObservableProperty]
        private bool _documentChanged = false;

        [ObservableProperty]
        private double _editorFontSize = 13;

        [ObservableProperty]
        private string _saveEncoding = "Latin-1";

        [ObservableProperty]
        private bool _isLoading = false;

        private string _currentFilename = string.Empty;

        /// <summary>Settings of the loaded translation (game id, fonts, encoding, ...), kept when saving.</summary>
        public TranslationOptions Options { get; private set; } = new();

        partial void OnSelectedEntryChanged(TranslationEntry? value)
        {
            if (value != null)
            {
                SourceText = value.Key;
                TranslationText = value.Value;
            }
        }

        partial void OnTranslationTextChanged(string value)
        {
            if (SelectedEntry != null && SelectedEntry.Value != value)
            {
                SelectedEntry.Value = value;
                DocumentChanged = true;
                UpdateStatus();
                if (!WindowTitle.StartsWith("*"))
                    WindowTitle = "*" + WindowTitle;
            }
        }

        partial void OnFilterUntranslatedChanged(bool value)
        {
            RefreshDisplayedEntries();
        }

        partial void OnEntriesChanged(ObservableCollection<TranslationEntry> value)
        {
            RefreshDisplayedEntries();
        }

        private void RefreshDisplayedEntries()
        {
            var source = FilterUntranslated
                ? Entries.Where(e => string.IsNullOrEmpty(e.Value))
                : Entries.AsEnumerable();

            DisplayedEntries = new ObservableCollection<TranslationEntry>(source);
        }

        /// <summary>Jump to the next entry with no translation.</summary>
        public TranslationEntry? GetNextUntranslated()
        {
            var list = FilterUntranslated ? DisplayedEntries : Entries;
            int startIdx = SelectedEntry != null ? list.IndexOf(SelectedEntry) : -1;
            for (int i = startIdx + 1; i < list.Count; i++)
                if (string.IsNullOrEmpty(list[i].Value))
                    return list[i];
            // Wrap around
            for (int i = 0; i <= startIdx; i++)
                if (string.IsNullOrEmpty(list[i].Value))
                    return list[i];
            return null;
        }

        [RelayCommand]
        public async Task OpenFile()
        {
            // File opening is handled by the View via dialog interaction
        }

        public async Task LoadFile(string filename)
        {
            IsLoading = true;
            try
            {
                string ext = Path.GetExtension(filename).ToLowerInvariant();

                TranslationDocument? doc = await Task.Run(() =>
                {
                    return ext switch
                    {
                        ".tra" => TraFile.Read(filename),
                        ".trs" => TrsFile.Read(filename),
                        ".csv" or ".tsv" => new TranslationDocument { Entries = CSVFormat.OpenCSV(filename) },
                        ".po" => new TranslationDocument { Entries = POFormat.OpenPO(filename) },
                        _ => null
                    };
                });

                if (doc != null)
                {
                    Options = doc.Options;
                    Entries.Clear();
                    int idx = 1;
                    foreach (var kvp in doc.Entries)
                        Entries.Add(new TranslationEntry { Key = kvp.Key, Value = kvp.Value, RowIndex = idx++ });

                    _currentFilename = filename;
                    DocumentChanged = false;
                    WindowTitle = $"{Path.GetFileName(filename)} - AGS Translation Editor";
                    FileStatusText = $"Loaded {Entries.Count} entries" + (Options.IsUtf8 ? " (UTF-8)" : "");
                    RefreshDisplayedEntries();
                    UpdateStatus();
                }
                else
                {
                    FileStatusText = "No entries found";
                }
            }
            catch (Exception ex)
            {
                FileStatusText = $"Error: {ex.Message}";
            }
            finally
            {
                IsLoading = false;
            }
        }

        /// <summary>
        /// Saves as TRS with the AGS header and the translation's settings. The "UTF-8" save setting
        /// switches 8-bit translations to UTF-8; a UTF-8 translation is never downgraded, as that could lose characters.
        /// </summary>
        public void SaveFile(string filename)
        {
            try
            {
                if (SaveEncoding == "UTF-8" && !Options.IsUtf8)
                    Options.Encoding = "UTF-8";
                var warnings = TrsFile.Write(filename, ToDocument(e => e.Value));
                _currentFilename = filename;
                DocumentChanged = false;
                WindowTitle = WindowTitle.TrimStart('*');
                FileStatusText = warnings.Count > 0 ? $"Saved. ⚠ {warnings[0]}" : "Saved";
            }
            catch (Exception ex)
            {
                FileStatusText = $"Error saving: {ex.Message}";
            }
        }

        /// <summary>
        /// If a source string starts with &lt;&amp;N &gt; (AGS voice cue), ensure the
        /// translation also starts with it — otherwise the game plays no voice.
        /// </summary>
        public static string EnsureVoicePrefix(string source, string translation)
        {
            if (string.IsNullOrEmpty(translation)) return translation;
            var m = System.Text.RegularExpressions.Regex.Match(
                source, @"^(&\d+ )", System.Text.RegularExpressions.RegexOptions.None);
            if (!m.Success) return translation;
            string prefix = m.Groups[1].Value;
            // Only add if translation doesn't already have a voice prefix
            if (!System.Text.RegularExpressions.Regex.IsMatch(translation, @"^&\d+ "))
                return prefix + translation;
            return translation;
        }

        /// <summary>Build the translation for TRA export, with voice prefixes ensured.</summary>
        public TranslationDocument GetDocumentForExport() => ToDocument(e => EnsureVoicePrefix(e.Key, e.Value));

        private TranslationDocument ToDocument(Func<TranslationEntry, string> translation)
        {
            var doc = new TranslationDocument { Options = Options.Clone() };
            foreach (var entry in Entries)
                doc.Entries.TryAdd(entry.Key, translation(entry));
            return doc;
        }

        public string CurrentFilename => _currentFilename;
        public void ResetDocumentChanged() => DocumentChanged = false;

        private void UpdateStatus()
        {
            int total = Entries.Count;
            int translated = Entries.Count(e => !string.IsNullOrEmpty(e.Value));

            EntriesCountText = $"Entries: {translated}/{total}";
            ProgressValue = total > 0 ? (translated * 100) / total : 0;
        }
    }
}

