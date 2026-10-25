using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Platform.Storage;
using Avalonia.Styling;
using AGS_TranslationEditor.Models;
using AGS_TranslationEditor.ViewModels;
using AGSTools;
using TranslationApi;

namespace AGS_TranslationEditor.Views
{
    public partial class MainWindow : Window
    {
        private MainWindowViewModel ViewModel => (MainWindowViewModel)DataContext!;

        private AppSettings _settings;

        /// <summary>The game file picked last in this session; the font editor opens it.</summary>
        private string? _lastGameFile;

        public MainWindow()
        {
            _settings = AppSettings.Load();

            // Restore window dimensions before controls are created
            Width = _settings.WindowWidth;
            Height = _settings.WindowHeight;
            if (_settings.WindowX >= 0 && _settings.WindowY >= 0)
                WindowStartupLocation = WindowStartupLocation.Manual;

            InitializeComponent();

            // Apply theme
            if (Application.Current != null)
                Application.Current.RequestedThemeVariant =
                    _settings.Theme == "Light" ? ThemeVariant.Light : ThemeVariant.Dark;

            // Restore window position if saved
            if (_settings.WindowX >= 0 && _settings.WindowY >= 0)
                Position = new PixelPoint((int)_settings.WindowX, (int)_settings.WindowY);

            AttachEventHandlers();
            RegisterKeyboardShortcuts();

            // Apply ViewModel-bound settings when DataContext is available
            DataContextChanged += (s, e) =>
            {
                if (DataContext is MainWindowViewModel vm)
                {
                    vm.EditorFontSize = _settings.FontSize;
                    vm.SaveEncoding = _settings.Encoding;
                }
            };

            // Apply font family and restore splitter after layout
            Loaded += (s, e) =>
            {
                ApplyFontFamily();

                var grid = this.FindControl<Grid>("MainContentGrid");
                if (grid != null)
                    grid.RowDefinitions[2].Height = new GridLength(_settings.SplitterPosition, GridUnitType.Pixel);
            };
        }

        private void ApplyFontFamily()
        {
            var fontFamily = _settings.MonospaceFont
                ? new FontFamily("Cascadia Code,Consolas,monospace")
                : FontFamily.Default;
            var txtSource = this.FindControl<TextBox>("TxtSource");
            var txtTranslation = this.FindControl<TextBox>("TxtTranslation");
            if (txtSource != null) txtSource.FontFamily = fontFamily;
            if (txtTranslation != null) txtTranslation.FontFamily = fontFamily;
        }

