using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.IO;
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
                using (StreamWriter fw = new StreamWriter(filename, false))
                {
                    foreach (var entry in Entries)
                    {
                        fw.WriteLine(entry.Key);
                        fw.WriteLine(entry.Value);
                    }
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
            int translated = 0;
            foreach (var e in Entries)
                if (!string.IsNullOrEmpty(e.Value))
                    translated++;

            EntriesCountText = $"Entries: {translated}/{total}";
            ProgressValue = total > 0 ? (translated * 100) / total : 0;
        }
    }
}

