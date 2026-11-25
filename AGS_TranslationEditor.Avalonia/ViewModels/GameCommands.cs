using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using AGS_TranslationEditor.Services;
using AGSFontEditor.Localization;
using AGSTools;
using CommunityToolkit.Mvvm.Input;

namespace AGS_TranslationEditor.ViewModels
{
    /// <summary>
    /// Commands that read or change a game: extract texts, game info, TRA files, update from the game, check,
    /// install, uninstall, player package and reading the origins of the texts.
    /// </summary>
    public sealed partial class GameCommands
    {
        private readonly MainWindowViewModel _main;

        public GameCommands(MainWindowViewModel main) => _main = main;

        private IDialogService Dialogs => _main.Dialogs;

        private static readonly FileFilter[] GameFileTypes =
        {
            new(Loc.T("AGS Game Files"), "*.ags", "*.exe", "*.bin", "*.dat", "game28.dta", "ac2game.dta"),
            new(Loc.T("All Files"), "*"),
        };

        private string Status
        {
            set => _main.FileStatusText = value;
        }

        private Progress<string> StatusProgress() => new(message => _main.FileStatusText = message);

        /// <summary>Asks for a game file and remembers it for the font and image editors.</summary>
        public async Task<string?> PickGameFile(string title)
        {
            string? file = await Dialogs.OpenFileAsync(title, GameFileTypes);
            if (file != null)
                _main.LastGameFile = file;
            return file;
        }

        private Task<string?> PickTraOutput(string? suggestedName) =>
            Dialogs.SaveFileAsync(Loc.T("Save TRA File"), "tra", new FileFilter(Loc.T("TRA Translation File"), "*.tra"), suggestedName);

        /// <summary>Reads game info, reporting a missing or unreadable game in the status bar.</summary>
        private GameInfo? ReadGameInfo(string gameFile)
        {
            try
            {
                var info = GameInfo.GetGameInfo(gameFile);
                if (info == null)
                    Status = Loc.F("No AGS game data found in {0}.", Path.GetFileName(gameFile));
                return info;
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                Status = Loc.F("Could not read game file: {0}", ex.Message);
                return null;
            }
        }

        /// <summary>Uses the game's id, name and codepage, keeping the translation's own settings.</summary>
        public static void ApplyGame(TranslationOptions options, GameInfo info)
        {
            options.GameUid = info.UniqueId;
            options.GameName = info.GameTitle;
            if (info.IsUnicode)
                options.Encoding = "UTF-8";
            else if (info.TextCodepage > 0)
                options.AnsiCodepage = info.TextCodepage;
        }

        private async Task WriteTra(TranslationDocument doc, GameInfo info, string traPath)
        {
            ApplyGame(doc.Options, info);
            try
            {
                var warnings = await Task.Run(() =>
                {
                    var result = doc.ReadWarnings.Concat(TraFile.Write(traPath, doc))
                        .Concat(EncodingAdvice.Check(doc, info.Version, info.IsUnicode)).ToList();
                    if (info.DataPath.Length > 0)
                        result.AddRange(FontCoverage.CheckGame(doc, info.DataPath));
                    return result;
                });
                int written = doc.Entries.Count(e => !string.IsNullOrWhiteSpace(e.Value));
                Status = Loc.F("TRA created: {0} ({1} translated entries for \"{2}\")", Path.GetFileName(traPath), written, info.GameTitle);
                if (warnings.Count > 0)
                    await Dialogs.ShowInfoAsync(Loc.T("TRA created with warnings:\n\n") + string.Join("\n\n", warnings), Loc.T("Warnings"));
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                Status = Loc.F("TRA export failed: {0}", ex.Message);
            }
        }

        /// <summary>Commands that need an open translation.</summary>
        private bool HasEntries() => _main.HasEntries;

        [RelayCommand(CanExecute = nameof(HasEntries))]
        private async Task ExportTra()
        {
            string? gameFile = await PickGameFile(Loc.T("Select AGS Game File (for Game ID)"));
            if (gameFile == null || ReadGameInfo(gameFile) is not { } info)
                return;
            string? traPath = await PickTraOutput(Path.GetFileNameWithoutExtension(_main.CurrentFilename));
            if (traPath != null)
                await WriteTra(_main.GetDocumentForExport(), info, traPath);
        }

