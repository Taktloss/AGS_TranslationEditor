using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Media;
using Avalonia.Styling;
using AGS_TranslationEditor.Models;
using AGS_TranslationEditor.ViewModels;
using AGSFontEditor.Localization;

namespace AGS_TranslationEditor.Views
{
    /// <summary>
    /// The main window: layout, keyboard shortcuts and what only a window can do (other windows, column
    /// widths, the spell bar). The commands themselves are in the view model (Files, Game, Ai).
    /// </summary>
    public partial class MainWindow : Window
    {
        private MainWindowViewModel ViewModel => (MainWindowViewModel)DataContext!;

        private readonly AppSettings _settings;

        public MainWindow()
        {
            _settings = AppSettings.Load();

            // Restore window dimensions before controls are created
            Width = _settings.WindowWidth;
            Height = _settings.WindowHeight;
            if (_settings.WindowX >= 0 && _settings.WindowY >= 0)
                WindowStartupLocation = WindowStartupLocation.Manual;

            InitializeComponent();

            if (Application.Current != null)
                Application.Current.RequestedThemeVariant =
                    _settings.Theme == "Light" ? ThemeVariant.Light : ThemeVariant.Dark;

            if (_settings.WindowX >= 0 && _settings.WindowY >= 0)
                Position = new PixelPoint((int)_settings.WindowX, (int)_settings.WindowY);

            AttachEventHandlers();
            RegisterKeyboardShortcuts();

            // Apply ViewModel-bound settings when DataContext is available
            DataContextChanged += (s, e) =>
            {
                if (DataContext is MainWindowViewModel vm)
                {
                    vm.Settings = _settings;
                    vm.Dialogs = new WindowDialogs(this);
                    vm.EditorFontSize = _settings.FontSize;
                    vm.SaveEncoding = _settings.Encoding;
                    vm.ShowContext = _settings.ShowContext;
                    RestoreColumnWidths();
                    vm.RevealEntry += entry => Table.ScrollIntoView(entry, null);
                    vm.Live.PreferredPreviewFont = _settings.LivePreviewChosenFont;
                    vm.Live.PreviewGame = File.Exists(_settings.LivePreviewGame) ? _settings.LivePreviewGame : "";
                    vm.Live.WrapWidths = new Dictionary<string, int>(_settings.LivePreviewWrapWidths ?? new(), StringComparer.Ordinal);
                    vm.Live.WrapLikeGame = _settings.LivePreviewWrap;
                    vm.Live.ShowPreview = _settings.LivePreview;
                    vm.PropertyChanged += (_, args) =>
                    {
                        if (args.PropertyName == nameof(MainWindowViewModel.IsContextVisible))
                            ContextColumn.IsVisible = vm.IsContextVisible;
                    };
                    ContextColumn.IsVisible = vm.IsContextVisible;
                }
            };

            // Apply font family and restore splitter after layout
            Loaded += (s, e) =>
            {
                ApplyFontFamily();
                if (this.FindControl<Grid>("MainContentGrid") is { } grid)
                    grid.RowDefinitions[2].Height = new GridLength(_settings.SplitterPosition, GridUnitType.Pixel);
            };
        }

        private DataGrid Table => this.FindControl<DataGrid>("DgvTranslation")!;

        /// <summary>The optional "Context" column, the last one of the list.</summary>
        private DataGridColumn ContextColumn => Table.Columns[^1];

        private void ApplyFontFamily()
        {
            var fontFamily = _settings.MonospaceFont
                ? new FontFamily("Cascadia Code,Consolas,monospace")
                : FontFamily.Default;
            if (this.FindControl<TextBox>("TxtSource") is { } source) source.FontFamily = fontFamily;
            if (this.FindControl<TextBox>("TxtTranslation") is { } translation) translation.FontFamily = fontFamily;
        }

        private void RegisterKeyboardShortcuts()
        {
            KeyDown += (s, e) =>
            {
                switch (e.Key)
                {
                    case Key.S when e.KeyModifiers == KeyModifiers.Control:
                        ViewModel.Files.SaveCommand.Execute(null);
                        e.Handled = true;
                        break;
                    case Key.S when e.KeyModifiers == (KeyModifiers.Control | KeyModifiers.Shift):
                        ViewModel.Files.SaveAsCommand.Execute(null);
                        e.Handled = true;
                        break;
                    case Key.O when e.KeyModifiers == KeyModifiers.Control:
                        ViewModel.Files.OpenCommand.Execute(null);
                        e.Handled = true;
                        break;
                    // Text boxes have their own undo and handle these keys first
                    case Key.Z when e.KeyModifiers == KeyModifiers.Control:
                        ViewModel.UndoCommand.Execute(null);
                        e.Handled = true;
                        break;
                    case Key.Y when e.KeyModifiers == KeyModifiers.Control:
                    case Key.Z when e.KeyModifiers == (KeyModifiers.Control | KeyModifiers.Shift):
                        ViewModel.RedoCommand.Execute(null);
                        e.Handled = true;
                        break;
                    case Key.F3:
                        ViewModel.JumpToNextUntranslatedCommand.Execute(null);
                        e.Handled = true;
                        break;
                    case Key.F when e.KeyModifiers == KeyModifiers.Control:
                        this.FindControl<TextBox>("TxtSearch")?.Focus();
                        e.Handled = true;
                        break;
                }
            };
        }

