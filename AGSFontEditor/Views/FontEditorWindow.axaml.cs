using AGSFontEditor.Localization;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using AGSFontEditor.Controls;
using AGSFontEditor.ViewModels;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Platform.Storage;

namespace AGSFontEditor.Views
{
    /// <summary>
    /// Font unpacker, viewer and editor window. Used by the standalone AGS Font Editor and by the
    /// translation editor (Tools → Font Editor).
    /// </summary>
    public partial class FontEditorWindow : Window
    {
        private static FilePickerFileType[] GameFileTypes => new FilePickerFileType[]
        {
            new(Loc.T("AGS Game Files")) { Patterns = new[] { "*.ags", "*.exe", "*.bin", "*.dat", "game28.dta", "ac2game.dta" } },
            new(Loc.T("All Files")) { Patterns = new[] { "*" } },
        };

        private static FilePickerFileType[] FontFileTypes => new FilePickerFileType[]
        {
            new(Loc.T("AGS Fonts")) { Patterns = new[] { "*.ttf", "*.wfn" } },
            new(Loc.T("All Files")) { Patterns = new[] { "*" } },
        };

        private bool _closeConfirmed;

        public FontEditorWindow() : this(new FontEditorViewModel()) { }

        public FontEditorWindow(FontEditorViewModel viewModel)
        {
            InitializeComponent();
            DataContext = viewModel;

            this.FindControl<Button>("BtnOpenGame")!.Click += async (s, e) => await OpenGame();
            this.FindControl<Button>("BtnOpenFonts")!.Click += async (s, e) => await OpenFonts();
            this.FindControl<Button>("BtnExportSelected")!.Click += async (s, e) =>
                await Export(ViewModel.SelectedFont != null ? new[] { ViewModel.SelectedFont } : Array.Empty<FontItemViewModel>());
            this.FindControl<Button>("BtnExportAll")!.Click += async (s, e) => await Export(ViewModel.Fonts.ToList());
            this.FindControl<Button>("BtnSaveAs")!.Click += async (s, e) => await SaveAs();

            var editor = this.FindControl<PixelEditor>("Editor")!;
            editor.StrokeStarted += (s, e) => ViewModel.BeginStroke();
            editor.StrokeCompleted += (s, e) => ViewModel.EndStroke();

            // Keep the selected glyph visible, e.g. after "Go to"
            var grid = this.FindControl<ListBox>("GlyphGrid")!;
            viewModel.PropertyChanged += (s, e) =>
            {
                if (e.PropertyName == nameof(FontEditorViewModel.SelectedCell) && viewModel.SelectedCell is { } cell)
                    Avalonia.Threading.Dispatcher.UIThread.Post(() => grid.ScrollIntoView(cell));
            };

            Closing += OnClosing;
        }

        public FontEditorViewModel ViewModel => (FontEditorViewModel)DataContext!;

        /// <summary>Opens the window and loads a game or font files (by extension) if given.</summary>
        public static FontEditorWindow Open(Window? owner, string? path)
        {
            var window = new FontEditorWindow();
            if (owner != null)
                window.Show(owner);
            else
                window.Show();
            if (path != null)
                _ = window.LoadPath(path);
            return window;
        }

        /// <summary>Loads a game, or a single font file if the path is a .ttf/.wfn file.</summary>
        public Task LoadPath(string path)
        {
            string ext = Path.GetExtension(path);
            if (ext.Equals(".ttf", StringComparison.OrdinalIgnoreCase) || ext.Equals(".wfn", StringComparison.OrdinalIgnoreCase))
            {
                ViewModel.OpenFontFiles(new[] { path });
                return Task.CompletedTask;
            }
            return ViewModel.LoadGameAsync(path);
        }

        private async Task<bool> ConfirmDiscard()
        {
            if (!ViewModel.HasUnsavedChanges)
                return true;
            return await Dialogs.YesNo(this, Loc.T("Some fonts have unsaved changes. Discard them?"));
        }

        private async Task OpenGame()
        {
            if (!await ConfirmDiscard())
                return;
            var files = await StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions
            {
                Title = Loc.T("Select AGS Game File"),
                AllowMultiple = false,
                FileTypeFilter = GameFileTypes,
            });
            if (files.Count > 0)
                await ViewModel.LoadGameAsync(files[0].Path.LocalPath);
        }

