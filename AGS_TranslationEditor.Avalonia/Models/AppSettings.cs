using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;

namespace AGS_TranslationEditor.Models
{
    public class AppSettings
    {
        public int FontSize { get; set; } = 13;
        public bool MonospaceFont { get; set; } = true;
        public string Encoding { get; set; } = "Latin-1";
        public string Theme { get; set; } = "Dark";
        /// <summary>UI language: "System", "en" or "de" (see Loc).</summary>
        public string UiLanguage { get; set; } = "System";
        /// <summary>Show who says each text and where it comes from (context file next to the TRS).</summary>
        public bool ShowContext { get; set; }
        public double WindowWidth { get; set; } = 1100;
        public double WindowHeight { get; set; } = 700;
        public double WindowX { get; set; } = -1;
        public double WindowY { get; set; } = -1;
        public double SplitterPosition { get; set; } = 170;
        /// <summary>Widths of the table columns #, source, translation, context; source and translation as
        /// shares of the remaining width (star), the others in pixels. Empty = defaults.</summary>
        public double[] ColumnWidths { get; set; } = Array.Empty<double>();

        // Machine translation
        public string MtProvider { get; set; } = "Google";
        public string MtSourceLanguage { get; set; } = "en";
        public string MtTargetLanguage { get; set; } = "de";
        public string MtFormality { get; set; } = "Default";

        public static TranslationApi.Formality ParseFormality(string value) =>
            Enum.TryParse(value, out TranslationApi.Formality formality) ? formality : TranslationApi.Formality.Default;
        public string DeepLApiKey { get; set; } = "";
        public string ClaudeApiKey { get; set; } = "";
        public string ClaudeModel { get; set; } = "";
        /// <summary>Hunspell dictionary for the spell check: "" = the one for the target language, "off", or a name like "de_DE".</summary>
        public string SpellDictionary { get; set; } = "";

        /// <summary>LanguageTool server for the grammar check; empty = not used.</summary>
        public string LanguageToolUrl { get; set; } = "";

        /// <summary>Live mode: draw the selected text with a game font (off by default).</summary>
        public bool LivePreview { get; set; }
        /// <summary>Game file whose fonts the live preview uses.</summary>
        public string LivePreviewGame { get; set; } = "";
        /// <summary>Font the user chose for the live preview; -1 = the game's speech font. (The older
        /// "LivePreviewFont" also stored automatic choices and is no longer read.)</summary>
        public int LivePreviewChosenFont { get; set; } = -1;

        /// <summary>Live preview wraps long lines, at the width set for the game (<see cref="LivePreviewWrapWidths"/>) or estimated.</summary>
        public bool LivePreviewWrap { get; set; }
        /// <summary>Wrap width in game pixels per game file, where the user set one.</summary>
        public Dictionary<string, int> LivePreviewWrapWidths { get; set; } = new();

        public string OpenAiUrl { get; set; } = "";
        public string OpenAiApiKey { get; set; } = "";
        public string OpenAiModel { get; set; } = "";

        /// <summary>Keys, models and addresses for <see cref="TranslationApi.MachineTranslators"/>.</summary>
        public TranslationApi.ServiceSettings Services => new()
        {
            DeepLKey = DeepLApiKey,
            ClaudeKey = ClaudeApiKey,
            ClaudeModel = ClaudeModel,
            OpenAiUrl = OpenAiUrl,
            OpenAiKey = OpenAiApiKey,
            OpenAiModel = OpenAiModel,
        };

        public static string ConfigPath => Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            "AGSTranslationEditor", "settings.json");

        public static AppSettings Load()
        {
            try
            {
                if (File.Exists(ConfigPath))
                {
                    var json = File.ReadAllText(ConfigPath);
                    return JsonSerializer.Deserialize<AppSettings>(json) ?? new AppSettings();
                }
            }
            catch { }
            return new AppSettings();
        }

        public void Save()
        {
            try
            {
                var dir = Path.GetDirectoryName(ConfigPath)!;
                Directory.CreateDirectory(dir);
                var json = JsonSerializer.Serialize(this, new JsonSerializerOptions { WriteIndented = true });
                File.WriteAllText(ConfigPath, json);
                // The file may hold API keys: readable only by the user
                if (!OperatingSystem.IsWindows())
                    File.SetUnixFileMode(ConfigPath, UnixFileMode.UserRead | UnixFileMode.UserWrite);
            }
            catch { }
        }
    }
}
