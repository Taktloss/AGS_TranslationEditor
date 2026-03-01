using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Platform.Storage;
using AGS_TranslationEditor.ViewModels;
using AGSTools;

namespace AGS_TranslationEditor.Views
{
    public partial class MainWindow : Window
    {
        private MainWindowViewModel ViewModel => (MainWindowViewModel)DataContext!;

        public MainWindow()
        {
            InitializeComponent();
            AttachEventHandlers();
        }

        private void AttachEventHandlers()
        {
            Closing += MainWindow_Closing;

            var menuOpen = this.FindControl<MenuItem>("MenuOpen")!;
            var menuSave = this.FindControl<MenuItem>("MenuSave")!;
            var menuSaveAs = this.FindControl<MenuItem>("MenuSaveAs")!;
            var menuExportCSV = this.FindControl<MenuItem>("MenuExportCSV")!;
            var menuExportPO = this.FindControl<MenuItem>("MenuExportPO")!;
            var menuExit = this.FindControl<MenuItem>("MenuExit")!;
            var menuExtract = this.FindControl<MenuItem>("MenuExtractScript")!;
            var menuGameInfo = this.FindControl<MenuItem>("MenuGameInfo")!;
            var menuCreateTRA = this.FindControl<MenuItem>("MenuCreateTRA")!;
            var menuAbout = this.FindControl<MenuItem>("MenuAbout")!;
            var btnOpen = this.FindControl<Button>("BtnOpen")!;
            var btnSave = this.FindControl<Button>("BtnSave")!;
            var btnFind = this.FindControl<Button>("BtnFind")!;
            var btnFindPrev = this.FindControl<Button>("BtnFindPrev")!;
            var btnFindNext = this.FindControl<Button>("BtnFindNext")!;

            menuOpen.Click += async (s, e) => await OpenFile();
            menuSave.Click += (s, e) => SaveFile();
            menuSaveAs.Click += async (s, e) => await SaveFileAs();
            menuExportCSV.Click += async (s, e) => await ExportCSV();
            menuExportPO.Click += async (s, e) => await ExportPO();
            menuExit.Click += (s, e) => Close();
            menuExtract.Click += async (s, e) => await ExtractScript();
            menuGameInfo.Click += async (s, e) => await ShowGameInfo();
            menuCreateTRA.Click += async (s, e) => await CreateTRA();
            menuAbout.Click += (s, e) => ShowAbout();
            btnOpen.Click += async (s, e) => await OpenFile();
            btnSave.Click += (s, e) => SaveFile();
            btnFind.Click += (s, e) => Find();
            btnFindPrev.Click += (s, e) => FindPrev();
            btnFindNext.Click += (s, e) => FindNext();
        }

        private async void MainWindow_Closing(object? sender, WindowClosingEventArgs e)
        {
            if (ViewModel.DocumentChanged)
            {
                e.Cancel = true;
                var result = await ShowYesNoDialog($"Save changes to {Path.GetFileName(ViewModel.CurrentFilename)}?");
                if (result)
                {
                    ViewModel.SaveFile(ViewModel.CurrentFilename);
                }
                ViewModel.ResetDocumentChanged();
                Close();
            }
        }

        private async System.Threading.Tasks.Task OpenFile()
        {
            var files = await StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions
            {
                Title = "Open Translation File",
                AllowMultiple = false,
                FileTypeFilter = new[]
                {
                    new FilePickerFileType("AGS Translation Files") { Patterns = new[] { "*.tra", "*.trs" } },
                    new FilePickerFileType("CSV/TSV Files") { Patterns = new[] { "*.csv", "*.tsv" } },
                    new FilePickerFileType("PO Files") { Patterns = new[] { "*.po" } },
                    new FilePickerFileType("All Files") { Patterns = new[] { "*" } },
                }
            });

            if (files.Count > 0)
            {
                string filename = files[0].Path.LocalPath;
                ViewModel.LoadFile(filename);
            }
        }