        private async Task OpenFonts()
        {
            if (!await ConfirmDiscard())
                return;
            var files = await StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions
            {
                Title = Loc.T("Open Font Files"),
                AllowMultiple = true,
                FileTypeFilter = FontFileTypes,
            });
            if (files.Count > 0)
                ViewModel.OpenFontFiles(files.Select(f => f.Path.LocalPath));
        }

        private async Task Export(IReadOnlyList<FontItemViewModel> fonts)
        {
            if (fonts.Count == 0)
            {
                ViewModel.StatusText = Loc.T("No font to export.");
                return;
            }
            var folders = await StorageProvider.OpenFolderPickerAsync(new FolderPickerOpenOptions
            {
                Title = fonts.Count == 1 ? Loc.F("Export {0} To", fonts[0].Font.FileName) : Loc.F("Export {0} Fonts To", fonts.Count),
                AllowMultiple = false,
            });
            if (folders.Count == 0)
                return;
            string folder = folders[0].Path.LocalPath;
            if (ViewModel.IsGameFolder(folder))
            {
                await Dialogs.Message(this, Loc.T("Please choose a folder other than the game folder: font files there override the game's fonts."));
                return;
            }
            var existing = FontEditorViewModel.ExistingExportFiles(fonts, folder);
            if (existing.Count > 0 && !await Dialogs.YesNo(this,
                    Loc.F("{0} file(s) already exist in this folder ({1}). Overwrite them?", existing.Count,
                        string.Join(", ", existing.Take(3).Select(Path.GetFileName)) + (existing.Count > 3 ? ", ..." : ""))))
                return;
            ViewModel.Export(fonts, folder);
            if (fonts.Any(f => f.IsModified))
                ViewModel.StatusText += " " + Loc.T("(Original files; use Save Font As to save your changes.)");
        }

        private async Task SaveAs()
        {
            FontItemViewModel? font = ViewModel.SelectedFont;
            if (font == null)
                return;
            string ext = Path.GetExtension(font.Font.FileName).TrimStart('.').ToLowerInvariant();
            var file = await StorageProvider.SaveFilePickerAsync(new FilePickerSaveOptions
            {
                Title = Loc.F("Save {0} As", font.Font.FileName),
                SuggestedFileName = font.Font.FileName,
                DefaultExtension = ext,
                ShowOverwritePrompt = true,
                FileTypeChoices = new[] { new FilePickerFileType(ext == "wfn" ? Loc.T("WFN Font") : Loc.T("TrueType Font")) { Patterns = new[] { "*." + ext } } },
            });
            if (file != null)
                ViewModel.SaveSelected(file.Path.LocalPath);
        }

        private async void OnClosing(object? sender, WindowClosingEventArgs e)
        {
            if (_closeConfirmed || !ViewModel.HasUnsavedChanges)
                return;
            e.Cancel = true;
            if (await ConfirmDiscard())
            {
                _closeConfirmed = true;
                Close();
            }
        }
    }

    /// <summary>Simple modal message boxes.</summary>
    internal static class Dialogs
    {
        public static async Task<bool> YesNo(Window owner, string message)
        {
            bool result = false;
            var dialog = CreateDialog(message, out StackPanel buttons);
            var yes = new Button { Content = Loc.T("Yes") };
            var no = new Button { Content = Loc.T("No") };
            yes.Click += (s, e) => { result = true; dialog.Close(); };
            no.Click += (s, e) => dialog.Close();
            buttons.Children.Add(yes);
            buttons.Children.Add(no);
            await dialog.ShowDialog(owner);
            return result;
        }

        public static async Task Message(Window owner, string message)
        {
            var dialog = CreateDialog(message, out StackPanel buttons);
            var ok = new Button { Content = Loc.T("OK") };
            ok.Click += (s, e) => dialog.Close();
            buttons.Children.Add(ok);
            await dialog.ShowDialog(owner);
        }

        private static Window CreateDialog(string message, out StackPanel buttons)
        {
            var dialog = new Window
            {
                Title = Loc.T("AGS Font Editor"),
                Width = 420,
                SizeToContent = SizeToContent.Height,
                WindowStartupLocation = WindowStartupLocation.CenterOwner,
                CanResize = false,
                RequestedThemeVariant = Avalonia.Styling.ThemeVariant.Dark,
            };
            var panel = new StackPanel { Margin = new Avalonia.Thickness(16), Spacing = 12 };
            panel.Children.Add(new TextBlock { Text = message, TextWrapping = Avalonia.Media.TextWrapping.Wrap });
            buttons = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right, Spacing = 8 };
            panel.Children.Add(buttons);
            dialog.Content = panel;
            return dialog;
        }
    }
}
