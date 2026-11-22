using AGSFontEditor.Localization;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using AGS_TranslationEditor.Models;
using AGS_TranslationEditor.Services;
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
        private string _fileStatusText = Loc.T("Ready");

        [ObservableProperty]
        private string _entriesCountText = Loc.T("0 entries");

        [ObservableProperty]
        private int _progressValue = 0;

        [ObservableProperty]
        private string _windowTitle = Loc.T("AGS Translation Editor");

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

        /// <summary>Setting: read, show and keep the optional context file (speaker and origin of each text).</summary>
        [ObservableProperty]
        [NotifyPropertyChangedFor(nameof(IsContextVisible))]
        private bool _showContext;

        /// <summary>Origins from the context file of the current translation, or null if it has none.</summary>
        private Dictionary<string, TextOrigin>? _context;

        public bool IsContextVisible => ShowContext && _context != null;

        partial void OnShowContextChanged(bool value)
        {
            // Switching it on later reads the context of the file that is already open
            if (value && _context == null && _currentFilename.Length > 0)
                SetContext(TextContext.Read(TextContext.PathFor(_currentFilename)));
        }

        /// <summary>Uses <paramref name="context"/> for the entries (null removes it).</summary>
        public void SetContext(Dictionary<string, TextOrigin>? context)
        {
            _context = context;
            foreach (var entry in Entries)
            {
                entry.OriginInfo = context != null && context.TryGetValue(entry.Key, out var origin) ? origin : null;
                entry.Origin = entry.OriginInfo?.ToString() ?? "";
            }
            OnPropertyChanged(nameof(IsContextVisible));
            if (ContextFilter.Length > 0)
                RefreshDisplayedEntries();
        }

        /// <summary>True if the origins of the texts are known (context file read or created).</summary>
        public bool HasContext => _context != null;

        /// <summary>Reads the context file of the open translation, also when the context column is off.</summary>
        public void EnsureContextLoaded()
        {
            if (_context == null && _currentFilename.Length > 0)
                SetContext(TextContext.Read(TextContext.PathFor(_currentFilename)));
        }

        /// <summary>Creates the context file next to the open translation from the game, whatever the setting.</summary>
        public void CreateContext(ExtractionResult extraction)
        {
            var context = TextContext.FromExtraction(extraction);
            TextContext.Write(TextContext.PathFor(_currentFilename), context);
            SetContext(context);
            Progress.OnDocumentLoaded();
        }

        /// <summary>Sets the context from a fresh extraction and writes it next to the translation.</summary>
        public void UpdateContext(ExtractionResult extraction, string translationPath)
        {
            if (!ShowContext)
                return;
            var context = TextContext.FromExtraction(extraction);
            TextContext.Write(TextContext.PathFor(translationPath), context);
            SetContext(context);
        }

        /// <summary>Findings of the last translation check.</summary>
        public ObservableCollection<CheckItem> CheckItems { get; } = new();

        [ObservableProperty]
        private bool _showCheckPanel;

        [ObservableProperty]
        private string _checkSummary = string.Empty;

        [ObservableProperty]
        private CheckItem? _selectedCheckItem;

        public void ShowCheckResults(IEnumerable<CheckIssue> issues, string summary)
        {
            CheckItems.Clear();
            foreach (CheckIssue issue in issues.OrderBy(i => i.Severity).ThenBy(i => i.Category))
                CheckItems.Add(new CheckItem(issue, FindEntry(issue.Key)));
            CheckSummary = summary;
            ShowCheckPanel = true;
        }

        /// <summary>Starts an empty panel for AI suggestions; <see cref="AddSuggestions"/> fills it.</summary>
        public void StartSuggestions(string summary)
        {
            CheckItems.Clear();
            CheckSummary = summary;
            ShowCheckPanel = true;
            OnPropertyChanged(nameof(HasOpenSuggestions));
        }

        /// <summary>Adds AI suggestions: (category, entry key, reason, suggested translation).</summary>
        public void AddSuggestions(IEnumerable<(string Category, string Key, string Reason, string? Suggestion)> suggestions)
        {
            foreach (var (category, key, reason, suggestion) in suggestions)
                CheckItems.Add(new CheckItem(new CheckIssue(suggestion != null ? CheckSeverity.Info : CheckSeverity.Warning, category, key, reason),
                    FindEntry(key), suggestion));
            OnPropertyChanged(nameof(HasOpenSuggestions));
        }

        public bool HasOpenSuggestions => CheckItems.Any(c => c.CanApply);

        /// <summary>Adds corrections that are applied to the entry's current text (several per entry are possible).</summary>
        public void AddCorrections(IEnumerable<(string Category, string Key, string Reason, string Preview, System.Func<string, string?>? Fix)> corrections)
        {
            foreach (var (category, key, reason, preview, fix) in corrections)
                CheckItems.Add(new CheckItem(new CheckIssue(CheckSeverity.Info, category, key, reason), FindEntry(key), fix != null ? preview : null, fix));
            OnPropertyChanged(nameof(HasOpenSuggestions));
        }

        /// <summary>Puts the AI's suggestion into its entry.</summary>
        [RelayCommand]
        private void ApplySuggestion(CheckItem? item)
        {
            if (item is not { CanApply: true, Entry: { } entry })
                return;
            if (item.Fix(entry.Value) is not { } corrected)
            {
                FileStatusText = Loc.T("The entry was changed in the meantime; this suggestion no longer applies.");
                return;
            }
            entry.Value = corrected;
            item.IsApplied = true;
            DocumentChanged = true;
            OnPropertyChanged(nameof(HasOpenSuggestions));
        }

        [RelayCommand]
        private void ApplyAllSuggestions()
        {
            int count = 0;
            BeginEditBatch(Loc.T("AI suggestions applied"));
            try
            {
                foreach (CheckItem item in CheckItems.Where(c => c.CanApply).ToList())
                {
                    ApplySuggestion(item);
                    count++;
                }
            }
            finally
            {
                EndEditBatch();
            }
            FileStatusText = Loc.F("{0} suggestions applied.", count);
        }

        [RelayCommand]
        private void CloseCheckPanel() => ShowCheckPanel = false;

        private System.Threading.CancellationTokenSource? _aiCancellation;

        /// <summary>True while an AI service works; the status bar then shows a Cancel button.</summary>
        [ObservableProperty]
        private bool _isAiBusy;

        /// <summary>Starts AI work that <see cref="CancelAiCommand"/> can stop.</summary>
        public System.Threading.CancellationToken StartAiWork()
        {
            _aiCancellation?.Dispose();
            _aiCancellation = new System.Threading.CancellationTokenSource();
            IsAiBusy = true;
            return _aiCancellation.Token;
        }

        public void EndAiWork() => IsAiBusy = false;

        [RelayCommand]
        private void CancelAi()
        {
            if (_aiCancellation is { IsCancellationRequested: false } cancellation)
            {
                cancellation.Cancel();
                FileStatusText = Loc.T("Cancelling - waiting for the running request...");
            }
        }

        private Dictionary<string, TranslationEntry> _entriesByKey = new();

        public MainWindowViewModel()
        {
            Live = new LiveViewModel(this);
            Progress = new ProgressViewModel(this);
            Spelling = new SpellingService(() => Settings);
            Files = new FileCommands(this);
            Game = new GameCommands(this);
            Ai = new AiCommands(this);
        }

        /// <summary>The window's dialogs; set by the window (tests set their own).</summary>
        public IDialogService Dialogs { get; set; } = NoDialogs.Instance;

        /// <summary>The user's settings; set by the window, edited in place by the settings dialog.</summary>
        public AppSettings Settings { get; set; } = new();

        public SpellingService Spelling { get; }

        /// <summary>Open, save and export.</summary>
        public FileCommands Files { get; }

        /// <summary>Everything that reads or changes a game: extract, check, TRA, install, package.</summary>
        public GameCommands Game { get; }

        /// <summary>Machine translation, AI review and shortening, LanguageTool.</summary>
        public AiCommands Ai { get; }

        /// <summary>The game file chosen last in this session; the font and image editors open it.</summary>
        public string? LastGameFile { get; set; }

        /// <summary>Selects an entry and scrolls the table to it.</summary>
        public void Reveal(TranslationEntry entry)
        {
            SelectedEntry = entry;
            RevealEntry?.Invoke(entry);
        }

        /// <summary>Folder of the open translation (glossary, own words), or null for a new one.</summary>
        public string? TranslationFolder() =>
            CurrentFilename.Length > 0 ? Path.GetDirectoryName(Path.GetFullPath(CurrentFilename)) : null;

        /// <summary>"glossary.txt" or "Glossar.txt" next to the open translation, or null.</summary>
        public string? FindGlossary() =>
            TranslationFolder() is { } folder
                ? new[] { "glossary.txt", "Glossar.txt" }.Select(n => Path.Combine(folder, n)).FirstOrDefault(File.Exists)
                : null;

        /// <summary>Language name the game will use: the open file's name, e.g. "Deutsch" for Deutsch.trs.</summary>
        public string LanguageName()
        {
            string name = Path.GetFileNameWithoutExtension(CurrentFilename);
            name = new string(name.Where(c => char.IsLetterOrDigit(c) || c is '-' or '_').ToArray());
            return name.Length > 0 ? name : "Translation";
        }

        /// <summary>Live mode: texts of the running game, edits sent to it at once.</summary>
        public LiveViewModel Live { get; }

        public ProgressViewModel Progress { get; }

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

        /// <summary>Shows only entries whose origin contains this text, e.g. a character or room name.</summary>
        [ObservableProperty]
        private string _contextFilter = string.Empty;

        partial void OnContextFilterChanged(string value) => RefreshDisplayedEntries();

        /// <summary>Group chosen in the progress panel: only its texts are shown.</summary>
        private (ProgressGrouping Grouping, string Name)? _groupFilter;

        public void SetGroupFilter((ProgressGrouping Grouping, string Name)? filter)
        {
            if (_groupFilter == filter)
                return;
            _groupFilter = filter;
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
            string filter = ContextFilter.Trim();
            if (IsContextVisible && filter.Length > 0)
                source = source.Where(e => e.Origin.Contains(filter, StringComparison.OrdinalIgnoreCase));
            if (_groupFilter is var (grouping, name))
                source = source.Where(e => TranslationProgress.GroupOf(e.OriginInfo, grouping) == name);

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
                    WindowTitle = Loc.F("{0} - AGS Translation Editor", Path.GetFileName(filename));
                    FileStatusText = Loc.F("Loaded {0} entries", Entries.Count) + (Options.IsUtf8 ? Loc.T(" (UTF-8)") : "")
                        + (doc.ReadWarnings.Count > 0 ? $". ⚠ {doc.ReadWarnings[0]}" : "");
                    SetContext(ShowContext ? await Task.Run(() => TextContext.Read(TextContext.PathFor(filename))) : null);
                    RebuildIndex();
                    RefreshDisplayedEntries();
                    UpdateStatus();
                    Live.OnDocumentLoaded();
                    Progress.OnDocumentLoaded();
                }
                else
                {
                    FileStatusText = Loc.T("No entries found");
                }
            }
            catch (Exception ex)
            {
                FileStatusText = Loc.F("Error: {0}", ex.Message);
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
                // Save As takes the context along
                if (ShowContext && _context != null && !string.Equals(filename, _currentFilename, StringComparison.Ordinal))
                    TextContext.Write(TextContext.PathFor(filename), _context);
                _currentFilename = filename;
                DocumentChanged = false;
                WindowTitle = WindowTitle.TrimStart('*');
                FileStatusText = warnings.Count > 0 ? Loc.F("Saved. ⚠ {0}", warnings[0]) : Loc.T("Saved");
            }
            catch (Exception ex)
            {
                FileStatusText = Loc.F("Error saving: {0}", ex.Message);
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

        /// <summary>The current translation as edited.</summary>
        public TranslationDocument GetDocument() => ToDocument(e => e.Value);

        /// <summary>Replaces the edited translation, e.g. with a merge result; the file still has to be saved.</summary>
        public void ReplaceDocument(TranslationDocument doc)
        {
            Options = doc.Options;
            Entries.Clear();
            int idx = 1;
            foreach (var kvp in doc.Entries)
                Entries.Add(new TranslationEntry { Key = kvp.Key, Value = kvp.Value, RowIndex = idx++ });
            SetContext(_context);
            SelectedEntry = null;
            DocumentChanged = true;
            if (!WindowTitle.StartsWith("*"))
                WindowTitle = "*" + WindowTitle;
            RebuildIndex();
            RefreshDisplayedEntries();
            UpdateStatus();
            Live.OnDocumentLoaded();
            Progress.OnDocumentLoaded();
        }

        // ── Undo / redo of translations ──

        private readonly EditHistory<TranslationEntry> _history = new();

        [ObservableProperty]
        [NotifyCanExecuteChangedFor(nameof(UndoCommand))]
        private bool _canUndo;

        [ObservableProperty]
        [NotifyCanExecuteChangedFor(nameof(RedoCommand))]
        private bool _canRedo;

        /// <summary>Asks the view to scroll the table to an entry (after undo or redo).</summary>
        public event Action<TranslationEntry>? RevealEntry;

        /// <summary>Changes until <see cref="EndEditBatch"/> are undone together, e.g. auto-translating all empty entries.</summary>
        public void BeginEditBatch(string label) => _history.BeginBatch(label);

        public void EndEditBatch()
        {
            _history.EndBatch();
            UpdateUndoState();
        }

        private void UpdateUndoState()
        {
            CanUndo = _history.CanUndo;
            CanRedo = _history.CanRedo;
        }

        [RelayCommand(CanExecute = nameof(CanUndo))]
        private void Undo()
        {
            string? label = _history.UndoLabel;
            ShowHistoryStep(_history.Undo((entry, value) => entry.Value = value), label, undone: true);
        }

        [RelayCommand(CanExecute = nameof(CanRedo))]
        private void Redo()
        {
            string? label = _history.RedoLabel;
            ShowHistoryStep(_history.Redo((entry, value) => entry.Value = value), label, undone: false);
        }

        private void ShowHistoryStep(IReadOnlyList<TranslationEntry> entries, string? label, bool undone)
        {
            UpdateUndoState();
            if (entries.Count == 0)
                return;
            TranslationEntry entry = entries[0];
            DocumentChanged = true;
            if (!WindowTitle.StartsWith("*"))
                WindowTitle = "*" + WindowTitle;
            UpdateStatus();
            if (SelectedEntry == entry)
                TranslationText = entry.Value; // the editor shows the entry: update it
            else
                SelectedEntry = entry;
            RevealEntry?.Invoke(entry);
            string what = label != null ? Loc.F("{0} ({1} entries)", label, entries.Count) : Loc.F("entry {0}", entry.RowIndex);
            FileStatusText = undone ? Loc.F("Undone: {0}", what) : Loc.F("Redone: {0}", what);
        }

        private void RebuildIndex()
        {
            _history.Clear();
            UpdateUndoState();
            _entriesByKey = new Dictionary<string, TranslationEntry>();
            foreach (var entry in Entries)
            {
                _entriesByKey.TryAdd(entry.Key, entry);
                entry.ValueChanged += (changed, before) =>
                {
                    _history.Record(changed, before, changed.Value, DateTime.UtcNow);
                    UpdateUndoState();
                    // Keep the count in the status bar right for changes outside the editor (AI, undo)
                    int delta = (string.IsNullOrEmpty(changed.Value) ? 0 : 1) - (string.IsNullOrEmpty(before) ? 0 : 1);
                    if (delta != 0)
                        ShowCount(_translatedCount + delta);
                };
                // Edits in the editor panel, the grid or by auto-translate all go to a running game
                entry.PropertyChanged += (s, e) =>
                {
                    if (e.PropertyName != nameof(TranslationEntry.Value))
                        return;
                    Live.OnEntryEdited(entry);
                    Progress.OnEntryEdited();
                };
            }
        }

        /// <summary>The entry with this source text (as written in the TRS), if the translation has one.</summary>
        public TranslationEntry? FindEntry(string key) => _entriesByKey.GetValueOrDefault(key);

        /// <summary>True if some entry is translated to this runtime text.</summary>
        public bool IsTranslation(string runtimeText) =>
            Entries.Any(e => e.Value.Length > 0 && TextEscaping.ToRuntime(e.Value) == runtimeText);

        /// <summary>Saves the file if it is a TRS; other formats need Save As.</summary>
        public bool SaveIfTrs()
        {
            if (!_currentFilename.EndsWith(".trs", StringComparison.OrdinalIgnoreCase))
                return false;
            // Grid edits do not set DocumentChanged, so save without checking it
            SaveFile(_currentFilename);
            return true;
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

        private int _translatedCount;

        private void UpdateStatus() => ShowCount(Entries.Count(e => !string.IsNullOrEmpty(e.Value)));

        private void ShowCount(int translated)
        {
            _translatedCount = translated;
            int total = Entries.Count;
            EntriesCountText = Loc.F("Entries: {0}/{1}", translated, total);
            ProgressValue = total > 0 ? (translated * 100) / total : 0;
        }
    }
}

