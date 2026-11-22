using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using AGSFontEditor.Localization;
using AGSTools;
using TranslationApi;

namespace AGS_TranslationEditor.ViewModels
{
    /// <summary>The texts of the result dialogs; kept apart from the commands so they can be tested.</summary>
    public static class Reports
    {
        /// <summary>Up to <paramref name="max"/> warnings as a list, with how many more there are.</summary>
        public static void AppendWarnings(StringBuilder text, IReadOnlyCollection<string> warnings, int max = 10)
        {
            if (warnings.Count == 0)
                return;
            text.AppendLine().AppendLine(Loc.T("Warnings:"));
            foreach (string w in warnings.Take(max))
                text.AppendLine("• " + w);
            if (warnings.Count > max)
                text.AppendLine(Loc.F("... and {0} more.", warnings.Count - max));
        }

        public static string Extraction(ExtractionResult result, string trsPath, IEnumerable<string> writeWarnings, bool withContext)
        {
            var text = new StringBuilder();
            text.AppendLine(Loc.F("{0} texts extracted to {1}.", result.Texts.Count, Path.GetFileName(trsPath)));
            if (result.Game != null)
                text.AppendLine(Loc.F("Game: {0} (AGS {1}), {2} rooms.", result.Game.Title, result.Game.EngineVersion, result.RoomCount));
            foreach (var group in result.Texts.GroupBy(t => t.Source))
                text.AppendLine($"  {group.Key}: {group.Count()}");
            if (result.VoicedCount > 0)
                text.AppendLine(Loc.F("{0} texts got a voice prefix (&N).", result.VoicedCount));
            if (withContext)
                text.AppendLine(Loc.F("Context saved: {0} texts with a known speaker.", result.Texts.Count(t => t.Speaker != null)));
            AppendWarnings(text, result.Warnings.Concat(writeWarnings).ToList());
            return text.ToString();
        }

        public static string Merge(ExtractionResult extraction, MergeResult merge)
        {
            var text = new StringBuilder();
            text.AppendLine(Loc.F("{0} texts in the game:", extraction.Texts.Count));
            text.AppendLine(Loc.F("  {0} translations carried over unchanged", merge.ExactCount));
            text.AppendLine(Loc.F("  {0} carried over from texts differing only in spaces or voice prefix", merge.SimilarCount));
            text.AppendLine(Loc.F("  {0} without translation", merge.NewCount));
            if (merge.Obsolete.Count > 0)
            {
                text.AppendLine();
                text.AppendLine(Loc.F("{0} translated texts no longer exist in the game. They were kept at the end of the list, e.g.:", merge.Obsolete.Count));
                foreach (var entry in merge.Obsolete.Take(5))
                    text.AppendLine("• " + entry.Key);
            }
            AppendWarnings(text, extraction.Warnings);
            return text.ToString();
        }

        public static string GameInfo(GameInfo info, string gameFile)
        {
            string encoding = info.IsUnicode ? "UTF-8" : info.TextCodepage > 0 ? Loc.F("8-bit, codepage {0}", info.TextCodepage) : Loc.T("8-bit (default codepage)");
            string dataFile = string.Equals(Path.GetFullPath(info.DataPath), Path.GetFullPath(gameFile), StringComparison.OrdinalIgnoreCase)
                ? "" : "\n" + Loc.F("Game data: {0}", Path.GetFileName(info.DataPath));
            return Loc.F("Title: {0}\nGame ID: {1} (legacy hex: {2})\nAGS version: {3}\nText encoding: {4}", info.GameTitle, info.UniqueId, info.GameUID, info.Version, encoding)
                + dataFile;
        }

        public static string Install(InstallResult result)
        {
            var text = new StringBuilder();
            text.AppendLine(Loc.F("\"{0}\" was installed into {1}.", result.Language, result.GameFolder));
            text.AppendLine();
            foreach (string f in result.Added) text.AppendLine(Loc.F("added: {0}", f));
            foreach (string f in result.Replaced) text.AppendLine(Loc.F("replaced: {0} (original backed up)", f));
            foreach (string c in result.ConfigFiles) text.AppendLine(Loc.F("language set in: {0}", c));
            text.AppendLine().AppendLine(Loc.F("Backup: {0}", result.BackupFolder));
            if (result.Warnings.Count > 0)
                text.AppendLine().AppendLine(Loc.T("Warnings:")).AppendLine(string.Join("\n", result.Warnings.Select(w => "• " + w)));
            return text.ToString();
        }

        public static string Uninstall(UninstallResult result)
        {
            var text = new StringBuilder();
            foreach (string f in result.Restored) text.AppendLine(Loc.F("restored: {0}", f));
            foreach (string f in result.Removed) text.AppendLine(Loc.F("moved into the backup folder: {0}", f));
            foreach (string w in result.Warnings) text.AppendLine(Loc.F("Warning: {0}", w));
            return text.ToString();
        }

        public static string CheckSummary(IReadOnlyCollection<CheckIssue> issues, string? glossaryPath) =>
            Loc.F("{0} errors, {1} warnings, {2} notes.", issues.Count(i => i.Severity == CheckSeverity.Error),
                issues.Count(i => i.Severity == CheckSeverity.Warning), issues.Count(i => i.Severity == CheckSeverity.Info)) + " "
            + (glossaryPath != null ? Loc.F("Glossary: {0}", Path.GetFileName(glossaryPath)) : Loc.T("No glossary.txt next to the translation."));

        /// <summary>Why a shortened text fits or not, for the check panel.</summary>
        public static string FitNote(FitSuggestion r)
        {
            string Size(FitResult f) => Loc.F(f.IsHeight ? "{0} px high" : "{0} px wide", f.Size);
            string room = Loc.F("room: {0} px", r.Before.Limit);
            return r.Outcome switch
            {
                FitOutcome.Fits => $"{Size(r.Before)} → {Size(r.After!)}, {room}",
                FitOutcome.StillTooBig => Loc.F("The shortest answer \"{0}\" is still too big ({1}, {2}).", r.Attempt ?? "", Size(r.After!), room),
                FitOutcome.MarkupChanged => Loc.F("The answer \"{0}\" lost or added placeholders or line breaks.", r.Attempt ?? ""),
                _ => Loc.T("The service gave no answer for this text."),
            };
        }
    }
}
