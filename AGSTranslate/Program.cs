using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using AGSTools;
using TranslationApi;

namespace AGSTranslate
{
    class Program
    {
        private const int ExitOk = 0;
        private const int ExitUsage = 1;
        private const int ExitError = 2;
        private const string Utf8Option = "--utf8";
        private const string EightBitOption = "--8bit";
        private const string ContextOption = "--context";

        /// <summary>Set by <c>--utf8</c> or <c>--8bit</c>: the encoding of the written TRS/TRA, regardless of the source's encoding.</summary>
        private static string? _forcedEncoding;

        /// <summary>Set by <c>--context</c>: extract and merge also write the context file next to the TRS.</summary>
        private static bool _writeContext;

        static int Main(string[] args)
        {
            bool utf8 = args.Any(a => a.Equals(Utf8Option, StringComparison.OrdinalIgnoreCase));
            bool eightBit = args.Any(a => a.Equals(EightBitOption, StringComparison.OrdinalIgnoreCase));
            if (utf8 && eightBit)
            {
                Console.Error.WriteLine($"Error: {Utf8Option} and {EightBitOption} cannot be combined.");
                return ExitUsage;
            }
            _forcedEncoding = utf8 ? "UTF-8" : eightBit ? "ASCII" : null;
            _writeContext = args.Any(a => a.Equals(ContextOption, StringComparison.OrdinalIgnoreCase));
            args = args.Where(a => !a.Equals(Utf8Option, StringComparison.OrdinalIgnoreCase)
                && !a.Equals(EightBitOption, StringComparison.OrdinalIgnoreCase)
                && !a.Equals(ContextOption, StringComparison.OrdinalIgnoreCase)).ToArray();
            try
            {
                string command = args.Length > 0 ? args[0].ToLowerInvariant() : "";
                return command switch
                {
                    "info" when args.Length == 2 => Info(args[1]),
                    "extract" when args.Length is 2 or 3 => Extract(args[1], args.Length == 3 ? args[2] : Path.ChangeExtension(args[1], ".trs")),
                    "trs2tra" when args.Length == 4 => TrsToTra(args[1], args[2], args[3]),
                    "tra2trs" when args.Length is 3 or 4 => TraToTrs(args[1], args[2], args.Length == 4 ? args[3] : null),
                    "merge" when args.Length == 4 => Merge(args[1], args[2], args[3]),
                    "fonts" when args.Length is 2 or 3 => Fonts(args[1], args.Length == 3 ? args[2] : null),
                    "check" when args.Length is 3 or 4 => Check(args[1], args[2], args.Length == 4 ? args[3] : null),
                    "install" when args.Length >= 3 => Install(args[1], args[2], args.Skip(3).ToList()),
                    "uninstall" when args.Length == 2 => Uninstall(args[1]),
                    "pack" when args.Length >= 4 => Pack(args[1], args[2], args[3], args.Skip(4).ToList()),
                    "translate" when args.Length >= 3 => Translate(args[1], args[2], args.Skip(3).ToList()),
                    _ when args.Length == 3 && File.Exists(args[0]) => LegacyConvert(args[0], args[1], args[2]),
                    _ => ShowHelp(),
                };
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                Console.Error.WriteLine($"Error: {ex.Message}");
                return ExitError;
            }
        }

        static int Info(string gameFile)
        {
            GameInfo info = RequireGameInfo(gameFile);
            Console.WriteLine($"Title:       {info.GameTitle}");
            Console.WriteLine($"Game ID:     {info.UniqueId}");
            Console.WriteLine($"AGS version: {info.Version}");
            Console.WriteLine($"Encoding:    {(info.IsUnicode ? "UTF-8" : $"8-bit, codepage {(info.TextCodepage > 0 ? info.TextCodepage : AgsTextEncoding.DefaultAnsiCodepage)}")}");
            if (!string.Equals(Path.GetFullPath(info.DataPath), Path.GetFullPath(gameFile), StringComparison.OrdinalIgnoreCase))
                Console.WriteLine($"Game data:   {info.DataPath}");
            return ExitOk;
        }

