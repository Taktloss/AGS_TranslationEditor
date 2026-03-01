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

        private string _currentFilename = string.Empty;
        private bool _documentChanged = false;

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
                _documentChanged = true;
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

        public void LoadFile(string filename)
        {
            try
            {
                Dictionary<string, string>? items = null;
                string ext = Path.GetExtension(filename).ToLowerInvariant();

                switch (ext)
                {
                    case ".tra":
                        items = Translation.ParseTRA_Translation(filename);
                        break;
                    case ".trs":
                        items = Translation.ParseTRS_Translation(filename);
                        break;
                    case ".csv":
                    case ".tsv":
                        items = CSVFormat.OpenCSV(filename);
                        break;
                    case ".po":
                        items = POFormat.OpenPO(filename);
                        break;
                }

                if (items != null)
                {
                    Entries.Clear();
                    foreach (var kvp in items)
                        Entries.Add(new TranslationEntry { Key = kvp.Key, Value = kvp.Value });

                    _currentFilename = filename;
                    _documentChanged = false;
                    WindowTitle = $"{Path.GetFileName(filename)} - AGS Translation Editor";
                    FileStatusText = "File loaded";
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
        }

        public void SaveFile(string filename)
        {
            try
            {
                // Use Latin-1 so TRS files are compatible with the AGS engine
                using var fw = new StreamWriter(filename, false, Encoding.Latin1);
                foreach (var entry in Entries)
                {
                    fw.WriteLine(entry.Key);
                    fw.WriteLine(entry.Value);
                }
                _currentFilename = filename;
                _documentChanged = false;
                WindowTitle = WindowTitle.TrimStart('*');
                FileStatusText = "Saved";
            }
            catch (Exception ex)
            {
                FileStatusText = $"Error saving: {ex.Message}";
            }
        }

        public string CurrentFilename => _currentFilename;
        public bool DocumentChanged => _documentChanged;
        public void ResetDocumentChanged() => _documentChanged = false;

        private void UpdateStatus()
        {
            int total = Entries.Count;
            int translated = Entries.Count(e => !string.IsNullOrEmpty(e.Value));

            EntriesCountText = $"Entries: {translated}/{total}";
            ProgressValue = total > 0 ? (translated * 100) / total : 0;
        }
    }
}

