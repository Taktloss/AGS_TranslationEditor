using AGSFontEditor.Localization;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using AGS_TranslationEditor.Models;
using AGSFontEditor.Fonts;
using AGSFontEditor.Rendering;
using AGSTools;
using Avalonia.Media.Imaging;
using Avalonia.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace AGS_TranslationEditor.ViewModels
{
    /// <summary>
    /// Live mode: lists the texts the running game displays (through the AGSLive hook) and sends every
    /// edit to the game at once, so translations can be corrected while playing.
    /// </summary>
    public partial class LiveViewModel : ViewModelBase, IDisposable
    {
        private const int MaxItems = 300;

        private readonly MainWindowViewModel _main;
        private readonly DispatcherTimer _autoSaveTimer;
        private LiveServer? _server;

        [ObservableProperty]
        private bool _isActive;

        [ObservableProperty]
        private string _statusText = string.Empty;

        [ObservableProperty]
        private bool _autoSave = true;

        [ObservableProperty]
        private LiveItem? _selectedItem;

        [ObservableProperty]
        [NotifyCanExecuteChangedFor(nameof(AttachCommand))]
        private RunningGame? _selectedGame;

        [ObservableProperty]
        [NotifyCanExecuteChangedFor(nameof(AttachCommand))]
        [NotifyCanExecuteChangedFor(nameof(RefreshGamesCommand))]
        private bool _isAttaching;

        /// <summary>AGS games that are running now (for attaching the hook to one of them).</summary>
        public ObservableCollection<RunningGame> RunningGames { get; } = new();

        /// <summary>Displayed texts, newest first.</summary>
        public ObservableCollection<LiveItem> Items { get; } = new();

        // ── Preview: the selected (or newest) text drawn with a game font, as the corrected line will look ──

        private readonly Dictionary<GameFont, EditableFont?> _openFonts = new();
        private string? _previewKey;

        /// <summary>Optional, off by default: draw the text with the game's font.</summary>
        [ObservableProperty]
        private bool _showPreview;

        /// <summary>Game file the preview fonts come from (loose agsfnt files in its folder win, like in the engine).</summary>
        [ObservableProperty]
        private string _previewGame = string.Empty;

        [ObservableProperty]
        private GameFont? _previewFont;

        [ObservableProperty]
        private Bitmap? _previewImage;

        [ObservableProperty]
        private string _previewNote = string.Empty;

        /// <summary>Fonts of <see cref="PreviewGame"/>.</summary>
        public ObservableCollection<GameFont> PreviewFonts { get; } = new();

        /// <summary>Font number to select after loading (remembered in the settings).</summary>
        public int PreferredPreviewFont { get; set; } = -1;

        public LiveViewModel(MainWindowViewModel main)
        {
            _main = main;
            _autoSaveTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(1.5) };
            _autoSaveTimer.Tick += (s, e) => SavePending();
            Items.CollectionChanged += (s, e) => UpdatePreview();
        }

        partial void OnShowPreviewChanged(bool value)
        {
            LoadPreviewFontsIfNeeded();
            UpdatePreview();
        }

        /// <summary>Wrap long lines in the preview at <see cref="WrapWidth"/>, roughly like the game.</summary>
        [ObservableProperty]
        private bool _wrapLikeGame;

        /// <summary>Wrap width in game pixels; set from the game's resolution when its fonts are loaded.</summary>
        [ObservableProperty]
        private int _wrapWidth;

        /// <summary>The width estimated from the game's resolution, for "reset".</summary>
        public int EstimatedWrapWidth { get; private set; }

        partial void OnWrapLikeGameChanged(bool value) => UpdatePreview();

        partial void OnWrapWidthChanged(int value) => UpdatePreview();

        [RelayCommand]
        private void ResetWrapWidth()
        {
            if (EstimatedWrapWidth > 0)
                WrapWidth = EstimatedWrapWidth;
        }

        private void LoadPreviewFontsIfNeeded()
        {
            if (IsActive && ShowPreview && PreviewFonts.Count == 0 && PreviewGame.Length > 0)
                _ = LoadPreviewFontsAsync(PreviewGame);
        }

        partial void OnSelectedItemChanged(LiveItem? value) => UpdatePreview();

        partial void OnPreviewFontChanged(GameFont? value)
        {
            if (value != null)
                PreferredPreviewFont = value.Number;
            UpdatePreview();
        }

        /// <summary>Loads the fonts of a game for the preview, in the background.</summary>
        public async Task LoadPreviewFontsAsync(string gameFile)
        {
            PreviewNote = Loc.T("Loading the game's fonts...");
            try
            {
                (List<GameFont> fonts, GameInfo? info) = await Task.Run(() => (GameFonts.Load(gameFile), GameInfo.GetGameInfo(gameFile)));
                EstimatedWrapWidth = info?.EstimatedWrapWidth ?? 0;
                // A width chosen for this game stays; another game starts with its estimate
                if (WrapWidth <= 0 || gameFile != PreviewGame)
                    WrapWidth = EstimatedWrapWidth;
                CloseFonts();
                PreviewFonts.Clear();
                foreach (GameFont font in fonts.Where(f => f.Error == null))
                    PreviewFonts.Add(font);
                PreviewGame = gameFile;
                int wanted = PreferredPreviewFont >= 0 ? PreferredPreviewFont : _main.Options.SpeechFont;
                PreviewFont = PreviewFonts.FirstOrDefault(f => f.Number == wanted) ?? PreviewFonts.FirstOrDefault();
                PreviewNote = PreviewFonts.Count == 0 ? Loc.T("The game has no fonts that can be read.") : string.Empty;
            }
            catch (Exception ex)
            {
                PreviewNote = Loc.F("Could not load the fonts: {0}", ex.Message);
            }
            _previewKey = null;
            UpdatePreview();
        }

        /// <summary>Draws the selected text, or the newest one, with the chosen font. Only redraws when text or font changed.</summary>
        private void UpdatePreview()
        {
            if (!ShowPreview || !IsActive)
            {
                _previewKey = null;
                PreviewImage = null;
                return;
            }
            LiveItem? item = SelectedItem ?? Items.FirstOrDefault();
            GameFont? font = PreviewFont;
            if (item == null || font == null)
            {
                PreviewImage = null;
                if (font == null && PreviewNote.Length == 0)
                    PreviewNote = Loc.T("Choose the game file to load its fonts.");
                return;
            }
            string text = TextEscaping.ToDisplay(item.Shown);
            int wrap = WrapLikeGame ? WrapWidth : 0;
            string key = $"{font.Number}\u0001{wrap}\u0001{text}";
            if (key == _previewKey)
                return;
            _previewKey = key;
            try
            {
                if (!_openFonts.TryGetValue(font, out EditableFont? editable))
                    _openFonts[font] = editable = EditableFont.Open(font);
                // Long texts are cut, the preview is about how the words and letters look
                if (text.Length > 600)
                    text = text[..600];
                TextPreview preview = TextPreview.Render(editable!, text, _main.Options, wrap);
                // Whole pixels, as large as the panel (about 320 wide) allows, at most 3x; wider ones are scaled down
                int scale = Math.Clamp(320 / Math.Max(1, preview.Canvas.Width), 1, 3);
                PreviewImage = preview.Canvas.ToBitmap(scale);
                PreviewNote = preview.Missing.Count > 0
                    ? Loc.F("Missing in the font (red): {0}", string.Join(" ", preview.Missing))
                    : string.Empty;
            }
            catch (Exception ex)
            {
                _openFonts[font] = null;
                PreviewImage = null;
                PreviewNote = Loc.F("This font cannot be drawn: {0}", ex.Message);
            }
        }

        private void CloseFonts()
        {
            foreach (EditableFont? font in _openFonts.Values)
                font?.Dispose();
            _openFonts.Clear();
            _previewKey = null;
        }

        partial void OnIsActiveChanged(bool value)
        {
            if (value)
            {
                Start();
                LoadPreviewFontsIfNeeded();
            }
            else
            {
                Stop();
                // Fonts are only kept while live mode is on
                CloseFonts();
                PreviewFonts.Clear();
                PreviewImage = null;
            }
        }

        private void Start()
        {
            if (!OperatingSystem.IsLinux() && !OperatingSystem.IsWindows())
            {
                Fail(Loc.T("Live mode works with games on Linux and Windows."));
                return;
            }
            try
            {
                var server = new LiveServer(encoding: GameEncoding());
                server.TextShown += text => Dispatcher.UIThread.Post(() => OnTextShown(text));
                server.ConnectionChanged += connected => Dispatcher.UIThread.Post(() => StatusText = connected
                    ? Loc.F("Game connected (process {0}). Edits appear the next time the text is shown.", server.GameProcessId)
                    : Loc.T("Game closed. Waiting for it to start again."));
                server.Start();
                _server = server;
                StatusText = OperatingSystem.IsWindows()
                    ? Loc.T("Waiting for the game. Start it, then attach to it below.")
                    : Loc.T("Waiting for the game. Attach to a running game below, or start it with:\ntools/ags-test.sh --live -t <file.tra> <game folder>");
                _ = RefreshGamesAsync();
            }
            catch (Exception ex)
            {
                Fail(ex.Message);
            }
        }

        // The live panel is hidden when live mode is off, so the reason goes to the status bar. Switched off
        // after this change notification, or the toggle button would stay pressed.
        private void Fail(string message)
        {
            StatusText = message;
            _main.FileStatusText = Loc.F("Live mode: {0}", message);
            Dispatcher.UIThread.Post(() => IsActive = false);
        }

        /// <summary>Looks for running AGS games; the newest one without the hook is selected.</summary>
        [RelayCommand(CanExecute = nameof(CanRefreshGames))]
        private async Task RefreshGamesAsync()
        {
            string? folder = GameAttacher.FindFolder();
            if (folder == null)
            {
                RunningGames.Clear();
                return;
            }
            try
            {
                var games = await Task.Run(() => GameAttacher.ListAsync(folder));
                RunningGames.Clear();
                foreach (RunningGame game in games.OrderByDescending(g => g.ProcessId))
                    RunningGames.Add(game);
                SelectedGame = RunningGames.FirstOrDefault(g => !g.IsHooked) ?? RunningGames.FirstOrDefault();
            }
            catch (Exception ex)
            {
                StatusText = ex.Message;
            }
        }

        private bool CanRefreshGames() => !IsAttaching;

        /// <summary>Loads the live hook into the selected running game (agsinject).</summary>
        [RelayCommand(CanExecute = nameof(CanAttach))]
        private async Task AttachAsync()
        {
            RunningGame? game = SelectedGame;
            string? folder = GameAttacher.FindFolder();
            if (game == null || _server == null)
                return;
            if (folder == null)
            {
                StatusText = Loc.T("agsinject was not found next to the program (build it with: make -C AGSLive, on Windows make -C AGSLive/win).");
                return;
            }
            IsAttaching = true;
            StatusText = Loc.F("Attaching to {0} (process {1})...", game.Name, game.ProcessId);
            try
            {
                string? error = await Task.Run(() => GameAttacher.AttachAsync(folder, game.ProcessId));
                if (error != null)
                    StatusText = Loc.F("Could not attach: {0}", error);
                else if (_server?.IsConnected != true)
                    StatusText = Loc.F("Hook loaded into {0}. Waiting for it to connect...", game.Name);
            }
            catch (Exception ex)
            {
                StatusText = Loc.F("Could not attach: {0}", ex.Message);
            }
            finally
            {
                IsAttaching = false;
            }
            await RefreshGamesAsync();
        }

        private bool CanAttach() => SelectedGame != null && !IsAttaching;

        private void Stop()
        {
            SavePending();
            _server?.Dispose();
            _server = null;
        }

        /// <summary>A different translation was loaded: corrections for the previous one no longer apply.</summary>
        public void OnDocumentLoaded()
        {
            Items.Clear();
            _previewKey = null; // the encoding may have changed
            if (_server == null)
                return;
            _server.Encoding = GameEncoding();
            _server.ClearCorrections();
        }

        // The translation's text encoding is the game's: its codepage, or UTF-8
        private Encoding GameEncoding() => _main.Options.IsUtf8
            ? AgsTextEncoding.Utf8
            : AgsTextEncoding.ForCodepage(_main.Options.AnsiCodepage);

        private void OnTextShown(LiveText text)
        {
            TranslationEntry? entry = _main.FindEntry(TextEscaping.ToTrs(text.Original));
            // Some texts go through the translation twice; the second time the "original" is the translation
            if (entry == null && _main.IsTranslation(text.Original))
                return;

            LiveItem? item = Items.FirstOrDefault(i => i.Original == text.Original);
            if (item == null)
            {
                item = new LiveItem(text.Original, entry);
            }
            else if (item == SelectedItem || Items.IndexOf(item) == 0)
            {
                // Don't move the text that is being edited
                item.Shown = text.Shown;
                UpdatePreview();
                return;
            }
            else
            {
                Items.Remove(item);
            }
            item.Shown = text.Shown;
            Items.Insert(0, item);
            while (Items.Count > MaxItems)
                Items.RemoveAt(Items.Count - 1);
        }

        /// <summary>Sends an edited translation to the game and schedules saving the TRS.</summary>
        public void OnEntryEdited(TranslationEntry entry)
        {
            if (_server == null)
                return;
            string original = TextEscaping.ToRuntime(entry.Key);
            // An emptied translation shows the original, as the TRA will after compiling
            string shown = string.IsNullOrEmpty(entry.Value)
                ? original
                : TextEscaping.ToRuntime(MainWindowViewModel.EnsureVoicePrefix(entry.Key, entry.Value));
            _server.SetCorrection(original, shown);
            foreach (LiveItem item in Items.Where(i => i.Entry == entry))
            {
                item.Shown = shown;
                item.IsCorrected = true;
            }
            UpdatePreview();
            if (AutoSave)
            {
                _autoSaveTimer.Stop();
                _autoSaveTimer.Start();
            }
        }

        private void SavePending()
        {
            if (!_autoSaveTimer.IsEnabled)
                return;
            _autoSaveTimer.Stop();
            if (!_main.SaveIfTrs())
                StatusText = Loc.T("Not saved automatically: only .trs files are saved (use File > Save As).");
        }

        public void Dispose()
        {
            Stop();
            CloseFonts();
        }
    }
}