        [RelayCommand]
        private async Task Extract()
        {
            string? gameFile = await PickGameFile(Loc.T("Select AGS Game File"));
            if (gameFile == null)
                return;
            // Game folders are often read-only, so ask where to save
            string? trsPath = await Dialogs.SaveFileAsync(Loc.T("Save Extracted Texts As"), "trs", FileCommands.TrsFilter,
                Path.GetFileNameWithoutExtension(gameFile) + ".trs");
            if (trsPath == null)
                return;

            _main.IsLoading = true;
            Status = Loc.F("Extracting texts from {0}...", Path.GetFileName(gameFile));
            try
            {
                var progress = StatusProgress();
                bool writeContext = _main.Settings.ShowContext;
                var (result, writeWarnings) = await Task.Run(() =>
                {
                    var r = TextExtractor.Extract(gameFile, null, progress);
                    var written = TrsFile.Write(trsPath, r.ToTranslationDocument());
                    if (writeContext)
                        TextContext.Write(TextContext.PathFor(trsPath), TextContext.FromExtraction(r));
                    return (r, written);
                });
                await _main.LoadFile(trsPath);
                await Dialogs.ShowInfoAsync(Reports.Extraction(result, trsPath, writeWarnings, writeContext), Loc.T("Extraction"));
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                Status = Loc.F("Extraction failed: {0}", ex.Message);
            }
            finally
            {
                _main.IsLoading = false;
            }
        }

        [RelayCommand]
        private async Task ShowGameInfo()
        {
            string? gameFile = await PickGameFile(Loc.T("Select AGS Game File"));
            if (gameFile != null && ReadGameInfo(gameFile) is { } info)
                await Dialogs.ShowInfoAsync(Reports.GameInfo(info, gameFile));
        }

        [RelayCommand(CanExecute = nameof(HasEntries))]
        private async Task CreateTra()
        {
            string? gameFile = await PickGameFile(Loc.T("Select AGS Game File"));
            if (gameFile == null || ReadGameInfo(gameFile) is not { } info)
                return;
            string? trsPath = await Dialogs.OpenFileAsync(Loc.T("Select TRS File"), new FileFilter(Loc.T("TRS"), "*.trs"));
            if (trsPath == null)
                return;
            string? traPath = await PickTraOutput(Path.GetFileNameWithoutExtension(trsPath));
            if (traPath == null)
                return;

            TranslationDocument doc;
            try
            {
                int codepage = info.TextCodepage > 0 && !info.IsUnicode ? info.TextCodepage : AgsTextEncoding.DefaultAnsiCodepage;
                doc = TrsFile.Read(trsPath, codepage);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                Status = Loc.F("Could not read TRS file: {0}", ex.Message);
                return;
            }
            await WriteTra(doc, info, traPath);
        }

        /// <summary>
        /// Extracts the game's current texts and carries the open translation over: new texts are added,
        /// texts that only differ in surrounding spaces or the voice prefix keep their translation.
        /// </summary>
        [RelayCommand(CanExecute = nameof(HasEntries))]
        private async Task MergeWithGame()
        {
            if (_main.Entries.Count == 0)
            {
                Status = Loc.T("Open the translation to update first.");
                return;
            }
            string? gameFile = await PickGameFile(Loc.T("Select AGS Game File to Update From"));
            if (gameFile == null)
                return;

            _main.IsLoading = true;
            Status = Loc.F("Extracting texts from {0}...", Path.GetFileName(gameFile));
            try
            {
                var existing = _main.GetDocument();
                var progress = StatusProgress();
                var (extraction, merge) = await Task.Run(() =>
                {
                    var r = TextExtractor.Extract(gameFile, null, progress);
                    return (r, TranslationMerger.Merge(r.ToTranslationDocument(), existing));
                });
                _main.ReplaceDocument(merge.Document);
                _main.UpdateContext(extraction, _main.CurrentFilename);
                Status = Loc.F("Updated from {0} - save to keep the changes.", Path.GetFileName(gameFile));
                await Dialogs.ShowInfoAsync(Reports.Merge(extraction, merge), Loc.T("Update from Game"));
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                Status = Loc.F("Update failed: {0}", ex.Message);
            }
            finally
            {
                _main.IsLoading = false;
            }
        }

