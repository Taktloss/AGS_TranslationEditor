using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text.Json;
using Avalonia.Markup.Xaml;

namespace AGSFontEditor.Localization
{
    /// <summary>
    /// UI language of the translator and the font editor. The English text is the key; other
    /// languages map it to their text. Missing entries fall back to English. The language is
    /// chosen once at startup, before any window is created.
    /// </summary>
    public static class Loc
    {
        public const string System = "System";
        public const string English = "en";
        public const string German = "de";

        private static IReadOnlyDictionary<string, string>? _strings;

        /// <summary>The active language: "en" or "de".</summary>
        public static string Language { get; private set; } = English;

        /// <summary>Selects "en", "de" or "System" (the operating system's UI language).</summary>
        public static void SetLanguage(string? language)
        {
            string code = string.IsNullOrEmpty(language) || language == System
                ? CultureInfo.CurrentUICulture.TwoLetterISOLanguageName
                : language;
            Language = code == German ? German : English;
            _strings = Language == German ? StringsDe.Table : null;
        }

        /// <summary>
        /// The language saved by the translation editor (its settings.json), so the stand-alone font
        /// editor uses the same one; "System" if there is none.
        /// </summary>
        public static string ReadSavedLanguage()
        {
            try
            {
                string path = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
                    "AGSTranslationEditor", "settings.json");
                using var json = JsonDocument.Parse(File.ReadAllText(path));
                return json.RootElement.TryGetProperty("UiLanguage", out var value) && value.ValueKind == JsonValueKind.String
                    ? value.GetString()! : System;
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException)
            {
                return System;
            }
        }

        /// <summary>The text for the English <paramref name="text"/> in the active language.</summary>
        public static string T(string text) =>
            _strings != null && _strings.TryGetValue(text, out string? translated) ? translated : text;

        /// <summary><see cref="T"/> with string.Format placeholders ({0}, {1}, ...).</summary>
        public static string F(string format, params object?[] args) =>
            string.Format(CultureInfo.CurrentCulture, T(format), args);
    }

    /// <summary>XAML: <c>Header="{l:T '_File'}"</c>.</summary>
    public sealed class TExtension : MarkupExtension
    {
        public TExtension(string text) => Text = text;

        public string Text { get; set; }

        public override object ProvideValue(IServiceProvider serviceProvider) => Loc.T(Text);
    }
}
