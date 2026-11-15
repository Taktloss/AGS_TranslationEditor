using System;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using AGSFontEditor.Controls;
using AGSFontEditor.Localization;
using AGSFontEditor.ViewModels;
using AGSTools;
using Avalonia.Controls;
using Avalonia.Platform.Storage;

namespace AGSFontEditor.Views
{
    /// <summary>
    /// Image editor for the sprites of a game (Tools → Image Editor in the translation editor): texts drawn into
    /// pictures, e.g. letters, maps or menus, can be replaced and installed as a sprite patch.
    /// </summary>
    public partial class SpriteEditorWindow : Window
    {
        private static FilePickerFileType[] GameFileTypes => new FilePickerFileType[]
        {
            new(Loc.T("AGS Game Files")) { Patterns = new[] { "*.ags", "*.exe", "*.bin", "*.dat", "game28.dta", "ac2game.dta" } },
            new(Loc.T("All Files")) { Patterns = new[] { "*" } },
        };

        private static FilePickerFileType[] PngFileTypes => new FilePickerFileType[]
        {
            new(Loc.T("PNG Images")) { Patterns = new[] { "*.png" } },
        };

        private bool _closeConfirmed;

        public SpriteEditorWindow()
        {
            InitializeComponent();
            DataContext = new SpriteEditorViewModel();

            this.FindControl<Button>("BtnOpenGame")!.Click += async (s, e) => await OpenGame();
            this.FindControl<Button>("BtnExportSelected")!.Click += async (s, e) => await ExportSelected();
            this.FindControl<Button>("BtnExportList")!.Click += async (s, e) => await ExportList();
            this.FindControl<Button>("BtnImport")!.Click += async (s, e) => await ImportPng();
            this.FindControl<Button>("BtnImportFolder")!.Click += async (s, e) => await ImportFolder();
            this.FindControl<Button>("BtnBuildPatch")!.Click += async (s, e) => await BuildPatch();
            this.FindControl<SpriteCanvas>("Canvas")!.ColorPicked += (s, p) => ViewModel.PickColor(p);
            this.FindControl<Button>("BtnAddFont")!.Click += async (s, e) =>
            {
                var files = await StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions
                {
                    Title = Loc.T("Open Font Files"),
                    AllowMultiple = true,
                    FileTypeFilter = new FilePickerFileType[] { new(Loc.T("AGS Fonts")) { Patterns = new[] { "*.ttf", "*.wfn" } } },
                });
                if (files.Count > 0)
                    ViewModel.AddFontFiles(files.Select(f => f.Path.LocalPath));
            };

            Closing += OnClosing;
            Closed += (s, e) => ViewModel.Dispose();
        }

        public SpriteEditorViewModel ViewModel => (SpriteEditorViewModel)DataContext!;

        /// <summary>Opens the window and loads the sprites of a game if given.</summary>
        public static SpriteEditorWindow Open(Window? owner, string? gamePath)
        {
            var window = new SpriteEditorWindow();
            if (owner != null)
                window.Show(owner);
            else
                window.Show();
            if (gamePath != null)
                _ = window.ViewModel.LoadGameAsync(gamePath);
            return window;
        }

        private async Task<bool> ConfirmDiscard()
        {
            if (!ViewModel.HasChanges || ViewModel.PatchIsCurrent)
                return true;
            return await Dialogs.YesNo(this, Loc.T("Some sprites have changes that are not in a sprite patch yet. Discard them?"));
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

        private async Task<string?> PickFolder(string title)
        {
            var folders = await StorageProvider.OpenFolderPickerAsync(new FolderPickerOpenOptions { Title = title, AllowMultiple = false });
            if (folders.Count == 0)
                return null;
            string folder = folders[0].Path.LocalPath;
            if (ViewModel.IsGameFolder(folder))
            {
                await Dialogs.Message(this, Loc.T("Please choose a folder other than the game folder: files there change the game."));
                return null;
            }
            return folder;
        }

        private async Task ExportSelected()
        {
            if (ViewModel.SelectedItem is not { } item)
                return;
            var file = await StorageProvider.SaveFilePickerAsync(new FilePickerSaveOptions
            {
                Title = Loc.F("Save Sprite {0} As", item.Number),
                SuggestedFileName = GameSprites.PngName(item.Number),
                DefaultExtension = "png",
                ShowOverwritePrompt = true,
                FileTypeChoices = PngFileTypes,
            });
            if (file == null)
                return;
            string path = file.Path.LocalPath;
            if (ViewModel.IsGameFolder(Path.GetDirectoryName(path)!))
            {
                await Dialogs.Message(this, Loc.T("Please choose a folder other than the game folder: files there change the game."));
                return;
            }
            await ViewModel.ExportAsync(new[] { item.Number }, Path.GetDirectoryName(path)!, Path.GetFileName(path));
        }

        private async Task ExportList()
        {
            var numbers = ViewModel.VisibleNumbers;
            if (numbers.Count == 0)
                return;
            if (numbers.Count > 500 && !await Dialogs.YesNo(this, Loc.F("Save {0} sprites as PNG files? Use the filter to choose fewer.", numbers.Count)))
                return;
            string? folder = await PickFolder(Loc.F("Save {0} Sprites To", numbers.Count));
            if (folder == null)
                return;
            int existing = numbers.Count(n => File.Exists(Path.Combine(folder, GameSprites.PngName(n))));
            if (existing > 0 && !await Dialogs.YesNo(this, Loc.F("{0} file(s) already exist in this folder. Overwrite them?", existing)))
                return;
            await ViewModel.ExportAsync(numbers, folder);
        }

        private async Task ImportPng()
        {
            var files = await StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions
            {
                Title = Loc.T("Replace with PNG"),
                AllowMultiple = false,
                FileTypeFilter = PngFileTypes,
            });
            if (files.Count > 0)
                ViewModel.ImportPng(files[0].Path.LocalPath);
        }

        private async Task ImportFolder()
        {
            var folders = await StorageProvider.OpenFolderPickerAsync(new FolderPickerOpenOptions { Title = Loc.T("Load PNG Folder"), AllowMultiple = false });
            if (folders.Count > 0)
                await ViewModel.ImportFolderAsync(folders[0].Path.LocalPath);
        }

        private async Task BuildPatch()
        {
            string? folder = await PickFolder(Loc.T("Write Sprite Patch To"));
            if (folder == null)
                return;
            if ((File.Exists(Path.Combine(folder, SpriteFile.FileName)) || File.Exists(Path.Combine(folder, SpriteFile.IndexFileName)))
                && !await Dialogs.YesNo(this, Loc.T("This folder already has a sprite patch. Replace it?")))
                return;
            await ViewModel.BuildPatchAsync(folder);
        }

        private async void OnClosing(object? sender, WindowClosingEventArgs e)
        {
            if (_closeConfirmed || !ViewModel.HasChanges || ViewModel.PatchIsCurrent)
                return;
            e.Cancel = true;
            if (await ConfirmDiscard())
            {
                _closeConfirmed = true;
                Close();
            }
        }
    }
}
