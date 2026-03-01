using System;
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
        public double WindowWidth { get; set; } = 1100;
        public double WindowHeight { get; set; } = 700;
        public double WindowX { get; set; } = -1;
        public double WindowY { get; set; } = -1;
        public double SplitterPosition { get; set; } = 130;

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
            }
            catch { }
        }
    }
}
