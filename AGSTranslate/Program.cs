using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using AGSTools;

namespace AGSTranslate
{
    class Program
    {
        private const int ExitOk = 0;
        private const int ExitUsage = 1;
        private const int ExitError = 2;
        private const string Utf8Option = "--utf8";

        /// <summary>Set by <c>--utf8</c>: write the TRS/TRA as UTF-8 regardless of the source's encoding.</summary>
        private static bool _forceUtf8;

        static int Main(string[] args)
        {
            _forceUtf8 = args.Any(a => a.Equals(Utf8Option, StringComparison.OrdinalIgnoreCase));
            args = args.Where(a => !a.Equals(Utf8Option, StringComparison.OrdinalIgnoreCase)).ToArray();
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
            ApplyUtf8Option(doc.Options);
            IReadOnlyList<string> writeWarnings = TrsFile.Write(outFile, doc);

            if (result.Game != null)
                Console.WriteLine($"Game: {result.Game.Title} (AGS {result.Game.EngineVersion}), {result.RoomCount} rooms");
            foreach (var group in result.Texts.GroupBy(t => t.Source))
                Console.WriteLine($"  {group.Key,-14} {group.Count(),6}");
            PrintWarnings(result.Warnings.Concat(writeWarnings).Concat(EngineWarnings(doc.Options, result.Game?.EngineVersion)));
            Console.WriteLine($"{result.Texts.Count} texts → {outFile}");
            return ExitOk;
        }

        static int TrsToTra(string gameFile, string trsFile, string traFile)
        {
            GameInfo info = RequireGameInfo(gameFile);
            Console.WriteLine($"Game: {info.GameTitle}  ID: {info.UniqueId}  AGS {info.Version}");

            TranslationDocument doc = TrsFile.Read(trsFile, AnsiCodepage(info));
            ApplyGame(doc.Options, info);
            ApplyUtf8Option(doc.Options);
            int translated = doc.Entries.Count(p => !string.IsNullOrWhiteSpace(p.Value));
            Console.WriteLine($"Entries in TRS: {doc.Entries.Count} (translated: {translated})");

            PrintWarnings(TraFile.Write(traFile, doc).Concat(EngineWarnings(doc.Options, info.Version)).Concat(FontCoverage.CheckGame(doc, gameFile)));
            Console.WriteLine($"Done → {traFile} ({(doc.Options.IsUtf8 ? "UTF-8" : "8-bit")})");
            return ExitOk;
        }

        static int TraToTrs(string traFile, string trsFile, string? gameFile)
        {
            GameInfo? info = gameFile != null ? RequireGameInfo(gameFile) : null;
            TranslationDocument doc = TraFile.Read(traFile, info != null ? AnsiCodepage(info) : AgsTextEncoding.DefaultAnsiCodepage);
            Console.WriteLine($"Entries in TRA: {doc.Entries.Count}" + (doc.Options.GameName.Length > 0 ? $" (for \"{doc.Options.GameName}\")" : ""));
            ApplyUtf8Option(doc.Options);

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

            MergeResult merge = TranslationMerger.Merge(extraction.ToTranslationDocument(), existing);
            ApplyUtf8Option(merge.Document.Options);
            PrintWarnings(extraction.Warnings.Concat(TrsFile.Write(outFile, merge.Document)));

            Console.WriteLine($"{extraction.Texts.Count} texts in the game");
            Console.WriteLine($"  unchanged:          {merge.ExactCount,6}");
            Console.WriteLine($"  similar (spaces/&N):{merge.SimilarCount,6}");
            Console.WriteLine($"  untranslated:       {merge.NewCount,6}");
            Console.WriteLine($"  obsolete (kept):    {merge.Obsolete.Count,6}");
            Console.WriteLine($"Done → {outFile}");
            return ExitOk;
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
            ApplyGame(doc.Options, info);
            ApplyUtf8Option(doc.Options);
            Console.WriteLine($"{info.GameTitle}: Converting {inFile} to {outFile}");
            PrintWarnings(TraFile.Write(outFile, doc).Concat(EngineWarnings(doc.Options, info.Version)).Concat(FontCoverage.CheckGame(doc, gameFile)));
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

        static void ApplyUtf8Option(TranslationOptions options)
        {
            if (_forceUtf8)
                options.Encoding = "UTF-8";
        }

        /// <summary>UTF-8 translations are only understood by AGS 3.6 and later.</summary>
        static IEnumerable<string> EngineWarnings(TranslationOptions options, string? engineVersion)
        {
            if (options.IsUtf8 && System.Version.TryParse(engineVersion, out Version? version) && version < new Version(3, 6))
                yield return $"UTF-8 translations need AGS 3.6 or later, but the game uses AGS {engineVersion}; it will show garbled special characters.";
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
            Console.WriteLine("  AGSTranslate <game> <in.csv|tsv|po|trs> <out.tra>");
            Console.WriteLine();
            Console.WriteLine("Options:");
            Console.WriteLine("  --utf8   Write the TRS/TRA as UTF-8 (recommended for AGS 3.6+ to show umlauts, „ “ – …)");
            Console.WriteLine("           with extract, merge, tra2trs, trs2tra and the conversion form");
            Console.WriteLine();
            Console.WriteLine("<game> can be the game exe, its .ags file or game28.dta.");
            return ExitUsage;
        }
    }
}
