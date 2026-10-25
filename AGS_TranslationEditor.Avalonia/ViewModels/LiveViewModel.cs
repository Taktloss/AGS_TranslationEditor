using System;
using System.Collections.ObjectModel;
using System.Linq;
using System.Text;
using AGS_TranslationEditor.Models;
using AGSTools;
using Avalonia.Threading;
using CommunityToolkit.Mvvm.ComponentModel;

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
            if (!OperatingSystem.IsLinux())
            {
                StatusText = "Live mode needs a game running on the Linux AGS engine.";
                IsActive = false;
                return;
            }
            try
            {
                var server = new LiveServer(encoding: GameEncoding());
                server.TextShown += text => Dispatcher.UIThread.Post(() => OnTextShown(text));
                server.ConnectionChanged += connected => Dispatcher.UIThread.Post(() => StatusText = connected
                    ? $"Game connected (process {server.GameProcessId}). Edits appear the next time the text is shown."
                    : "Game closed. Waiting for it to start again.");
                server.Start();
                _server = server;
                StatusText = "Waiting for the game. Start it with:\ntools/ags-test.sh --live -t <file.tra> <game folder>";
            }
            catch (Exception ex)
            {
                StatusText = ex.Message;
                IsActive = false;
            }
        }

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
                StatusText = "Not saved automatically: only .trs files are saved (use File > Save As).";
        }

        public void Dispose() => Stop();
    }
}