        private void SaveFile()
        {
            if (string.IsNullOrEmpty(ViewModel.CurrentFilename))
            {
                _ = SaveFileAs();
                return;
            }
            ViewModel.SaveFile(ViewModel.CurrentFilename);
        }

        private async System.Threading.Tasks.Task SaveFileAs()
        {
            var file = await StorageProvider.SaveFilePickerAsync(new FilePickerSaveOptions
            {
                Title = "Save Translation File",
                DefaultExtension = "trs",
                FileTypeChoices = new[]
                {
                    new FilePickerFileType("TRS Translation File") { Patterns = new[] { "*.trs" } },
                }
            });

            if (file != null)
            {
                ViewModel.SaveFile(file.Path.LocalPath);
            }
        }

        private async System.Threading.Tasks.Task ExportCSV()
        {
            var file = await StorageProvider.SaveFilePickerAsync(new FilePickerSaveOptions
            {
                Title = "Export as CSV",
                DefaultExtension = "csv",
                FileTypeChoices = new[] { new FilePickerFileType("CSV File") { Patterns = new[] { "*.csv" } } }
            });
            if (file != null)
            {
                var data = ViewModel.Entries.ToDictionary(e => e.Key, e => e.Value);
                CSVFormat.CreateCSV(file.Path.LocalPath, data);
            }
        }

        private async System.Threading.Tasks.Task ExportPO()
        {
            var file = await StorageProvider.SaveFilePickerAsync(new FilePickerSaveOptions
            {
                Title = "Export as PO",
                DefaultExtension = "po",
                FileTypeChoices = new[] { new FilePickerFileType("PO File") { Patterns = new[] { "*.po" } } }
            });
            if (file != null)
            {
                var data = ViewModel.Entries.ToDictionary(e => e.Key, e => e.Value);
                POFormat.CreatePO(file.Path.LocalPath, data);
            }
        }

