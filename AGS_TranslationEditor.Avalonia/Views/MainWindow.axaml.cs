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
using AGSFontEditor.Localization;
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
                    vm.ShowContext = _settings.ShowContext;
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
            var menuCheck = this.FindControl<MenuItem>("MenuCheck")!;
            var menuInstall = this.FindControl<MenuItem>("MenuInstall")!;
            var menuUninstall = this.FindControl<MenuItem>("MenuUninstall")!;
            var menuPack = this.FindControl<MenuItem>("MenuPack")!;
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
            menuCheck.Click += async (s, e) => await CheckTranslation();
            menuInstall.Click += async (s, e) => await InstallIntoGame();
            menuUninstall.Click += async (s, e) => await UninstallFromGame();
            menuPack.Click += async (s, e) => await CreatePlayerPackage();
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

            // Clicking a text of the running game selects its entry for editing
            var lstLive = this.FindControl<ListBox>("LstLive")!;
            lstLive.SelectionChanged += (s, e) =>
            {
                if (lstLive.SelectedItem is LiveItem { Entry: { } entry })
                {
                    ViewModel.SelectedEntry = entry;
                    dgv.ScrollIntoView(entry, null);
                }
            };
            var lstCheck = this.FindControl<ListBox>("LstCheck")!;
            lstCheck.SelectionChanged += (s, e) =>
            {
                if (lstCheck.SelectedItem is CheckItem { Entry: { } entry })
                {
                    ViewModel.SelectedEntry = entry;
                    dgv.ScrollIntoView(entry, null);
                }
            };
            Closed += (s, e) => ViewModel.Live.Dispose();
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
                var result = await ShowYesNoDialog(Loc.F("Save changes to {0}?", Path.GetFileName(ViewModel.CurrentFilename)));
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
                Title = Loc.T("Open Translation File"),
                AllowMultiple = false,
                FileTypeFilter = new[]
                {
                    new FilePickerFileType(Loc.T("AGS Translation Files")) { Patterns = new[] { "*.tra", "*.trs" } },
                    new FilePickerFileType(Loc.T("CSV/TSV Files")) { Patterns = new[] { "*.csv", "*.tsv" } },
                    new FilePickerFileType(Loc.T("PO Files")) { Patterns = new[] { "*.po" } },
                    new FilePickerFileType(Loc.T("All Files")) { Patterns = new[] { "*" } },
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
                    Loc.F("⚠ Warning: {0} translation(s) contain newlines — stripped on save.", problematic.Count);
                foreach (var e in problematic)
                    e.Value = e.Value.Replace("\r\n", " ").Replace("\n", " ");
            }

            ViewModel.SaveFile(ViewModel.CurrentFilename);
        }

        private async System.Threading.Tasks.Task SaveFileAs()
        {
            var file = await StorageProvider.SaveFilePickerAsync(new FilePickerSaveOptions
            {
                Title = Loc.T("Save Translation File"),
                DefaultExtension = "trs",
                FileTypeChoices = new[]
                {
                    new FilePickerFileType(Loc.T("TRS Translation File")) { Patterns = new[] { "*.trs" } },
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
                Title = Loc.T("Export as CSV"),
                DefaultExtension = "csv",
                FileTypeChoices = new[] { new FilePickerFileType(Loc.T("CSV File")) { Patterns = new[] { "*.csv" } } }
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
                Title = Loc.T("Export as PO"),
                DefaultExtension = "po",
                FileTypeChoices = new[] { new FilePickerFileType(Loc.T("PO File")) { Patterns = new[] { "*.po" } } }
            });
            if (file != null)
            {
                var data = ViewModel.Entries.ToDictionary(e => e.Key, e => e.Value);
                POFormat.CreatePO(file.Path.LocalPath, data);
            }
        }

        private static readonly FilePickerFileType[] GameFileTypes =
        {
            new FilePickerFileType(Loc.T("AGS Game Files")) { Patterns = new[] { "*.ags", "*.exe", "*.bin", "*.dat", "game28.dta", "ac2game.dta" } },
            new FilePickerFileType(Loc.T("All Files")) { Patterns = new[] { "*" } },
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
                Title = Loc.T("Save TRA File"),
                DefaultExtension = "tra",
                SuggestedFileName = suggestedName,
                FileTypeChoices = new[] { new FilePickerFileType(Loc.T("TRA Translation File")) { Patterns = new[] { "*.tra" } } }
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
                    ViewModel.FileStatusText = Loc.F("No AGS game data found in {0}.", Path.GetFileName(gameFile));
                return info;
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                ViewModel.FileStatusText = Loc.F("Could not read game file: {0}", ex.Message);
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
                    var result = doc.ReadWarnings.Concat(TraFile.Write(traPath, doc))
                        .Concat(EncodingAdvice.Check(doc, info.Version, info.IsUnicode)).ToList();
                    if (info.DataPath.Length > 0)
                        result.AddRange(FontCoverage.CheckGame(doc, info.DataPath));
                    return result;
                });
                int written = doc.Entries.Count(e => !string.IsNullOrWhiteSpace(e.Value));
                ViewModel.FileStatusText = Loc.F("TRA created: {0} ({1} translated entries for \"{2}\")", Path.GetFileName(traPath), written, info.GameTitle);
                if (warnings.Count > 0)
                    await ShowInfoDialog(Loc.T("TRA created with warnings:\n\n") + string.Join("\n\n", warnings), Loc.T("Warnings"));
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                ViewModel.FileStatusText = Loc.F("TRA export failed: {0}", ex.Message);
            }
        }

        private async System.Threading.Tasks.Task ExportAsTRA()
        {
            string? gameFile = await PickGameFile(Loc.T("Select AGS Game File (for Game ID)"));
            if (gameFile == null) return;
            var info = ReadGameInfo(gameFile);
            if (info == null) return;

            string? traPath = await PickTraOutput(Path.GetFileNameWithoutExtension(ViewModel.CurrentFilename));
            if (traPath == null) return;

            await WriteTra(ViewModel.GetDocumentForExport(), info, traPath);
        }

        private async System.Threading.Tasks.Task ExtractScript()
        {
            string? gameFile = await PickGameFile(Loc.T("Select AGS Game File"));
            if (gameFile == null) return;

            // Game folders are often read-only, so ask where to save
            var output = await StorageProvider.SaveFilePickerAsync(new FilePickerSaveOptions
            {
                Title = Loc.T("Save Extracted Texts As"),
                DefaultExtension = "trs",
                SuggestedFileName = Path.GetFileNameWithoutExtension(gameFile) + ".trs",
                FileTypeChoices = new[] { new FilePickerFileType(Loc.T("TRS Translation File")) { Patterns = new[] { "*.trs" } } }
            });
            if (output == null) return;
            string trsPath = output.Path.LocalPath;

            ViewModel.IsLoading = true;
            ViewModel.FileStatusText = Loc.F("Extracting texts from {0}...", Path.GetFileName(gameFile));
            try
            {
                var progress = new Progress<string>(message => ViewModel.FileStatusText = message);
                bool writeContext = _settings.ShowContext;
                var (result, writeWarnings) = await System.Threading.Tasks.Task.Run(() =>
                {
                    var r = TextExtractor.Extract(gameFile, null, progress);
                    var written = TrsFile.Write(trsPath, r.ToTranslationDocument());
                    if (writeContext)
                        TextContext.Write(TextContext.PathFor(trsPath), TextContext.FromExtraction(r));
                    return (r, written);
                });

                await ViewModel.LoadFile(trsPath);

                var summary = new System.Text.StringBuilder();
                summary.AppendLine(Loc.F("{0} texts extracted to {1}.", result.Texts.Count, Path.GetFileName(trsPath)));
                if (result.Game != null)
                    summary.AppendLine(Loc.F("Game: {0} (AGS {1}), {2} rooms.", result.Game.Title, result.Game.EngineVersion, result.RoomCount));
                foreach (var group in result.Texts.GroupBy(t => t.Source))
                    summary.AppendLine($"  {group.Key}: {group.Count()}");
                if (result.VoicedCount > 0)
                    summary.AppendLine(Loc.F("{0} texts got a voice prefix (&N).", result.VoicedCount));
                if (writeContext)
                    summary.AppendLine(Loc.F("Context saved: {0} texts with a known speaker.", result.Texts.Count(t => t.Speaker != null)));
                var warnings = result.Warnings.Concat(writeWarnings).ToList();
                if (warnings.Count > 0)
                {
                    summary.AppendLine().AppendLine(Loc.T("Warnings:"));
                    foreach (string w in warnings.Take(10))
                        summary.AppendLine("• " + w);
                    if (warnings.Count > 10)
                        summary.AppendLine(Loc.F("... and {0} more.", warnings.Count - 10));
                }
                await ShowInfoDialog(summary.ToString(), Loc.T("Extraction"));
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                ViewModel.FileStatusText = Loc.F("Extraction failed: {0}", ex.Message);
            }
            finally
            {
                ViewModel.IsLoading = false;
            }
        }

        private async System.Threading.Tasks.Task ShowGameInfo()
        {
            string? gameFile = await PickGameFile(Loc.T("Select AGS Game File"));
            if (gameFile == null) return;
            var info = ReadGameInfo(gameFile);
            if (info == null) return;

            string encoding = info.IsUnicode ? "UTF-8" : info.TextCodepage > 0 ? Loc.F("8-bit, codepage {0}", info.TextCodepage) : Loc.T("8-bit (default codepage)");
            string dataFile = string.Equals(Path.GetFullPath(info.DataPath), Path.GetFullPath(gameFile), StringComparison.OrdinalIgnoreCase)
                ? "" : "\n" + Loc.F("Game data: {0}", Path.GetFileName(info.DataPath));
            await ShowInfoDialog(
                Loc.F("Title: {0}\nGame ID: {1} (legacy hex: {2})\nAGS version: {3}\nText encoding: {4}", info.GameTitle, info.UniqueId, info.GameUID, info.Version, encoding)
                + dataFile);
        }

        private async System.Threading.Tasks.Task CreateTRA()
        {
            string? gameFile = await PickGameFile(Loc.T("Select AGS Game File"));
            if (gameFile == null) return;
            var info = ReadGameInfo(gameFile);
            if (info == null) return;

            var trsFiles = await StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions
            {
                Title = Loc.T("Select TRS File"),
                FileTypeFilter = new[] { new FilePickerFileType(Loc.T("TRS")) { Patterns = new[] { "*.trs" } } }
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
                ViewModel.FileStatusText = Loc.F("Could not read TRS file: {0}", ex.Message);
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
                ViewModel.FileStatusText = Loc.T("Open the translation to update first.");
                return;
            }
            string? gameFile = await PickGameFile(Loc.T("Select AGS Game File to Update From"));
            if (gameFile == null) return;

            ViewModel.IsLoading = true;
            ViewModel.FileStatusText = Loc.F("Extracting texts from {0}...", Path.GetFileName(gameFile));
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
                ViewModel.UpdateContext(extraction, ViewModel.CurrentFilename);
                ViewModel.FileStatusText = Loc.F("Updated from {0} - save to keep the changes.", Path.GetFileName(gameFile));

                var summary = new System.Text.StringBuilder();
                summary.AppendLine(Loc.F("{0} texts in the game:", extraction.Texts.Count));
                summary.AppendLine(Loc.F("  {0} translations carried over unchanged", merge.ExactCount));
                summary.AppendLine(Loc.F("  {0} carried over from texts differing only in spaces or voice prefix", merge.SimilarCount));
                summary.AppendLine(Loc.F("  {0} without translation", merge.NewCount));
                if (merge.Obsolete.Count > 0)
                {
                    summary.AppendLine();
                    summary.AppendLine(Loc.F("{0} translated texts no longer exist in the game. They were kept at the end of the list, e.g.:", merge.Obsolete.Count));
                    foreach (var entry in merge.Obsolete.Take(5))
                        summary.AppendLine("• " + entry.Key);
                }
                if (extraction.Warnings.Count > 0)
                {
                    summary.AppendLine().AppendLine(Loc.T("Warnings:"));
                    foreach (string w in extraction.Warnings.Take(10))
                        summary.AppendLine("• " + w);
                }
                await ShowInfoDialog(summary.ToString(), Loc.T("Update from Game"));
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                ViewModel.FileStatusText = Loc.F("Update failed: {0}", ex.Message);
            }
            finally
            {
                ViewModel.IsLoading = false;
            }
        }

        /// <summary>
        /// Checks the open translation against the game: placeholders, voice prefixes, consistency and
        /// GUI text sizes. A glossary named "glossary.txt" or "Glossar.txt" next to the translation is used too.
        /// </summary>
        private async System.Threading.Tasks.Task CheckTranslation()
        {
            if (ViewModel.Entries.Count == 0)
            {
                ViewModel.FileStatusText = Loc.T("Open a translation to check first.");
                return;
            }
            string? gameFile = await PickGameFile(Loc.T("Select the AGS Game File of this Translation"));
            if (gameFile == null) return;

            string? glossaryPath = FindGlossary();

            ViewModel.IsLoading = true;
            ViewModel.FileStatusText = Loc.F("Checking against {0}...", Path.GetFileName(gameFile));
            try
            {
                var doc = ViewModel.GetDocument();
                var progress = new Progress<string>(message => ViewModel.FileStatusText = message);
                var issues = await System.Threading.Tasks.Task.Run(() =>
                {
                    var extraction = TextExtractor.Extract(gameFile, null, progress);
                    var fonts = GameFonts.Load(gameFile);
                    var glossary = glossaryPath != null ? Glossary.Read(glossaryPath) : null;
                    return TranslationCheck.Run(doc, extraction, fonts, glossary);
                });
                string summary = Loc.F("{0} errors, {1} warnings, {2} notes.", issues.Count(i => i.Severity == CheckSeverity.Error),
                        issues.Count(i => i.Severity == CheckSeverity.Warning), issues.Count(i => i.Severity == CheckSeverity.Info)) + " "
                    + (glossaryPath != null ? Loc.F("Glossary: {0}", Path.GetFileName(glossaryPath)) : Loc.T("No glossary.txt next to the translation."));
                ViewModel.ShowCheckResults(issues, summary);
                ViewModel.FileStatusText = Loc.T("Check finished - click a finding to jump to its entry.");
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                ViewModel.FileStatusText = Loc.F("Check failed: {0}", ex.Message);
            }
            finally
            {
                ViewModel.IsLoading = false;
            }
        }

        /// <summary>Language name the game will use: the open file's name, e.g. "Deutsch" for Deutsch.trs.</summary>
        private string LanguageName()
        {
            string name = Path.GetFileNameWithoutExtension(ViewModel.CurrentFilename);
            name = new string(name.Where(c => char.IsLetterOrDigit(c) || c is '-' or '_').ToArray());
            return name.Length > 0 ? name : "Translation";
        }

        /// <summary>Compiles the open translation for the game into a temporary TRA; null on failure.</summary>
        private async System.Threading.Tasks.Task<string?> CompileTempTra(GameInfo info)
        {
            var doc = ViewModel.GetDocumentForExport();
            ApplyGame(doc.Options, info);
            string folder = Path.Combine(Path.GetTempPath(), "agstranslate-" + Guid.NewGuid().ToString("N"));
            string tra = Path.Combine(folder, LanguageName() + ".tra");
            try
            {
                Directory.CreateDirectory(folder);
                await System.Threading.Tasks.Task.Run(() => TraFile.Write(tra, doc));
                return tra;
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                ViewModel.FileStatusText = Loc.F("TRA could not be created: {0}", ex.Message);
                return null;
            }
        }

        private static void DeleteTempTra(string tra)
        {
            try
            {
                File.Delete(tra);
                Directory.Delete(Path.GetDirectoryName(tra)!);
            }
            catch (IOException)
            {
                // Only a temporary file
            }
        }

        /// <summary>Asks whether to add fonts and returns the agsfntN files of the chosen folder (empty for none).</summary>
        private async System.Threading.Tasks.Task<List<string>?> PickFonts()
        {
            if (!await ShowYesNoDialog(Loc.T("Also include changed fonts (agsfntN.ttf / agsfntN.wfn) from a folder?")))
                return new List<string>();
            var folders = await StorageProvider.OpenFolderPickerAsync(new FolderPickerOpenOptions { Title = Loc.T("Folder with the font files") });
            if (folders.Count == 0)
                return null;
            var fonts = Directory.EnumerateFiles(folders[0].Path.LocalPath).Where(f => GameFonts.FontNumber(f) >= 0).ToList();
            if (fonts.Count == 0)
                ViewModel.FileStatusText = Loc.T("No agsfntN.ttf/.wfn files in that folder; continuing without fonts.");
            return fonts;
        }

        /// <summary>
        /// Compiles the open translation and puts it into the game folder (with fonts if wanted), backing up
        /// replaced files, and selects it in acsetup.cfg.
        /// </summary>
        private async System.Threading.Tasks.Task InstallIntoGame()
        {
            if (ViewModel.Entries.Count == 0)
            {
                ViewModel.FileStatusText = Loc.T("Open the translation to install first.");
                return;
            }
            string? gameFile = await PickGameFile(Loc.T("Select the AGS Game to Install Into"));
            if (gameFile == null) return;
            var info = ReadGameInfo(gameFile);
            if (info == null) return;
            string gameFolder = Path.GetDirectoryName(Path.GetFullPath(info.DataPath)) ?? "";
            if (!await ShowYesNoDialog(Loc.F("Install \"{0}\" into\n{1}?\n\nReplaced files are backed up and can be restored with Uninstall.", LanguageName(), gameFolder)))
                return;
            var fonts = await PickFonts();
            if (fonts == null) return;

            string? tra = await CompileTempTra(info);
            if (tra == null) return;
            try
            {
                var result = await System.Threading.Tasks.Task.Run(() => TranslationInstaller.Install(gameFile, tra, fonts));
                var text = new System.Text.StringBuilder();
                text.AppendLine(Loc.F("\"{0}\" was installed into {1}.", result.Language, result.GameFolder));
                text.AppendLine();
                foreach (string f in result.Added) text.AppendLine(Loc.F("added: {0}", f));
                foreach (string f in result.Replaced) text.AppendLine(Loc.F("replaced: {0} (original backed up)", f));
                foreach (string c in result.ConfigFiles) text.AppendLine(Loc.F("language set in: {0}", c));
                text.AppendLine().AppendLine(Loc.F("Backup: {0}", result.BackupFolder));
                if (result.Warnings.Count > 0)
                    text.AppendLine().AppendLine(Loc.T("Warnings:")).AppendLine(string.Join("\n", result.Warnings.Select(w => "• " + w)));
                ViewModel.FileStatusText = Loc.F("Installed into {0}.", Path.GetFileName(result.GameFolder));
                await ShowInfoDialog(text.ToString(), Loc.T("Install into Game"));
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                ViewModel.FileStatusText = Loc.F("Install failed: {0}", ex.Message);
            }
            finally
            {
                DeleteTempTra(tra);
            }
        }

        private async System.Threading.Tasks.Task UninstallFromGame()
        {
            string? gameFile = await PickGameFile(Loc.T("Select the AGS Game to Uninstall From"));
            if (gameFile == null) return;
            if (!await ShowYesNoDialog(Loc.T("Undo the last translation install in this game? The backed-up files are put back; "
                + "installed files are moved into the backup folder, not deleted.")))
                return;
            try
            {
                var result = await System.Threading.Tasks.Task.Run(() => TranslationInstaller.Uninstall(gameFile));
                var text = new System.Text.StringBuilder();
                foreach (string f in result.Restored) text.AppendLine(Loc.F("restored: {0}", f));
                foreach (string f in result.Removed) text.AppendLine(Loc.F("moved into the backup folder: {0}", f));
                foreach (string w in result.Warnings) text.AppendLine(Loc.F("Warning: {0}", w));
                ViewModel.FileStatusText = Loc.T("Uninstalled.");
                await ShowInfoDialog(text.ToString(), Loc.T("Uninstall from Game"));
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                ViewModel.FileStatusText = Loc.F("Uninstall failed: {0}", ex.Message);
            }
        }

        /// <summary>Creates a zip for players with the TRA, fonts, install scripts and a read-me.</summary>
        private async System.Threading.Tasks.Task CreatePlayerPackage()
        {
            if (ViewModel.Entries.Count == 0)
            {
                ViewModel.FileStatusText = Loc.T("Open the translation to package first.");
                return;
            }
            string? gameFile = await PickGameFile(Loc.T("Select the AGS Game of this Translation"));
            if (gameFile == null) return;
            var info = ReadGameInfo(gameFile);
            if (info == null) return;
            var fonts = await PickFonts();
            if (fonts == null) return;
            var zipFile = await StorageProvider.SaveFilePickerAsync(new FilePickerSaveOptions
            {
                Title = Loc.T("Save Player Package"),
                DefaultExtension = "zip",
                SuggestedFileName = $"{info.GameTitle} - {LanguageName()}.zip",
                FileTypeChoices = new[] { new FilePickerFileType(Loc.T("Zip archive")) { Patterns = new[] { "*.zip" } } }
            });
            if (zipFile == null) return;
            string zipPath = zipFile.Path.LocalPath;

            string? tra = await CompileTempTra(info);
            if (tra == null) return;
            try
            {
                var warnings = await System.Threading.Tasks.Task.Run(() => PlayerPackage.Create(gameFile, tra, fonts, zipPath));
                ViewModel.FileStatusText = Loc.F("Player package created: {0}", Path.GetFileName(zipPath));
                await ShowInfoDialog(Loc.F("Created {0}.\n\nPlayers unpack it into the game folder and run install.bat (Windows) "
                    + "or install.sh (Linux/macOS); see LIESMICH.txt in the package.", zipPath)
                    + (warnings.Count > 0 ? "\n\n" + Loc.T("Warnings:") + "\n" + string.Join("\n", warnings.Select(w => "• " + w)) : ""), Loc.T("Player Package"));
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                ViewModel.FileStatusText = Loc.F("Package failed: {0}", ex.Message);
            }
            finally
            {
                DeleteTempTra(tra);
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
            ViewModel.ShowContext = _settings.ShowContext;
        }

        /// <summary>The optional "Context" column, the last one of the list.</summary>
        private DataGridColumn ContextColumn => this.FindControl<DataGrid>("DgvTranslation")!.Columns[^1];

        private void ShowAbout()
        {
            var about = new AboutDialog();
            about.ShowDialog(this);
        }

        /// <summary>
        /// Machine-translates the selected entry, or all empty entries if none is selected, with the service
        /// chosen in the settings. A glossary next to the translation is passed on to Claude.
        /// </summary>
        private async System.Threading.Tasks.Task TranslateEntry()
        {
            IMachineTranslator translator;
            try
            {
                translator = MachineTranslators.Create(_settings.MtProvider, _settings.DeepLApiKey, _settings.ClaudeApiKey, _settings.ClaudeModel);
            }
            catch (MachineTranslationException ex)
            {
                ViewModel.FileStatusText = ex.Message;
                return;
            }

            var entries = ViewModel.SelectedEntry != null
                ? new List<TranslationEntry> { ViewModel.SelectedEntry }
                : ViewModel.Entries.Where(e => string.IsNullOrEmpty(e.Value)).ToList();
            if (entries.Count == 0)
            {
                ViewModel.FileStatusText = Loc.T("No empty entries to translate.");
                return;
            }
            string direction = $"{(_settings.MtSourceLanguage.Length > 0 ? _settings.MtSourceLanguage : "auto")} → {_settings.MtTargetLanguage}";
            if (entries.Count > 1 && !await ShowYesNoDialog(Loc.F("Translate {0} empty entries with {1} ({2})?", entries.Count, translator.Name, direction)
                + (translator is GoogleTranslator ? "" : "\n\n" + Loc.T("The service bills for the translated text."))))
                return;

            string? glossaryPath = FindGlossary();
            var options = new MachineTranslationOptions
            {
                SourceLanguage = _settings.MtSourceLanguage,
                TargetLanguage = _settings.MtTargetLanguage,
                Formality = SettingsWindow.ParseFormality(_settings.MtFormality),
                GameTitle = ViewModel.GetDocument().Options.GameName,
                Glossary = glossaryPath != null ? Glossary.Read(glossaryPath) : null,
            };

            int count = 0;
            ViewModel.FileStatusText = Loc.F("Translating with {0}...", translator.Name);
            try
            {
                await MachineTranslation.TranslateAsync(translator, entries.Select(e => e.Key).ToList(), options, (index, text) =>
                    Avalonia.Threading.Dispatcher.UIThread.Post(() =>
                    {
                        entries[index].Value = text;
                        ViewModel.DocumentChanged = true;
                        count++;
                        ViewModel.FileStatusText = Loc.F("Translating with {0}... {1}/{2}", translator.Name, count, entries.Count);
                    }));
                await Avalonia.Threading.Dispatcher.UIThread.InvokeAsync(() =>
                    ViewModel.FileStatusText = Loc.F(count == 1 ? "Translated 1 entry with {1}" : "Translated {0} entries with {1}", count, translator.Name)
                        + (glossaryPath != null && translator is ClaudeTranslator ? " " + Loc.F("(glossary: {0})", Path.GetFileName(glossaryPath)) : ""));
            }
            catch (Exception ex) when (ex is MachineTranslationException or System.Net.Http.HttpRequestException or IOException)
            {
                await Avalonia.Threading.Dispatcher.UIThread.InvokeAsync(() =>
                    ViewModel.FileStatusText = Loc.F("Translation stopped after {0} entries: {1}", count, ex.Message));
            }
        }

        /// <summary>"glossary.txt" or "Glossar.txt" next to the open translation, or null.</summary>
        private string? FindGlossary()
        {
            string folder = Path.GetDirectoryName(ViewModel.CurrentFilename) ?? "";
            return folder.Length > 0
                ? new[] { "glossary.txt", "Glossar.txt" }.Select(n => Path.Combine(folder, n)).FirstOrDefault(File.Exists)
                : null;
        }

        private void JumpToNextUntranslated()
        {
            var next = ViewModel.GetNextUntranslated();
            if (next != null)
            {
                ViewModel.SelectedEntry = next;
                var dgv = this.FindControl<DataGrid>("DgvTranslation")!;
                dgv.ScrollIntoView(next, null);
                ViewModel.FileStatusText = Loc.T("Jumped to next untranslated entry.");
            }
            else
            {
                ViewModel.FileStatusText = Loc.T("No untranslated entries remaining.");
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
            ViewModel.FileStatusText = Loc.F("Found {0} matches", _foundIndices.Count);
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
                Title = Loc.T("AGS Translation Editor"),
                Width = 460,
                SizeToContent = SizeToContent.Height,
                WindowStartupLocation = WindowStartupLocation.CenterOwner,
                CanResize = false
            };
            bool result = false;
            var panel = new StackPanel { Margin = new Avalonia.Thickness(16), Spacing = 12 };
            panel.Children.Add(new TextBlock { Text = message, TextWrapping = Avalonia.Media.TextWrapping.Wrap });
            var buttons = new StackPanel { Orientation = Avalonia.Layout.Orientation.Horizontal, HorizontalAlignment = Avalonia.Layout.HorizontalAlignment.Right, Spacing = 8 };
            var yes = new Button { Content = Loc.T("Yes") };
            var no = new Button { Content = Loc.T("No") };
            yes.Click += (s, e) => { result = true; dialog.Close(); };
            no.Click += (s, e) => { result = false; dialog.Close(); };
            buttons.Children.Add(yes);
            buttons.Children.Add(no);
            panel.Children.Add(buttons);
            dialog.Content = panel;
            await dialog.ShowDialog(this);
            return result;
        }

        private async System.Threading.Tasks.Task ShowInfoDialog(string message, string? title = null)
        {
            var dialog = new Window
            {
                Title = title ?? Loc.T("Game Info"),
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
            var ok = new Button { Content = Loc.T("OK"), HorizontalAlignment = Avalonia.Layout.HorizontalAlignment.Right };
            ok.Click += (s, e) => dialog.Close();
            panel.Children.Add(ok);
            dialog.Content = panel;
            await dialog.ShowDialog(this);
        }
    }
}
