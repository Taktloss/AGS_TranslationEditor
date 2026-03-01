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

                Dictionary<string, string>? items = await Task.Run(() =>
                {
                    return ext switch
                    {
                        ".tra" => Translation.ParseTRA_Translation(filename),
                        ".trs" => Translation.ParseTRS_Translation(filename),
                        ".csv" or ".tsv" => CSVFormat.OpenCSV(filename),
                        ".po" => POFormat.OpenPO(filename),
                        _ => null
                    };
                });

                if (items != null)
                {
                    Entries.Clear();
                    int idx = 1;
                    foreach (var kvp in items)
                        Entries.Add(new TranslationEntry { Key = kvp.Key, Value = kvp.Value, RowIndex = idx++ });

                    _currentFilename = filename;
                    DocumentChanged = false;
                    WindowTitle = $"{Path.GetFileName(filename)} - AGS Translation Editor";
                    FileStatusText = $"Loaded {Entries.Count} entries";
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

        public void SaveFile(string filename)
        {
            try
            {
                var enc = SaveEncoding == "UTF-8" ? Encoding.UTF8 : Encoding.Latin1;
                using var fw = new StreamWriter(filename, false, enc);
                foreach (var entry in Entries)
                {
                    fw.WriteLine(entry.Key);
                    fw.WriteLine(entry.Value);
                }
                _currentFilename = filename;
                DocumentChanged = false;
                WindowTitle = WindowTitle.TrimStart('*');
                FileStatusText = "Saved";
            }
            catch (Exception ex)
            {
                FileStatusText = $"Error saving: {ex.Message}";
            }
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