        static int Extract(string gameFile, string outFile)
        {
            Console.WriteLine($"Extracting texts from: {gameFile}");
            ExtractionResult result = TextExtractor.Extract(gameFile, null, new Progress<string>(Console.Error.WriteLine));
            TranslationDocument doc = result.ToTranslationDocument();
            ApplyEncodingOption(doc.Options);
            IReadOnlyList<string> writeWarnings = TrsFile.Write(outFile, doc);

            if (result.Game != null)
                Console.WriteLine($"Game: {result.Game.Title} (AGS {result.Game.EngineVersion}), {result.RoomCount} rooms");
            foreach (var group in result.Texts.GroupBy(t => t.Source))
                Console.WriteLine($"  {group.Key,-14} {group.Count(),6}");
            PrintWarnings(result.Warnings.Concat(writeWarnings).Concat(EncodingAdvice.Check(doc, result.Game?.EngineVersion, result.Game?.IsUnicode ?? false)));
            Console.WriteLine($"{result.Texts.Count} texts → {outFile}");
            WriteContext(result, outFile);
            return ExitOk;
        }

        static int TrsToTra(string gameFile, string trsFile, string traFile)
        {
            GameInfo info = RequireGameInfo(gameFile);
            Console.WriteLine($"Game: {info.GameTitle}  ID: {info.UniqueId}  AGS {info.Version}");

            TranslationDocument doc = TrsFile.Read(trsFile, AnsiCodepage(info));
            PrintWarnings(doc.ReadWarnings);
            ApplyGame(doc.Options, info);
            ApplyEncodingOption(doc.Options);
            int translated = doc.Entries.Count(p => !string.IsNullOrWhiteSpace(p.Value));
            Console.WriteLine($"Entries in TRS: {doc.Entries.Count} (translated: {translated})");

            PrintWarnings(TraFile.Write(traFile, doc).Concat(EncodingAdvice.Check(doc, info.Version, info.IsUnicode)).Concat(FontCoverage.CheckGame(doc, gameFile)));
            Console.WriteLine($"Done → {traFile} ({(doc.Options.IsUtf8 ? "UTF-8" : "8-bit")})");
            return ExitOk;
        }

        static int TraToTrs(string traFile, string trsFile, string? gameFile)
        {
            GameInfo? info = gameFile != null ? RequireGameInfo(gameFile) : null;
            TranslationDocument doc = TraFile.Read(traFile, info != null ? AnsiCodepage(info) : AgsTextEncoding.DefaultAnsiCodepage);
            Console.WriteLine($"Entries in TRA: {doc.Entries.Count}" + (doc.Options.GameName.Length > 0 ? $" (for \"{doc.Options.GameName}\")" : ""));
            ApplyEncodingOption(doc.Options);

            PrintWarnings(TrsFile.Write(trsFile, doc));
            Console.WriteLine($"Done → {trsFile}");
            return ExitOk;
        }

        static int Merge(string gameFile, string translationFile, string outFile)
        {
            ExtractionResult extraction = TextExtractor.Extract(gameFile, null, new Progress<string>(Console.Error.WriteLine));
            int codepage = extraction.Game is { IsUnicode: false, TextCodepage: > 0 } g ? g.TextCodepage : AgsTextEncoding.DefaultAnsiCodepage;
            TranslationDocument existing = Path.GetExtension(translationFile).Equals(".tra", StringComparison.OrdinalIgnoreCase)
                ? TraFile.Read(translationFile, codepage)
                : TrsFile.Read(translationFile, codepage);
            PrintWarnings(existing.ReadWarnings);

            MergeResult merge = TranslationMerger.Merge(extraction.ToTranslationDocument(), existing);
            ApplyEncodingOption(merge.Document.Options);
            PrintWarnings(extraction.Warnings.Concat(TrsFile.Write(outFile, merge.Document)));

            Console.WriteLine($"{extraction.Texts.Count} texts in the game");
            Console.WriteLine($"  unchanged:          {merge.ExactCount,6}");
            Console.WriteLine($"  similar (spaces/&N):{merge.SimilarCount,6}");
            Console.WriteLine($"  untranslated:       {merge.NewCount,6}");
            Console.WriteLine($"  obsolete (kept):    {merge.Obsolete.Count,6}");
            Console.WriteLine($"Done → {outFile}");
            WriteContext(extraction, outFile);
            return ExitOk;
        }

