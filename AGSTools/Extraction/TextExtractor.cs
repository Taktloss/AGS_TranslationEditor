using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;

namespace AGSTools
{
    /// <summary>Where an extracted text comes from.</summary>
    public enum TextSource
    {
        Script,
        DialogOption,
        Gui,
        CharacterName,
        InventoryItem,
        GlobalMessage,
        DialogLine,
        Hotspot,
        RoomObject,
        RoomMessage,
        RoomScript,
    }

    /// <summary>
    /// A translatable text (in runtime form) and where it was found. <see cref="Speaker"/> is the script name of
    /// the character that says it (cEgo, player), "Display" for narration, or null if unknown.
    /// </summary>
    public sealed record ExtractedText(string Text, TextSource Source, string Location, string? Speaker = null);

    public sealed class ExtractionOptions
    {
        /// <summary>
        /// Include every script string literal instead of only those that look like player-visible text.
        /// This matches what other extraction tools do, at the cost of many technical strings.
        /// </summary>
        public bool IncludeAllScriptStrings { get; set; }
    }

    public sealed class ExtractionResult
    {
        /// <summary>Game information; null if the game data could only be scanned heuristically.</summary>
        public GameData? Game { get; internal set; }

        /// <summary>Unique texts in extraction order.</summary>
        public List<ExtractedText> Texts { get; } = new();

        public List<string> Warnings { get; } = new();

        public int RoomCount { get; internal set; }

        /// <summary>Number of texts that got an "&amp;N " voice prefix.</summary>
        public int VoicedCount { get; internal set; }

        /// <summary>
        /// Creates an untranslated translation for the extracted texts, identified for the game
        /// and using the game's text encoding.
        /// </summary>
        public TranslationDocument ToTranslationDocument()
        {
            var doc = new TranslationDocument();
            if (Game != null)
            {
                doc.Options.GameUid = Game.UniqueId;
                doc.Options.GameName = Game.Title;
                if (Game.IsUnicode)
                    doc.Options.Encoding = "UTF-8";
                else if (Game.TextCodepage > 0)
                    doc.Options.AnsiCodepage = Game.TextCodepage;
            }
            foreach (ExtractedText text in Texts)
                doc.Entries.TryAdd(TextEscaping.ToTrs(text.Text), "");
            return doc;
        }
    }

    /// <summary>
    /// Extracts all translatable texts from an AGS game: script strings (with voice prefixes),
    /// dialog options, GUI texts, character and inventory names, messages and room texts.
    /// </summary>
    public static class TextExtractor
    {
        public static ExtractionResult Extract(string gamePath, ExtractionOptions? options = null, IProgress<string>? progress = null)
        {
            options ??= new ExtractionOptions();
            var result = new ExtractionResult();
            var collector = new Collector(result);

            GameFileSource source = GameFileSource.Open(gamePath);
            if (!string.Equals(source.DataPath, gamePath, StringComparison.OrdinalIgnoreCase))
                progress?.Report($"Game data found in {Path.GetFileName(source.DataPath)}");

            progress?.Report("Reading game data...");
            byte[] gameData = source.ReadMainGameData();

            GameData game;
            try
            {
                game = GameDataReader.Read(gameData);
            }
            catch (AgsFormatException ex)
            {
                // Unsupported (2.x) or unknown data: fall back to the heuristic scanner
                result.Warnings.Add($"{ex.Message} Falling back to a heuristic scan, which may miss texts or include technical strings.");
                foreach (string text in LegacyExtraction.Extract(gameData, source, progress))
                    collector.Add(text, TextSource.Script, "");
                return result;
            }

            result.Game = game;
            if (game.Error != null)
                result.Warnings.Add($"Game data could only be read partially: {game.Error}");

            Encoding encoding = game.TextEncoding;
            List<CompiledScript> scripts = game.Scripts;
            if (scripts.Count == 0)
            {
                scripts = ScanForScripts(gameData, encoding);
                if (scripts.Count > 0)
                    result.Warnings.Add($"Scripts were located by signature scan ({scripts.Count} found).");
            }

            progress?.Report("Reading rooms...");
            var rooms = new List<RoomData>();
            foreach (var (name, read) in source.EnumerateRooms())
            {
                try
                {
                    RoomData room = RoomReader.Read(name, read(), encoding);
                    if (room.Error != null)
                        result.Warnings.Add($"{name}: {room.Error}");
                    rooms.Add(room);
                }
                catch (Exception ex) when (ex is IOException or AgsFormatException)
                {
                    result.Warnings.Add($"{name}: {ex.Message}");
                }
            }
            result.RoomCount = rooms.Count;

            // Voice wrappers may be called from any script, so collect voice numbers globally
            var voiceNumbers = new Dictionary<string, int>(StringComparer.Ordinal);
            foreach (CompiledScript script in scripts.Concat(rooms.Select(r => r.Script).OfType<CompiledScript>()))
                foreach (var (text, number) in script.FindVoicedStrings())
                    voiceNumbers.TryAdd(text, number);

            // Speakers are looked up by the literal, so a text said by two characters keeps the first one
            var speakers = new Dictionary<string, string>(StringComparer.Ordinal);
            foreach (CompiledScript script in scripts.Concat(rooms.Select(r => r.Script).OfType<CompiledScript>()))
                foreach (var (text, speaker) in script.FindSpeakers(game.CharacterScriptNames))
                    speakers.TryAdd(text, speaker);

            foreach (CompiledScript script in scripts)
            {
                // The section a text is in names its dialog or script better than the script's last section
                var sections = new Dictionary<string, string>(StringComparer.Ordinal);
                foreach (var (text, section) in script.FindSections())
                    sections.TryAdd(text, DialogName(game, section));
                AddScriptStrings(collector, script, TextSource.Script, sections, script.Name, voiceNumbers, speakers, options, result);
            }

            foreach (DialogInfo dialog in game.Dialogs)
                foreach (string option in dialog.Options)
                    collector.Add(option, TextSource.DialogOption, dialog.ScriptName);
            foreach (GuiText gui in game.GuiTexts.Where(g => g.Kind is GuiTextKind.Button or GuiTextKind.Label))
                collector.Add(gui.Text, TextSource.Gui, gui.ControlName);
            foreach (string name in game.CharacterNames)
                collector.Add(name, TextSource.CharacterName, "");
            foreach (string name in game.InventoryNames.Skip(1)) // slot 0 is unused
                collector.Add(name, TextSource.InventoryItem, "");
            foreach (string message in game.GlobalMessages)
                collector.Add(message, TextSource.GlobalMessage, "");
            foreach (string line in game.OldDialogLines)
                collector.Add(line, TextSource.DialogLine, "");

            foreach (RoomData room in rooms)
            {
                foreach (string name in room.HotspotNames.Where(n => !TextFilters.IsPlaceholderName(n)))
                    collector.Add(name, TextSource.Hotspot, room.Name);
                foreach (string name in room.ObjectNames.Where(n => !TextFilters.IsPlaceholderName(n)))
                    collector.Add(name, TextSource.RoomObject, room.Name);
                foreach (string message in room.Messages)
                    collector.Add(message, TextSource.RoomMessage, room.Name);
                if (room.Script != null)
                    AddScriptStrings(collector, room.Script, TextSource.RoomScript, null, room.Name, voiceNumbers, speakers, options, result);
            }

            return result;
        }