        private async System.Threading.Tasks.Task ExtractScript()
        {
            var files = await StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions
            {
                Title = "Select Game EXE or bin file",
                FileTypeFilter = new[] { new FilePickerFileType("AGS Files") { Patterns = new[] { "*.exe", "*.bin" } } }
            });
            if (files.Count > 0)
            {
                Extraction.ParseAGSFile(files[0].Path.LocalPath);
                ViewModel.FileStatusText = "Script extracted.";
            }
        }

        private async System.Threading.Tasks.Task ShowGameInfo()
        {
            var files = await StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions
            {
                Title = "Select Game EXE",
                FileTypeFilter = new[] { new FilePickerFileType("AGS EXE") { Patterns = new[] { "*.exe" } } }
            });
            if (files.Count > 0)
            {
                var info = GameInfo.GetGameInfo(files[0].Path.LocalPath);
                if (info != null)
                    await ShowInfoDialog($"Title: {info.GameTitle}\nUID: {info.GameUID}");
            }
        }

        private async System.Threading.Tasks.Task CreateTRA()
        {
            var exeFiles = await StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions
            {
                Title = "Select Game EXE",
                FileTypeFilter = new[] { new FilePickerFileType("EXE") { Patterns = new[] { "*.exe" } } }
            });
            if (exeFiles.Count == 0) return;

            var trsFiles = await StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions
            {
                Title = "Select TRS File",
                FileTypeFilter = new[] { new FilePickerFileType("TRS") { Patterns = new[] { "*.trs" } } }
            });
            if (trsFiles.Count == 0) return;

            var traFile = await StorageProvider.SaveFilePickerAsync(new FilePickerSaveOptions
            {
                Title = "Save TRA File",
                DefaultExtension = "tra",
                FileTypeChoices = new[] { new FilePickerFileType("TRA") { Patterns = new[] { "*.tra" } } }
            });
            if (traFile == null) return;

            var info = GameInfo.GetGameInfo(exeFiles[0].Path.LocalPath);
            if (info != null)
            {
                Translation.CreateTRA_File(info, traFile.Path.LocalPath,
                    Translation.ParseTRS_Translation(trsFiles[0].Path.LocalPath));
                ViewModel.FileStatusText = "TRA file created.";
            }
        }

        private void ShowAbout()
        {
            var about = new AboutDialog();
            about.ShowDialog(this);
        }

        // Search functionality
        private List<int> _foundIndices = new();
        private int _currentFindIndex = -1;

        private void Find()
        {
            var searchBox = this.FindControl<TextBox>("TxtSearch")!;
            string searchText = searchBox.Text ?? string.Empty;
            if (string.IsNullOrEmpty(searchText)) return;

            _foundIndices = new List<int>();
            for (int i = 0; i < ViewModel.Entries.Count; i++)
            {
                if (ViewModel.Entries[i].Key.Contains(searchText, StringComparison.OrdinalIgnoreCase) ||
                    ViewModel.Entries[i].Value.Contains(searchText, StringComparison.OrdinalIgnoreCase))
                    _foundIndices.Add(i);
            }

            _currentFindIndex = -1;
            ViewModel.FileStatusText = $"Found {_foundIndices.Count} matches";
            if (_foundIndices.Count > 0) FindNext();
        }

        private void FindNext()
        {
            if (_foundIndices.Count == 0) return;
            _currentFindIndex = (_currentFindIndex + 1) % _foundIndices.Count;
            SelectRow(_foundIndices[_currentFindIndex]);
        }

        private void FindPrev()
        {
            if (_foundIndices.Count == 0) return;
            _currentFindIndex = (_currentFindIndex - 1 + _foundIndices.Count) % _foundIndices.Count;
            SelectRow(_foundIndices[_currentFindIndex]);
        }

        private void SelectRow(int index)
        {
            var dgv = this.FindControl<DataGrid>("DgvTranslation")!;
            ViewModel.SelectedEntry = ViewModel.Entries[index];
            dgv.ScrollIntoView(ViewModel.Entries[index], null);
        }

        private async System.Threading.Tasks.Task<bool> ShowYesNoDialog(string message)
        {
            var dialog = new Window
            {
                Title = "AGS Translation Editor",
                Width = 380,
                Height = 150,
                WindowStartupLocation = WindowStartupLocation.CenterOwner,
                CanResize = false
            };
            bool result = false;
            var panel = new StackPanel { Margin = new Avalonia.Thickness(16), Spacing = 12 };
            panel.Children.Add(new TextBlock { Text = message, TextWrapping = Avalonia.Media.TextWrapping.Wrap });
            var buttons = new StackPanel { Orientation = Avalonia.Layout.Orientation.Horizontal, HorizontalAlignment = Avalonia.Layout.HorizontalAlignment.Right, Spacing = 8 };
            var yes = new Button { Content = "Yes" };
            var no = new Button { Content = "No" };
            yes.Click += (s, e) => { result = true; dialog.Close(); };
            no.Click += (s, e) => { result = false; dialog.Close(); };
            buttons.Children.Add(yes);
            buttons.Children.Add(no);
            panel.Children.Add(buttons);
            dialog.Content = panel;
            await dialog.ShowDialog(this);
            return result;
        }

        private async System.Threading.Tasks.Task ShowInfoDialog(string message)
        {
            var dialog = new Window
            {
                Title = "Game Info",
                Width = 350,
                Height = 160,
                WindowStartupLocation = WindowStartupLocation.CenterOwner,
                CanResize = false
            };
            var panel = new StackPanel { Margin = new Avalonia.Thickness(16), Spacing = 12 };
            panel.Children.Add(new TextBlock { Text = message, TextWrapping = Avalonia.Media.TextWrapping.Wrap });
            var ok = new Button { Content = "OK", HorizontalAlignment = Avalonia.Layout.HorizontalAlignment.Right };
            ok.Click += (s, e) => dialog.Close();
            panel.Children.Add(ok);
            dialog.Content = panel;
            await dialog.ShowDialog(this);
        }
    }
}
