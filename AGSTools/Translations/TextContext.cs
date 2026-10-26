using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace AGSTools
{
    /// <summary>Where a text of a translation comes from and who says it.</summary>
    public sealed record TextOrigin(string Source, string Location, string? Speaker)
    {
        /// <summary>Short form for a list column, e.g. "Rosa · room12.crm".</summary>
        public override string ToString()
        {
            string where = Location.Length > 0 ? Location : Source;
            return Speaker != null ? $"{Speaker} · {where}" : where;
        }
    }

    /// <summary>
    /// Optional companion file of a translation ("Game.context.json" next to "Game.trs") with the origin of
    /// every text: source, room or script, and the speaking character. The TRS itself stays unchanged, so
    /// the game and other tools never see this file.
    /// </summary>
    public static class TextContext
    {
        /// <summary>Speaker shown for texts displayed by Display() rather than said by a character.</summary>
        public const string Narration = "(Display)";

        private static readonly JsonSerializerOptions JsonOptions = new()
        {
            WriteIndented = true,
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
            DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
            Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
        };

        /// <summary>The context file belonging to a translation file.</summary>
        public static string PathFor(string translationPath) =>
            Path.Combine(Path.GetDirectoryName(translationPath) ?? "", Path.GetFileNameWithoutExtension(translationPath) + ".context.json");

        /// <summary>Origins of the extracted texts, keyed like the TRS entries. Speakers get their character names.</summary>
        public static Dictionary<string, TextOrigin> FromExtraction(ExtractionResult extraction)
        {
            var names = new Dictionary<string, string>(StringComparer.Ordinal);
            if (extraction.Game != null)
            {
                var game = extraction.Game;
                for (int i = 0; i < game.CharacterScriptNames.Count && i < game.CharacterNames.Count; i++)
                    if (game.CharacterScriptNames[i].Length > 0 && game.CharacterNames[i].Trim().Length > 0)
                        names.TryAdd(game.CharacterScriptNames[i], game.CharacterNames[i].Trim());
            }

            var origins = new Dictionary<string, TextOrigin>(StringComparer.Ordinal);
            foreach (ExtractedText text in extraction.Texts)
            {
                string? speaker = text.Speaker switch
                {
                    null => null,
                    CompiledScript.Narration => Narration,
                    _ => names.TryGetValue(text.Speaker, out string? name) ? name : text.Speaker,
                };
                origins.TryAdd(TextEscaping.ToTrs(text.Text), new TextOrigin(text.Source.ToString(), text.Location, speaker));
            }
            return origins;
        }

        public static void Write(string path, IReadOnlyDictionary<string, TextOrigin> origins)
        {
            var file = new ContextFile();
            foreach (var (text, origin) in origins)
                file.Texts.Add(new ContextEntry { Text = text, Source = origin.Source, Location = origin.Location, Speaker = origin.Speaker });
            string temp = path + ".tmp";
            File.WriteAllText(temp, JsonSerializer.Serialize(file, JsonOptions));
            File.Move(temp, path, overwrite: true);
        }

        /// <summary>Reads a context file; null if there is none or it cannot be read.</summary>
        public static Dictionary<string, TextOrigin>? Read(string path)
        {
            if (!File.Exists(path))
                return null;
            try
            {
                var file = JsonSerializer.Deserialize<ContextFile>(File.ReadAllText(path), JsonOptions);
                if (file == null)
                    return null;
                var origins = new Dictionary<string, TextOrigin>(StringComparer.Ordinal);
                foreach (ContextEntry entry in file.Texts)
                    if (entry.Text != null)
                        origins.TryAdd(entry.Text, new TextOrigin(entry.Source ?? "", entry.Location ?? "", entry.Speaker));
                return origins;
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException)
            {
                return null;
            }
        }

        private sealed class ContextFile
        {
            public int Version { get; set; } = 1;
            public List<ContextEntry> Texts { get; set; } = new();
        }

        private sealed class ContextEntry
        {
            public string? Text { get; set; }
            public string? Source { get; set; }
            public string? Location { get; set; }
            public string? Speaker { get; set; }
        }
    }
}