        /// <summary>"Dialog 12" becomes the dialog's script name ("dRosaIntro"); other sections stay as they are.</summary>
        private static string DialogName(GameData game, string section)
        {
            const string prefix = "Dialog ";
            return section.StartsWith(prefix, StringComparison.Ordinal)
                && int.TryParse(section.AsSpan(prefix.Length), out int index)
                && index >= 0 && index < game.Dialogs.Count && game.Dialogs[index].ScriptName.Length > 0
                ? game.Dialogs[index].ScriptName : section;
        }

        private static void AddScriptStrings(Collector collector, CompiledScript script, TextSource source,
            Dictionary<string, string>? sections, string location,
            Dictionary<string, int> voiceNumbers, Dictionary<string, string> speakers, ExtractionOptions options, ExtractionResult result)
        {
            foreach (var (_, text) in script.Strings)
            {
                if (!options.IncludeAllScriptStrings && !TextFilters.IsLikelyDisplayText(text))
                    continue;

                // Wrapper functions look up "&N text"; skip literals that already carry the prefix
                bool alreadyVoiced = text.Length > 1 && text[0] == '&' && char.IsDigit(text[1]);
                speakers.TryGetValue(text, out string? speaker);
                string where = sections != null && sections.TryGetValue(text, out string? section) ? section : location;
                if (!alreadyVoiced && voiceNumbers.TryGetValue(text, out int number))
                {
                    if (collector.Add($"&{number} {text}", source, where, speaker))
                        result.VoicedCount++;
                }
                else
                {
                    collector.Add(text, source, where, speaker);
                }
            }
        }

        /// <summary>Finds scripts by signature when the game data could not be read up to the scripts.</summary>
        internal static List<CompiledScript> ScanForScripts(byte[] data, Encoding encoding)
        {
            var scripts = new List<CompiledScript>();
            using var reader = new AgsReader(new MemoryStream(data, writable: false), encoding);
            for (int offset = data.AsSpan().IndexOf("SCOM"u8); offset >= 0;)
            {
                try
                {
                    reader.Position = offset;
                    scripts.Add(CompiledScript.Read(reader));
                }
                catch (AgsFormatException)
                {
                    // not a script, just the same bytes
                }
                int next = data.AsSpan(offset + 4).IndexOf("SCOM"u8);
                offset = next < 0 ? -1 : offset + 4 + next;
            }
            return scripts;
        }

        /// <summary>Collects unique, storable texts in order.</summary>
        private sealed class Collector
        {
            private readonly ExtractionResult _result;
            private readonly HashSet<string> _seen = new(StringComparer.Ordinal);

            public Collector(ExtractionResult result) => _result = result;

            public bool Add(string text, TextSource source, string location, string? speaker = null)
            {
                if (!TextFilters.IsStorable(text) || !TextFilters.HasLetters(text) || !_seen.Add(text))
                    return false;
                _result.Texts.Add(new ExtractedText(text, source, location, speaker));
                return true;
            }
        }
    }
}
