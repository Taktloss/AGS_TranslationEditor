using AGSFontEditor.Localization;
using System;
using System.Collections.ObjectModel;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using AGS_TranslationEditor.Models;
using AGSTools;
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

        public LiveViewModel(MainWindowViewModel main)
        {
            _main = main;
            _autoSaveTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(1.5) };
            _autoSaveTimer.Tick += (s, e) => SavePending();
        }

        partial void OnIsActiveChanged(bool value)
        {
            if (value)
                Start();
            else
                Stop();
        }

        private void Start()
        {
            if (!OperatingSystem.IsLinux() && !OperatingSystem.IsWindows())
            {
                StatusText = Loc.T("Live mode works with games on Linux and Windows.");
                IsActive = false;
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
                StatusText = ex.Message;
                IsActive = false;
            }
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

        public void Dispose() => Stop();
    }
}