        private void AttachEventHandlers()
        {
            Closing += MainWindow_Closing;
            Closed += (s, e) => ViewModel.Live.Dispose();

            this.FindControl<MenuItem>("MenuExit")!.Click += (s, e) => Close();
            this.FindControl<MenuItem>("MenuFontEditor")!.Click += (s, e) => AGSFontEditor.Views.FontEditorWindow.Open(this, ViewModel.LastGameFile);
            this.FindControl<MenuItem>("MenuSpriteEditor")!.Click += (s, e) => AGSFontEditor.Views.SpriteEditorWindow.Open(this, ViewModel.LastGameFile);
            this.FindControl<MenuItem>("MenuSettings")!.Click += async (s, e) => await ShowSettings();
            this.FindControl<MenuItem>("MenuAbout")!.Click += (s, e) => new AboutDialog().ShowDialog(this);
            AttachSpelling();

            // Enter in the search box searches
            this.FindControl<TextBox>("TxtSearch")!.KeyDown += (s, e) =>
            {
                if (e.Key == Key.Enter)
                {
                    ViewModel.FindCommand.Execute(null);
                    e.Handled = true;
                }
            };

            // Double-click a row → focus translation TextBox
            Table.DoubleTapped += (s, e) => this.FindControl<TextBox>("TxtTranslation")?.Focus();

            // Clicking a text of the running game or a finding selects its entry for editing
            var lstLive = this.FindControl<ListBox>("LstLive")!;
            lstLive.SelectionChanged += (s, e) =>
            {
                if (lstLive.SelectedItem is LiveItem { Entry: { } entry })
                    ViewModel.Reveal(entry);
            };
            var lstCheck = this.FindControl<ListBox>("LstCheck")!;
            lstCheck.SelectionChanged += (s, e) =>
            {
                if (lstCheck.SelectedItem is CheckItem { Entry: { } entry })
                    ViewModel.Reveal(entry);
            };
        }

        private async void MainWindow_Closing(object? sender, WindowClosingEventArgs e)
        {
            // Save window layout
            _settings.WindowWidth = Width;
            _settings.WindowHeight = Height;
            if (WindowState != WindowState.Maximized)
            {
                _settings.WindowX = Position.X;
                _settings.WindowY = Position.Y;
            }
            if (this.FindControl<Grid>("MainContentGrid") is { } grid && grid.RowDefinitions[2].Height is { IsAbsolute: true } h)
                _settings.SplitterPosition = h.Value;
            SaveColumnWidths();
            _settings.LivePreview = ViewModel.Live.ShowPreview;
            _settings.LivePreviewGame = ViewModel.Live.PreviewGame;
            _settings.LivePreviewChosenFont = ViewModel.Live.PreferredPreviewFont;
            _settings.LivePreviewWrap = ViewModel.Live.WrapLikeGame;
            _settings.LivePreviewWrapWidths = new Dictionary<string, int>(ViewModel.Live.WrapWidths);
            _settings.Save();

            if (ViewModel.DocumentChanged)
            {
                e.Cancel = true;
                if (await ViewModel.Dialogs.AskAsync(Loc.F("Save changes to {0}?", Path.GetFileName(ViewModel.CurrentFilename))))
                    ViewModel.SaveFile(ViewModel.CurrentFilename);
                ViewModel.ResetDocumentChanged();
                Close();
            }
        }

        private async System.Threading.Tasks.Task ShowSettings()
        {
            await new SettingsWindow(_settings).ShowDialog(this);
            // Re-apply font and ViewModel-bound settings after dialog closes
            ApplyFontFamily();
            ViewModel.EditorFontSize = _settings.FontSize;
            ViewModel.SaveEncoding = _settings.Encoding;
            ViewModel.ShowContext = _settings.ShowContext;
            SpellingSettingsChanged();
        }

        // Source and translation keep their share of the width when the window is resized
        private void SaveColumnWidths()
        {
            var columns = Table.Columns;
            if (columns.Count < 4)
                return;
            double source = columns[1].ActualWidth, translation = columns[2].ActualWidth;
            if (source <= 0 || translation <= 0)
                return;
            _settings.ColumnWidths = new[] { columns[0].ActualWidth, source / (source + translation) * 2,
                translation / (source + translation) * 2, columns[3].IsVisible ? columns[3].ActualWidth
                : columns[3].Width.IsAbsolute ? columns[3].Width.Value : 200 };
        }

        private void RestoreColumnWidths()
        {
            var columns = Table.Columns;
            double[] w = _settings.ColumnWidths;
            if (w.Length != 4 || columns.Count < 4 || w.Any(v => !double.IsFinite(v) || v <= 0))
                return;
            columns[0].Width = new DataGridLength(Math.Clamp(w[0], 24, 200));
            columns[1].Width = new DataGridLength(Math.Clamp(w[1], 0.1, 1.9), DataGridLengthUnitType.Star);
            columns[2].Width = new DataGridLength(Math.Clamp(w[2], 0.1, 1.9), DataGridLengthUnitType.Star);
            columns[3].Width = new DataGridLength(Math.Clamp(w[3], 40, 800));
        }
    }
}