        /// <summary>
        /// Checks the open translation against the game: placeholders, voice prefixes, consistency and
        /// GUI text sizes. A glossary named "glossary.txt" or "Glossar.txt" next to the translation is used too.
        /// </summary>
        [RelayCommand(CanExecute = nameof(HasEntries))]
        private async Task Check()
        {
            if (_main.Entries.Count == 0)
            {
                Status = Loc.T("Open a translation to check first.");
                return;
            }
            string? gameFile = await PickGameFile(Loc.T("Select the AGS Game File of this Translation"));
            if (gameFile == null)
                return;

            string? glossaryPath = _main.FindGlossary();
            _main.IsLoading = true;
            Status = Loc.F("Checking against {0}...", Path.GetFileName(gameFile));
            try
            {
                var doc = _main.GetDocument();
                var progress = StatusProgress();
                string? wordsFolder = _main.TranslationFolder();
                SpellingService spelling = _main.Spelling;
                var issues = await Task.Run(() =>
                {
                    var extraction = TextExtractor.Extract(gameFile, null, progress);
                    var fonts = GameFonts.Load(gameFile);
                    var glossary = glossaryPath != null ? Glossary.Read(glossaryPath) : null;
                    return TranslationCheck.Run(doc, extraction, fonts, glossary, spelling.Get(wordsFolder));
                });
                _main.ShowCheckResults(issues, Reports.CheckSummary(issues, glossaryPath));
                Status = Loc.T("Check finished - click a finding to jump to its entry.");
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                Status = Loc.F("Check failed: {0}", ex.Message);
            }
            finally
            {
                _main.IsLoading = false;
            }
        }