        private void RegisterKeyboardShortcuts()
        {
            KeyDown += (s, e) =>
            {
                switch (e.Key)
                {
                    case Key.S when e.KeyModifiers == KeyModifiers.Control:
                        SaveFile();
                        e.Handled = true;
                        break;
                    case Key.O when e.KeyModifiers == KeyModifiers.Control:
                        _ = OpenFile();
                        e.Handled = true;
                        break;
                    case Key.F3:
                        JumpToNextUntranslated();
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

            var menuOpen = this.FindControl<MenuItem>("MenuOpen")!;
            var menuSave = this.FindControl<MenuItem>("MenuSave")!;
            var menuSaveAs = this.FindControl<MenuItem>("MenuSaveAs")!;
            var menuExportCSV = this.FindControl<MenuItem>("MenuExportCSV")!;
            var menuExportPO = this.FindControl<MenuItem>("MenuExportPO")!;
            var menuExportTRA = this.FindControl<MenuItem>("MenuExportTRA")!;
            var menuExit = this.FindControl<MenuItem>("MenuExit")!;
            var menuExtract = this.FindControl<MenuItem>("MenuExtractScript")!;
            var menuGameInfo = this.FindControl<MenuItem>("MenuGameInfo")!;
            var menuCreateTRA = this.FindControl<MenuItem>("MenuCreateTRA")!;
            var menuMergeGame = this.FindControl<MenuItem>("MenuMergeGame")!;
            var menuFontEditor = this.FindControl<MenuItem>("MenuFontEditor")!;
            var menuSettings = this.FindControl<MenuItem>("MenuSettings")!;
            var menuAbout = this.FindControl<MenuItem>("MenuAbout")!;
            var btnOpen = this.FindControl<Button>("BtnOpen")!;
            var btnSave = this.FindControl<Button>("BtnSave")!;
            var btnFind = this.FindControl<Button>("BtnFind")!;
            var btnFindPrev = this.FindControl<Button>("BtnFindPrev")!;
            var btnFindNext = this.FindControl<Button>("BtnFindNext")!;
            var btnFilterUntranslated = this.FindControl<ToggleButton>("BtnFilterUntranslated")!;
            var btnNextUntranslated = this.FindControl<Button>("BtnNextUntranslated")!;
            var btnTranslate = this.FindControl<Button>("BtnTranslate")!;
            var dgv = this.FindControl<DataGrid>("DgvTranslation")!;

            menuOpen.Click += async (s, e) => await OpenFile();
            menuSave.Click += (s, e) => SaveFile();
            menuSaveAs.Click += async (s, e) => await SaveFileAs();
            menuExportCSV.Click += async (s, e) => await ExportCSV();
            menuExportPO.Click += async (s, e) => await ExportPO();
            menuExportTRA.Click += async (s, e) => await ExportAsTRA();
            menuExit.Click += (s, e) => Close();
            menuExtract.Click += async (s, e) => await ExtractScript();
            menuGameInfo.Click += async (s, e) => await ShowGameInfo();
            menuCreateTRA.Click += async (s, e) => await CreateTRA();
            menuMergeGame.Click += async (s, e) => await MergeWithGame();
            menuFontEditor.Click += (s, e) => AGSFontEditor.Views.FontEditorWindow.Open(this, _lastGameFile);
            menuSettings.Click += async (s, e) => await ShowSettings();
            menuAbout.Click += (s, e) => ShowAbout();
            btnOpen.Click += async (s, e) => await OpenFile();
            btnSave.Click += (s, e) => SaveFile();
            btnFind.Click += (s, e) => Find();
            btnFindPrev.Click += (s, e) => FindPrev();
            btnFindNext.Click += (s, e) => FindNext();
            btnFilterUntranslated.IsCheckedChanged += (s, e) =>
                ViewModel.FilterUntranslated = btnFilterUntranslated.IsChecked == true;
            btnNextUntranslated.Click += (s, e) => JumpToNextUntranslated();
            btnTranslate.Click += async (s, e) => await TranslateEntry();

            // Double-click a row → focus translation TextBox
            dgv.DoubleTapped += (s, e) =>
                this.FindControl<TextBox>("TxtTranslation")?.Focus();
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
            var grid = this.FindControl<Grid>("MainContentGrid");
            if (grid != null)
            {
                var h = grid.RowDefinitions[2].Height;
                if (h.IsAbsolute)
                    _settings.SplitterPosition = h.Value;
            }
            _settings.Save();

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
                await ViewModel.LoadFile(filename);
            }
        }

        private void SaveFile()
        {
            if (string.IsNullOrEmpty(ViewModel.CurrentFilename))
            {
                _ = SaveFileAs();
                return;
            }

            // Warn about newlines in translations (break TRS format)
            var problematic = ViewModel.Entries
                .Where(e => e.Value.Contains('\n'))
                .ToList();
            if (problematic.Count > 0)
            {
                ViewModel.FileStatusText =
                    $"⚠ Warning: {problematic.Count} translation(s) contain newlines — stripped on save.";
                foreach (var e in problematic)
                    e.Value = e.Value.Replace("\r\n", " ").Replace("\n", " ");
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

        private static readonly FilePickerFileType[] GameFileTypes =
        {
            new FilePickerFileType("AGS Game Files") { Patterns = new[] { "*.ags", "*.exe", "*.bin", "*.dat", "game28.dta", "ac2game.dta" } },
            new FilePickerFileType("All Files") { Patterns = new[] { "*" } },
        };

        private async System.Threading.Tasks.Task<string?> PickGameFile(string title)
        {
            var files = await StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions
            {
                Title = title,
                AllowMultiple = false,
                FileTypeFilter = GameFileTypes,
            });
            if (files.Count == 0)
                return null;
            _lastGameFile = files[0].Path.LocalPath;
            return _lastGameFile;
        }

        private async System.Threading.Tasks.Task<string?> PickTraOutput(string? suggestedName = null)
        {
            var traFile = await StorageProvider.SaveFilePickerAsync(new FilePickerSaveOptions
            {
                Title = "Save TRA File",
                DefaultExtension = "tra",
                SuggestedFileName = suggestedName,
                FileTypeChoices = new[] { new FilePickerFileType("TRA Translation File") { Patterns = new[] { "*.tra" } } }
            });
            return traFile?.Path.LocalPath;
        }

        /// <summary>Reads game info, reporting a missing or unreadable game in the status bar.</summary>
        private GameInfo? ReadGameInfo(string gameFile)
        {
            try
            {
                var info = GameInfo.GetGameInfo(gameFile);
                if (info == null)
                    ViewModel.FileStatusText = $"No AGS game data found in {Path.GetFileName(gameFile)}.";
                return info;
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                ViewModel.FileStatusText = $"Could not read game file: {ex.Message}";
                return null;
            }
        }

        /// <summary>Uses the game's id, name and codepage, keeping the translation's own settings.</summary>
        private static void ApplyGame(TranslationOptions options, GameInfo info)
        {
            options.GameUid = info.UniqueId;
            options.GameName = info.GameTitle;
            if (info.IsUnicode)
                options.Encoding = "UTF-8";
            else if (info.TextCodepage > 0)
                options.AnsiCodepage = info.TextCodepage;
        }

        private async System.Threading.Tasks.Task WriteTra(TranslationDocument doc, GameInfo info, string traPath)
        {
            ApplyGame(doc.Options, info);
            try
            {
                var warnings = await System.Threading.Tasks.Task.Run(() =>
                {
                    var result = doc.ReadWarnings.Concat(TraFile.Write(traPath, doc)).ToList();
                    if (info.DataPath.Length > 0)
                        result.AddRange(FontCoverage.CheckGame(doc, info.DataPath));
                    return result;
                });
                int written = doc.Entries.Count(e => !string.IsNullOrWhiteSpace(e.Value));
                ViewModel.FileStatusText = $"TRA created: {Path.GetFileName(traPath)} ({written} translated entries for \"{info.GameTitle}\")";
                if (warnings.Count > 0)
                    await ShowInfoDialog("TRA created with warnings:\n\n" + string.Join("\n\n", warnings), "Warnings");
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                ViewModel.FileStatusText = $"TRA export failed: {ex.Message}";
            }
        }

        private async System.Threading.Tasks.Task ExportAsTRA()
        {
            string? gameFile = await PickGameFile("Select AGS Game File (for Game ID)");
            if (gameFile == null) return;
            var info = ReadGameInfo(gameFile);
            if (info == null) return;

            string? traPath = await PickTraOutput(Path.GetFileNameWithoutExtension(ViewModel.CurrentFilename));
            if (traPath == null) return;

            await WriteTra(ViewModel.GetDocumentForExport(), info, traPath);
        }

        private async System.Threading.Tasks.Task ExtractScript()
        {
            string? gameFile = await PickGameFile("Select AGS Game File");
            if (gameFile == null) return;

            // Game folders are often read-only, so ask where to save
            var output = await StorageProvider.SaveFilePickerAsync(new FilePickerSaveOptions
            {
                Title = "Save Extracted Texts As",
                DefaultExtension = "trs",
                SuggestedFileName = Path.GetFileNameWithoutExtension(gameFile) + ".trs",
                FileTypeChoices = new[] { new FilePickerFileType("TRS Translation File") { Patterns = new[] { "*.trs" } } }
            });
            if (output == null) return;
            string trsPath = output.Path.LocalPath;

            ViewModel.IsLoading = true;
            ViewModel.FileStatusText = $"Extracting texts from {Path.GetFileName(gameFile)}...";
            try
            {
                var progress = new Progress<string>(message => ViewModel.FileStatusText = message);
                var (result, writeWarnings) = await System.Threading.Tasks.Task.Run(() =>
                {
                    var r = TextExtractor.Extract(gameFile, null, progress);
                    return (r, TrsFile.Write(trsPath, r.ToTranslationDocument()));
                });

                await ViewModel.LoadFile(trsPath);

                var summary = new System.Text.StringBuilder();
                summary.AppendLine($"{result.Texts.Count} texts extracted to {Path.GetFileName(trsPath)}.");
                if (result.Game != null)
                    summary.AppendLine($"Game: {result.Game.Title} (AGS {result.Game.EngineVersion}), {result.RoomCount} rooms.");
                foreach (var group in result.Texts.GroupBy(t => t.Source))
                    summary.AppendLine($"  {group.Key}: {group.Count()}");
                if (result.VoicedCount > 0)
                    summary.AppendLine($"{result.VoicedCount} texts got a voice prefix (&N).");
                var warnings = result.Warnings.Concat(writeWarnings).ToList();
                if (warnings.Count > 0)
                {
                    summary.AppendLine().AppendLine("Warnings:");
                    foreach (string w in warnings.Take(10))
                        summary.AppendLine("• " + w);
                    if (warnings.Count > 10)
                        summary.AppendLine($"... and {warnings.Count - 10} more.");
                }
                await ShowInfoDialog(summary.ToString(), "Extraction");
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                ViewModel.FileStatusText = $"Extraction failed: {ex.Message}";
            }
            finally
            {
                ViewModel.IsLoading = false;
            }
        }

        private async System.Threading.Tasks.Task ShowGameInfo()
        {
            string? gameFile = await PickGameFile("Select AGS Game File");
            if (gameFile == null) return;
            var info = ReadGameInfo(gameFile);
            if (info == null) return;

            string encoding = info.IsUnicode ? "UTF-8" : info.TextCodepage > 0 ? $"8-bit, codepage {info.TextCodepage}" : "8-bit (default codepage)";
            string dataFile = string.Equals(Path.GetFullPath(info.DataPath), Path.GetFullPath(gameFile), StringComparison.OrdinalIgnoreCase)
                ? "" : $"\nGame data: {Path.GetFileName(info.DataPath)}";
            await ShowInfoDialog(
                $"Title: {info.GameTitle}\nGame ID: {info.UniqueId} (legacy hex: {info.GameUID})\n" +
                $"AGS version: {info.Version}\nText encoding: {encoding}{dataFile}");
        }

        private async System.Threading.Tasks.Task CreateTRA()
        {
            string? gameFile = await PickGameFile("Select AGS Game File");
            if (gameFile == null) return;
            var info = ReadGameInfo(gameFile);
            if (info == null) return;

            var trsFiles = await StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions
            {
                Title = "Select TRS File",
                FileTypeFilter = new[] { new FilePickerFileType("TRS") { Patterns = new[] { "*.trs" } } }
            });
            if (trsFiles.Count == 0) return;
            string trsPath = trsFiles[0].Path.LocalPath;

            string? traPath = await PickTraOutput(Path.GetFileNameWithoutExtension(trsPath));
            if (traPath == null) return;

            TranslationDocument doc;
            try
            {
                int codepage = info.TextCodepage > 0 && !info.IsUnicode ? info.TextCodepage : AgsTextEncoding.DefaultAnsiCodepage;
                doc = TrsFile.Read(trsPath, codepage);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                ViewModel.FileStatusText = $"Could not read TRS file: {ex.Message}";
                return;
            }
            await WriteTra(doc, info, traPath);
        }

        /// <summary>
        /// Extracts the game's current texts and carries the open translation over: new texts are added,
        /// texts that only differ in surrounding spaces or the voice prefix keep their translation.
        /// </summary>
        private async System.Threading.Tasks.Task MergeWithGame()
        {
            if (ViewModel.Entries.Count == 0)
            {
                ViewModel.FileStatusText = "Open the translation to update first.";
                return;
            }
            string? gameFile = await PickGameFile("Select AGS Game File to Update From");
            if (gameFile == null) return;

            ViewModel.IsLoading = true;
            ViewModel.FileStatusText = $"Extracting texts from {Path.GetFileName(gameFile)}...";
            try
            {
                var existing = ViewModel.GetDocument();
                var progress = new Progress<string>(message => ViewModel.FileStatusText = message);
                var (extraction, merge) = await System.Threading.Tasks.Task.Run(() =>
                {
                    var r = TextExtractor.Extract(gameFile, null, progress);
                    return (r, TranslationMerger.Merge(r.ToTranslationDocument(), existing));
                });

                ViewModel.ReplaceDocument(merge.Document);
                ViewModel.FileStatusText = $"Updated from {Path.GetFileName(gameFile)} - save to keep the changes.";

                var summary = new System.Text.StringBuilder();
                summary.AppendLine($"{extraction.Texts.Count} texts in the game:");
                summary.AppendLine($"  {merge.ExactCount} translations carried over unchanged");
                summary.AppendLine($"  {merge.SimilarCount} carried over from texts differing only in spaces or voice prefix");
                summary.AppendLine($"  {merge.NewCount} without translation");
                if (merge.Obsolete.Count > 0)
                {
                    summary.AppendLine();
                    summary.AppendLine($"{merge.Obsolete.Count} translated texts no longer exist in the game. They were kept at the end of the list, e.g.:");
                    foreach (var entry in merge.Obsolete.Take(5))
                        summary.AppendLine("• " + entry.Key);
                }
                if (extraction.Warnings.Count > 0)
                {
                    summary.AppendLine().AppendLine("Warnings:");
                    foreach (string w in extraction.Warnings.Take(10))
                        summary.AppendLine("• " + w);
                }
                await ShowInfoDialog(summary.ToString(), "Update from Game");
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                ViewModel.FileStatusText = $"Update failed: {ex.Message}";
            }
            finally
            {
                ViewModel.IsLoading = false;
            }
        }

        private async System.Threading.Tasks.Task ShowSettings()
        {
            var dialog = new SettingsWindow(_settings);
            await dialog.ShowDialog(this);
            // Re-apply font and ViewModel-bound settings after dialog closes
            ApplyFontFamily();
            ViewModel.EditorFontSize = _settings.FontSize;
            ViewModel.SaveEncoding = _settings.Encoding;
        }

        private void ShowAbout()
        {
            var about = new AboutDialog();
            about.ShowDialog(this);
        }

        private async System.Threading.Tasks.Task TranslateEntry()
        {
            var translator = new GoogleTranslator();

            if (ViewModel.SelectedEntry != null)
            {
                ViewModel.FileStatusText = "Translating...";
                var entry = ViewModel.SelectedEntry;
                string sourceText = string.IsNullOrEmpty(entry.Value) ? entry.Key : entry.Value;
                try
                {
                    string translated = await System.Threading.Tasks.Task.Run(
                        () => translator.Translate(sourceText, "auto", "de"));
                    entry.Value = translated;
                    ViewModel.DocumentChanged = true;
                    ViewModel.FileStatusText = "Translated 1 entry";
                }
                catch (Exception ex)
                {
                    ViewModel.FileStatusText = $"Translation error: {ex.Message}";
                }
            }
            else
            {
                var emptyEntries = ViewModel.Entries.Where(e => string.IsNullOrEmpty(e.Value)).ToList();
                if (emptyEntries.Count == 0)
                {
                    ViewModel.FileStatusText = "No empty entries to translate.";
                    return;
                }
                ViewModel.FileStatusText = "Translating...";
                int count = 0;
                try
                {
                    foreach (var entry in emptyEntries)
                    {
                        string translated = await System.Threading.Tasks.Task.Run(
                            () => translator.Translate(entry.Key, "auto", "de"));
                        entry.Value = translated;
                        count++;
                    }
                    ViewModel.DocumentChanged = true;
                    ViewModel.FileStatusText = $"Translated {count} entries";
                }
                catch (Exception ex)
                {
                    ViewModel.FileStatusText = $"Translation error after {count} entries: {ex.Message}";
                }
            }
        }

        private void JumpToNextUntranslated()
        {
            var next = ViewModel.GetNextUntranslated();
            if (next != null)
            {
                ViewModel.SelectedEntry = next;
                var dgv = this.FindControl<DataGrid>("DgvTranslation")!;
                dgv.ScrollIntoView(next, null);
                ViewModel.FileStatusText = $"Jumped to next untranslated entry.";
            }
            else
            {
                ViewModel.FileStatusText = "No untranslated entries remaining.";
            }
        }


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

        private async System.Threading.Tasks.Task ShowInfoDialog(string message, string title = "Game Info")
        {
            var dialog = new Window
            {
                Title = title,
                Width = 460,
                SizeToContent = SizeToContent.Height,
                MaxHeight = 600,
                WindowStartupLocation = WindowStartupLocation.CenterOwner,
                CanResize = false
            };
            var panel = new StackPanel { Margin = new Avalonia.Thickness(16), Spacing = 12 };
            panel.Children.Add(new ScrollViewer
            {
                MaxHeight = 480,
                Content = new SelectableTextBlock { Text = message, TextWrapping = Avalonia.Media.TextWrapping.Wrap }
            });
            var ok = new Button { Content = "OK", HorizontalAlignment = Avalonia.Layout.HorizontalAlignment.Right };
            ok.Click += (s, e) => dialog.Close();
            panel.Children.Add(ok);
            dialog.Content = panel;
            await dialog.ShowDialog(this);
        }
    }
}
