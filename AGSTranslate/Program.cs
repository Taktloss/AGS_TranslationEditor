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

        static int Main(string[] args)
        {
            try
            {
                string command = args.Length > 0 ? args[0].ToLowerInvariant() : "";
                return command switch
                {
                    "info" when args.Length == 2 => Info(args[1]),
                    "extract" when args.Length is 2 or 3 => Extract(args[1], args.Length == 3 ? args[2] : Path.ChangeExtension(args[1], ".trs")),
                    "trs2tra" when args.Length == 4 => TrsToTra(args[1], args[2], args[3]),
                    "tra2trs" when args.Length is 3 or 4 => TraToTrs(args[1], args[2], args.Length == 4 ? args[3] : null),
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
            IReadOnlyList<string> writeWarnings = TrsFile.Write(outFile, result.ToTranslationDocument());

            if (result.Game != null)
                Console.WriteLine($"Game: {result.Game.Title} (AGS {result.Game.EngineVersion}), {result.RoomCount} rooms");
            foreach (var group in result.Texts.GroupBy(t => t.Source))
                Console.WriteLine($"  {group.Key,-14} {group.Count(),6}");
            PrintWarnings(result.Warnings.Concat(writeWarnings));
            Console.WriteLine($"{result.Texts.Count} texts → {outFile}");
            return ExitOk;
        }

        static int TrsToTra(string gameFile, string trsFile, string traFile)
        {
            GameInfo info = RequireGameInfo(gameFile);
            Console.WriteLine($"Game: {info.GameTitle}  ID: {info.UniqueId}  AGS {info.Version}");

            TranslationDocument doc = TrsFile.Read(trsFile, AnsiCodepage(info));
            ApplyGame(doc.Options, info);
            int translated = doc.Entries.Count(p => !string.IsNullOrWhiteSpace(p.Value));
            Console.WriteLine($"Entries in TRS: {doc.Entries.Count} (translated: {translated})");

            PrintWarnings(TraFile.Write(traFile, doc));
            Console.WriteLine($"Done → {traFile}");
            return ExitOk;
        }

        static int TraToTrs(string traFile, string trsFile, string? gameFile)
        {
            GameInfo? info = gameFile != null ? RequireGameInfo(gameFile) : null;
            TranslationDocument doc = TraFile.Read(traFile, info != null ? AnsiCodepage(info) : AgsTextEncoding.DefaultAnsiCodepage);
            Console.WriteLine($"Entries in TRA: {doc.Entries.Count}" + (doc.Options.GameName.Length > 0 ? $" (for \"{doc.Options.GameName}\")" : ""));

            PrintWarnings(TrsFile.Write(trsFile, doc));
            Console.WriteLine($"Done → {trsFile}");
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
            Console.WriteLine($"{info.GameTitle}: Converting {inFile} to {outFile}");
            PrintWarnings(TraFile.Write(outFile, doc));
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
            Console.WriteLine("  AGSTranslate <game> <in.csv|tsv|po|trs> <out.tra>");
            Console.WriteLine();
            Console.WriteLine("<game> can be the game exe, its .ags file or game28.dta.");
            return ExitUsage;
        }
    }
}