        /// <summary>Compiles the open translation for the game into a temporary TRA; null on failure.</summary>
        private async Task<string?> CompileTempTra(GameInfo info)
        {
            var doc = _main.GetDocumentForExport();
            ApplyGame(doc.Options, info);
            string folder = Path.Combine(Path.GetTempPath(), "agstranslate-" + Guid.NewGuid().ToString("N"));
            string tra = Path.Combine(folder, _main.LanguageName() + ".tra");
            try
            {
                Directory.CreateDirectory(folder);
                await Task.Run(() => TraFile.Write(tra, doc));
                return tra;
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                Status = Loc.F("TRA could not be created: {0}", ex.Message);
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

        /// <summary>
        /// Asks whether to add fonts and returns the agsfntN files, the sprite patch (acsprset.spr, sprindex.dat)
        /// and rooms of the chosen folder (empty for none); null if cancelled.
        /// </summary>
        private async Task<List<string>?> PickFonts()
        {
            if (!await Dialogs.AskAsync(Loc.T("Also include changed fonts (agsfntN.ttf / agsfntN.wfn), a sprite patch (acsprset.spr + sprindex.dat) or edited rooms (roomN.crm) from a folder?")))
                return new List<string>();
            string? folder = await Dialogs.PickFolderAsync(Loc.T("Folder with the font files"));
            if (folder == null)
                return null;
            var files = Directory.EnumerateFiles(folder).Where(f => GameFonts.FontNumber(f) >= 0 || GameSprites.IsPatchFile(f) || GameRooms.IsRoomFile(f)).ToList();
            if (files.Count == 0)
                Status = Loc.T("No font files, sprite patch or rooms in that folder; continuing without them.");
            return files;
        }

        /// <summary>
        /// Compiles the open translation and puts it into the game folder (with fonts if wanted), backing up
        /// replaced files, and selects it in acsetup.cfg.
        /// </summary>
        [RelayCommand(CanExecute = nameof(HasEntries))]
        private async Task Install()
        {
            if (_main.Entries.Count == 0)
            {
                Status = Loc.T("Open the translation to install first.");
                return;
            }
            string? gameFile = await PickGameFile(Loc.T("Select the AGS Game to Install Into"));
            if (gameFile == null || ReadGameInfo(gameFile) is not { } info)
                return;
            string gameFolder = Path.GetDirectoryName(Path.GetFullPath(info.DataPath)) ?? "";
            if (!await Dialogs.AskAsync(Loc.F("Install \"{0}\" into\n{1}?\n\nReplaced files are backed up and can be restored with Uninstall.", _main.LanguageName(), gameFolder)))
                return;
            if (await PickFonts() is not { } fonts)
                return;
            if (await CompileTempTra(info) is not { } tra)
                return;
            try
            {
                var result = await Task.Run(() => TranslationInstaller.Install(gameFile, tra, fonts));
                Status = Loc.F("Installed into {0}.", Path.GetFileName(result.GameFolder));
                await Dialogs.ShowInfoAsync(Reports.Install(result), Loc.T("Install into Game"));
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                Status = Loc.F("Install failed: {0}", ex.Message);
            }
            finally
            {
                DeleteTempTra(tra);
            }
        }

        [RelayCommand]
        private async Task Uninstall()
        {
            string? gameFile = await PickGameFile(Loc.T("Select the AGS Game to Uninstall From"));
            if (gameFile == null)
                return;
            if (!await Dialogs.AskAsync(Loc.T("Undo the last translation install in this game? The backed-up files are put back; "
                + "installed files are moved into the backup folder, not deleted.")))
                return;
            try
            {
                var result = await Task.Run(() => TranslationInstaller.Uninstall(gameFile));
                Status = Loc.T("Uninstalled.");
                await Dialogs.ShowInfoAsync(Reports.Uninstall(result), Loc.T("Uninstall from Game"));
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                Status = Loc.F("Uninstall failed: {0}", ex.Message);
            }
        }

        /// <summary>Creates a zip for players with the TRA, fonts, install scripts and a read-me.</summary>
        [RelayCommand(CanExecute = nameof(HasEntries))]
        private async Task CreatePlayerPackage()
        {
            if (_main.Entries.Count == 0)
            {
                Status = Loc.T("Open the translation to package first.");
                return;
            }
            string? gameFile = await PickGameFile(Loc.T("Select the AGS Game of this Translation"));
            if (gameFile == null || ReadGameInfo(gameFile) is not { } info)
                return;
            if (await PickFonts() is not { } fonts)
                return;
            string? zipPath = await Dialogs.SaveFileAsync(Loc.T("Save Player Package"), "zip", new FileFilter(Loc.T("Zip archive"), "*.zip"),
                $"{info.GameTitle} - {_main.LanguageName()}.zip");
            if (zipPath == null)
                return;
            if (await CompileTempTra(info) is not { } tra)
                return;
            try
            {
                var warnings = await Task.Run(() => PlayerPackage.Create(gameFile, tra, fonts, zipPath));
                Status = Loc.F("Player package created: {0}", Path.GetFileName(zipPath));
                await Dialogs.ShowInfoAsync(Loc.F("Created {0}.\n\nPlayers unpack it into the game folder and run install.bat (Windows) "
                    + "or install.sh (Linux/macOS); see LIESMICH.txt in the package.", zipPath)
                    + (warnings.Count > 0 ? "\n\n" + Loc.T("Warnings:") + "\n" + string.Join("\n", warnings.Select(w => "• " + w)) : ""), Loc.T("Player Package"));
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                Status = Loc.F("Package failed: {0}", ex.Message);
            }
            finally
            {
                DeleteTempTra(tra);
            }
        }

        /// <summary>Reads where each text comes from (room, script, speaker) and keeps it as the context file.</summary>
        [RelayCommand(CanExecute = nameof(HasEntries))]
        private async Task ReadOrigins()
        {
            if (_main.CurrentFilename.Length == 0)
            {
                Status = Loc.T("Open or save the translation first; the origins are kept next to it.");
                return;
            }
            string? gameFile = await PickGameFile(Loc.T("Select the AGS Game File of this Translation"));
            if (gameFile == null)
                return;
            _main.IsLoading = true;
            try
            {
                var progress = StatusProgress();
                var extraction = await Task.Run(() => TextExtractor.Extract(gameFile, null, progress));
                _main.CreateContext(extraction);
                Status = Loc.F("Origins read from {0}.", Path.GetFileName(gameFile));
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or AgsFormatException)
            {
                Status = Loc.F("Could not read the game: {0}", ex.Message);
            }
            finally
            {
                _main.IsLoading = false;
            }
        }

        /// <summary>Chooses the game whose fonts the live preview uses.</summary>
        [RelayCommand]
        private async Task PickPreviewGame()
        {
            string? gameFile = await PickGameFile(Loc.T("Select the AGS Game File for the Fonts"));
            if (gameFile != null)
                await _main.Live.LoadPreviewFontsAsync(gameFile);
        }
    }
}