        /// <summary>With --context: writes who says each text and where it comes from next to the TRS.</summary>
        static void WriteContext(ExtractionResult extraction, string trsFile)
        {
            if (!_writeContext)
                return;
            var origins = TextContext.FromExtraction(extraction);
            string path = TextContext.PathFor(trsFile);
            TextContext.Write(path, origins);
            int spoken = origins.Values.Count(o => o.Speaker != null && o.Speaker != TextContext.Narration);
            Console.WriteLine($"Context → {path} ({spoken} texts with speaker)");
        }

        static int Fonts(string gameFile, string? outDir)
        {
            List<GameFont> fonts = GameFonts.Load(gameFile);
            if (fonts.Count == 0)
            {
                Console.Error.WriteLine("No fonts found.");
                return ExitError;
            }
            Console.WriteLine($"{"#",3}  {"File",-14} {"Kind",-4} {"Origin",-7} {"Size",4} {"Outline",-7} {"Chars",5}  Name");
            foreach (GameFont font in fonts)
            {
                string outline = font.Info?.Outline switch
                {
                    null => "",
                    FontInfo.NoOutline => "none",
                    FontInfo.AutoOutline => "auto",
                    int n => $"font {n}",
                };
                int chars = font.Wfn?.Glyphs.Count(g => g is { IsEmpty: false }) ?? font.Ttf?.CodePoints.Count ?? 0;
                string name = font.Error != null ? $"ERROR: {font.Error}" : font.Ttf?.Name ?? "";
                Console.WriteLine($"{font.Number,3}  {font.FileName,-14} {font.Kind.ToString().ToUpperInvariant(),-4} "
                    + $"{(font.Origin == FontOrigin.Package ? "package" : "folder"),-7} {font.Info?.Size.ToString() ?? "",4} {outline,-7} {chars,5}  {name}");
                foreach (string problem in font.Wfn?.Problems.Take(3) ?? Enumerable.Empty<string>())
                    Console.WriteLine($"{"",5}{problem}");
            }

            if (outDir == null)
                return ExitOk;
            // Packaged fonts copied into the game folder would override the game's own fonts
            string gameDir = Path.GetDirectoryName(Path.GetFullPath(gameFile)) ?? "";
            if (string.Equals(Path.TrimEndingDirectorySeparator(Path.GetFullPath(outDir)), gameDir, StringComparison.OrdinalIgnoreCase))
            {
                Console.Error.WriteLine("Error: choose an output folder other than the game folder.");
                return ExitError;
            }
            List<string> written = GameFonts.Export(fonts, outDir, overwrite: true);
            Console.WriteLine($"{written.Count} font files → {outDir}");
            return ExitOk;
        }

        static int Check(string gameFile, string translationFile, string? glossaryFile)
        {
            ExtractionResult extraction = TextExtractor.Extract(gameFile, null, new Progress<string>(Console.Error.WriteLine));
            int codepage = extraction.Game is { IsUnicode: false, TextCodepage: > 0 } g ? g.TextCodepage : AgsTextEncoding.DefaultAnsiCodepage;
            TranslationDocument doc = Path.GetExtension(translationFile).Equals(".tra", StringComparison.OrdinalIgnoreCase)
                ? TraFile.Read(translationFile, codepage)
                : TrsFile.Read(translationFile, codepage);
            PrintWarnings(doc.ReadWarnings);
            Glossary? glossary = glossaryFile != null ? Glossary.Read(glossaryFile) : null;
            List<GameFont> fonts = GameFonts.Load(gameFile);

            List<CheckIssue> issues = TranslationCheck.Run(doc, extraction, fonts, glossary);
            foreach (var group in issues.GroupBy(i => i.Category).OrderBy(g => g.Min(i => i.Severity)))
            {
                Console.WriteLine();
                Console.WriteLine($"== {group.Key} ({group.Count()}) ==");
                foreach (CheckIssue issue in group)
                {
                    Console.WriteLine($"{issue.Severity.ToString().ToUpperInvariant()}: {Shorten(issue.Key)}");
                    Console.WriteLine($"    {issue.Message}");
                }
            }
            Console.WriteLine();
            Console.WriteLine($"{issues.Count(i => i.Severity == CheckSeverity.Error)} errors, "
                + $"{issues.Count(i => i.Severity == CheckSeverity.Warning)} warnings, "
                + $"{issues.Count(i => i.Severity == CheckSeverity.Info)} notes");
            return issues.Any(i => i.Severity == CheckSeverity.Error) ? ExitError : ExitOk;

            static string Shorten(string text) => text.Length > 90 ? text[..87] + "..." : text;
        }

        static int Install(string gameFile, string traFile, List<string> fontArgs)
        {
            bool force = fontArgs.Remove("--force");
            InstallResult result = TranslationInstaller.Install(gameFile, traFile, ExpandFonts(fontArgs), force);
            Console.WriteLine($"Installed \"{result.Language}\" into {result.GameFolder}");
            foreach (string f in result.Added)
                Console.WriteLine($"  added     {f}");
            foreach (string f in result.Replaced)
                Console.WriteLine($"  replaced  {f} (original backed up)");
            foreach (string c in result.ConfigFiles)
                Console.WriteLine($"  language  set in {c}");
            Console.WriteLine($"Backup: {result.BackupFolder}");
            Console.WriteLine("Undo with: AGSTranslate uninstall <game>");
            PrintWarnings(result.Warnings);
            return ExitOk;
        }

        static int Uninstall(string gameFile)
        {
            UninstallResult result = TranslationInstaller.Uninstall(gameFile);
            foreach (string f in result.Restored)
                Console.WriteLine($"  restored  {f}");
            foreach (string f in result.Removed)
                Console.WriteLine($"  removed   {f} (moved into the backup folder)");
            Console.WriteLine($"Undone: {result.BackupFolder}");
            PrintWarnings(result.Warnings);
            return ExitOk;
        }

        static int Pack(string gameFile, string traFile, string zipFile, List<string> fontArgs)
        {
            List<string> warnings = PlayerPackage.Create(gameFile, traFile, ExpandFonts(fontArgs), zipFile);
            PrintWarnings(warnings);
            Console.WriteLine($"Player package → {zipFile}");
            return ExitOk;
        }

        /// <summary>Machine-translates the empty entries of a TRS/TRA. API keys come from DEEPL_AUTH_KEY / ANTHROPIC_API_KEY.</summary>
        static int Translate(string inFile, string outFile, List<string> options)
        {
            string? Option(string name)
            {
                int i = options.FindIndex(o => o.Equals(name, StringComparison.OrdinalIgnoreCase));
                return i >= 0 && i + 1 < options.Count ? options[i + 1] : null;
            }

            TranslationDocument doc = Path.GetExtension(inFile).Equals(".tra", StringComparison.OrdinalIgnoreCase)
                ? TraFile.Read(inFile, AgsTextEncoding.DefaultAnsiCodepage)
                : TrsFile.Read(inFile, AgsTextEncoding.DefaultAnsiCodepage);
            PrintWarnings(doc.ReadWarnings);
            ApplyEncodingOption(doc.Options);
            string? glossaryFile = Option("--glossary");
            var mtOptions = new MachineTranslationOptions
            {
                SourceLanguage = Option("--from") is { } from && !from.Equals("auto", StringComparison.OrdinalIgnoreCase) ? from : options.Contains("--from") ? "" : "en",
                TargetLanguage = Option("--to") ?? "de",
                Formality = options.Contains("--informal") ? Formality.Informal : options.Contains("--formal") ? Formality.Formal : Formality.Default,
                GameTitle = doc.Options.GameName,
                Glossary = glossaryFile != null ? Glossary.Read(glossaryFile) : null,
            };

            List<string> keys = doc.Entries.Where(e => string.IsNullOrEmpty(e.Value)).Select(e => e.Key).ToList();
            int done = 0;
            try
            {
                IMachineTranslator translator = MachineTranslators.Create(Option("--service") ?? MachineTranslators.Google, claudeModel: Option("--model"));
                Console.WriteLine($"Translating {keys.Count} empty entries with {translator.Name} ({(mtOptions.SourceLanguage.Length > 0 ? mtOptions.SourceLanguage : "auto")} → {mtOptions.TargetLanguage})");
                MachineTranslation.TranslateAsync(translator, keys, mtOptions, (i, text) =>
                {
                    doc.Entries[keys[i]] = text;
                    if (++done % 50 == 0)
                        Console.Error.WriteLine($"  {done}/{keys.Count}");
                }).GetAwaiter().GetResult();
            }
            catch (Exception ex) when (ex is MachineTranslationException or System.Net.Http.HttpRequestException)
            {
                Console.Error.WriteLine($"Error: {ex.Message}");
                if (done == 0)
                    return ExitError;
                Console.Error.WriteLine($"Writing the {done} entries translated so far.");
            }

            PrintWarnings(TrsFile.Write(outFile, doc));
            Console.WriteLine($"{done} entries translated → {outFile}");
            return done == keys.Count ? ExitOk : ExitError;
        }

        // Font arguments can be files or folders; a folder stands for all agsfntN.ttf/.wfn files in it
        static List<string> ExpandFonts(IEnumerable<string> args) =>
            args.SelectMany<string, string>(a => Directory.Exists(a)
                ? Directory.EnumerateFiles(a).Where(f => GameFonts.FontNumber(f) >= 0).OrderBy(f => f, StringComparer.OrdinalIgnoreCase)
                : new[] { a }).ToList();

        /// <summary>Old form: AGSTranslate &lt;game&gt; &lt;file.csv|tsv|trs|po&gt; &lt;output.tra&gt;</summary>
        static int LegacyConvert(string gameFile, string inFile, string outFile)
        {
            GameInfo info = RequireGameInfo(gameFile);
            TranslationDocument doc = Path.GetExtension(inFile).ToLowerInvariant() switch
            {
                ".csv" or ".tsv" => new TranslationDocument { Entries = CSVFormat.OpenCSV(inFile) },
                ".po" => new TranslationDocument { Entries = POFormat.OpenPO(inFile) },
                ".trs" => TrsFile.Read(inFile, AnsiCodepage(info)),
                _ => throw new IOException($"Unsupported input file type '{Path.GetExtension(inFile)}' (expected .csv, .tsv, .po or .trs)."),
            };
            PrintWarnings(doc.ReadWarnings);
            ApplyGame(doc.Options, info);
            ApplyEncodingOption(doc.Options);
            Console.WriteLine($"{info.GameTitle}: Converting {inFile} to {outFile}");
            PrintWarnings(TraFile.Write(outFile, doc).Concat(EncodingAdvice.Check(doc, info.Version, info.IsUnicode)).Concat(FontCoverage.CheckGame(doc, gameFile)));
            return ExitOk;
        }

        static GameInfo RequireGameInfo(string gameFile) =>
            GameInfo.GetGameInfo(gameFile) ?? throw new IOException($"No AGS game data found in '{gameFile}'.");

        static int AnsiCodepage(GameInfo info) =>
            info.TextCodepage > 0 && !info.IsUnicode ? info.TextCodepage : AgsTextEncoding.DefaultAnsiCodepage;

        static void ApplyGame(TranslationOptions options, GameInfo info)
        {
            options.GameUid = info.UniqueId;
            options.GameName = info.GameTitle;
            options.AnsiCodepage = AnsiCodepage(info);
            if (info.IsUnicode)
                options.Encoding = "UTF-8";
        }

        static void ApplyEncodingOption(TranslationOptions options)
        {
            if (_forcedEncoding != null)
                options.Encoding = _forcedEncoding;
        }

        static void PrintWarnings(IEnumerable<string> warnings)
        {
            foreach (string warning in warnings)
                Console.Error.WriteLine($"Warning: {warning}");
        }

        static int ShowHelp()
        {
            Console.WriteLine("AGSTranslate - tools for Adventure Game Studio translations");
            Console.WriteLine();
            Console.WriteLine("Usage:");
            Console.WriteLine("  AGSTranslate info <game>                       Show game title, ID, version and text encoding");
            Console.WriteLine("  AGSTranslate extract <game> [out.trs]          Extract all translatable texts into a TRS file");
            Console.WriteLine("  AGSTranslate trs2tra <game> <in.trs> <out.tra> Compile a TRS file for the game");
            Console.WriteLine("  AGSTranslate tra2trs <in.tra> <out.trs> [game] Decompile a TRA file into a TRS file");
            Console.WriteLine("  AGSTranslate merge <game> <old.trs|tra> <out.trs>");
            Console.WriteLine("                                                 Update a translation to the game's current texts");
            Console.WriteLine("  AGSTranslate fonts <game> [outdir]             List the game fonts; with outdir, export the font files");
            Console.WriteLine("  AGSTranslate check <game> <in.trs|tra> [glossary.txt]");
            Console.WriteLine("                                                 Check placeholders, consistency, glossary and GUI text sizes");
            Console.WriteLine("  AGSTranslate install <game> <file.tra> [fonts...] [--force]");
            Console.WriteLine("                                                 Copy TRA and fonts into the game folder (with backup), select the language");
            Console.WriteLine("  AGSTranslate uninstall <game>                  Undo the last install");
            Console.WriteLine("  AGSTranslate pack <game> <file.tra> <out.zip> [fonts...]");
            Console.WriteLine("                                                 Create a zip for players with install scripts");
            Console.WriteLine("  (fonts: agsfntN.ttf/.wfn files or folders containing them)");
            Console.WriteLine("  AGSTranslate translate <in.trs|tra> <out.trs> [--service google|deepl|claude] [--from en] [--to de]");
            Console.WriteLine("                         [--informal|--formal] [--glossary file] [--model claude-model]");
            Console.WriteLine("                                                 Machine-translate the empty entries");
            Console.WriteLine("                                                 (keys: DEEPL_AUTH_KEY, ANTHROPIC_API_KEY)");
            Console.WriteLine("  AGSTranslate <game> <in.csv|tsv|po|trs> <out.tra>");
            Console.WriteLine();
            Console.WriteLine("Options:");
            Console.WriteLine("  --utf8   Write the TRS/TRA as UTF-8 (extract, merge, tra2trs, trs2tra, conversion form).");
            Console.WriteLine("           Needed for characters outside the game's codepage, e.g. „ “ – … in a 1252 game.");
            Console.WriteLine("           AGS 3.6+ only; for games with 8-bit texts it works on Windows only.");
            Console.WriteLine("  --8bit   Write the TRS/TRA in the game's 8-bit codepage (works with every engine and OS).");
            Console.WriteLine("  --context  extract, merge: also write <out>.context.json with the speaker and origin of");
            Console.WriteLine("           every text, shown by the editor when context is enabled in its settings.");
            Console.WriteLine();
            Console.WriteLine("<game> can be the game exe, its .ags file or game28.dta.");
            return ExitUsage;
        }
    }
}
