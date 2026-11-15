using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Text.Json;

namespace TranslationApi
{
    /// <summary>Prompts and answer parsing shared by all language models.</summary>
    internal static class TranslationPrompts
    {
        private static readonly JsonSerializerOptions Indented = new()
        {
            WriteIndented = true,
            Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
        };

        public static string TranslateSystem(MachineTranslationOptions options, IReadOnlyList<string> texts)
        {
            var sb = new StringBuilder();
            sb.AppendLine($"You translate the dialogue and interface texts of {Game(options)} from {MachineTranslationOptions.LanguageName(options.SourceLanguage)} into {MachineTranslationOptions.LanguageName(options.TargetLanguage)}.");
            sb.AppendLine("The lines are in game order; neighbouring lines are often part of the same conversation, so use them as context. Translate each line on its own though: the answer needs exactly one translation per line, in the same order.");
            sb.AppendLine("Write natural, spoken language that fits the character and the situation. Short interface texts (menu items, buttons, inventory names) must stay short.");
            sb.AppendLine();
            AppendRules(sb, options, texts);
            return sb.ToString();
        }

        public static string ReviewSystem(MachineTranslationOptions options, IReadOnlyList<string> originals)
        {
            var sb = new StringBuilder();
            sb.AppendLine($"You review the {MachineTranslationOptions.LanguageName(options.TargetLanguage)} translation of {Game(options)} (source language: {MachineTranslationOptions.LanguageName(options.SourceLanguage)}).");
            sb.AppendLine("Every item has an id, the original and the translation. The items are in game order, so neighbouring lines give context.");
            sb.AppendLine("Report only real problems: wrong meaning, missing or added content, grammar and spelling mistakes, unnatural wording, an inconsistent form of address, or broken markup. Do not report matters of taste.");
            sb.AppendLine("For each problem give the id, a short description in the language of the translation, and a corrected translation. Leave out items without problems; an empty list is a fine answer.");
            sb.AppendLine();
            AppendRules(sb, options, originals);
            return sb.ToString();
        }

        public static string ShortenSystem(MachineTranslationOptions options)
        {
            var sb = new StringBuilder();
            sb.AppendLine($"Translations of {Game(options)} into {MachineTranslationOptions.LanguageName(options.TargetLanguage)} are too long for the space the game has for them (buttons, labels, short lines).");
            sb.AppendLine("Every item has an id, the original, the current translation and the maximum number of characters. Write a shorter translation for each item that keeps the meaning and does not exceed the maximum. Abbreviate only if there is no other way.");
            sb.AppendLine("Keep \"[\" line breaks, placeholders like %s and %d and tokens like @SCORE@ unchanged. Give exactly one result per item.");
            AppendFormality(sb, options);
            return sb.ToString();
        }

        private static string Game(MachineTranslationOptions options) =>
            options.GameTitle.Length > 0 ? $"the adventure game \"{options.GameTitle}\"" : "an adventure game";

        private static void AppendRules(StringBuilder sb, MachineTranslationOptions options, IReadOnlyList<string> texts)
        {
            sb.AppendLine("The lines contain engine markup that must stay intact:");
            sb.AppendLine("- \"[\" is a line break. Keep the same number of line breaks at matching places.");
            sb.AppendLine("- \"\\[\" is a literal bracket; keep it as it is.");
            sb.AppendLine("- Placeholders like %s, %d, %02d and tokens like @SCORE@ are filled in by the game. Keep every one of them unchanged, in an order that still makes sense.");
            sb.AppendLine("- Names of characters and places stay as they are unless the glossary says otherwise.");
            AppendFormality(sb, options);

            var terms = options.Glossary?.Terms.Where(t => texts.Any(text => text.Contains(t.Original, StringComparison.OrdinalIgnoreCase))).ToList();
            if (terms is { Count: > 0 })
            {
                sb.AppendLine();
                sb.AppendLine("Glossary (use these translations):");
                foreach (var term in terms)
                    sb.AppendLine($"- {term.Original} = {string.Join(" / ", term.Translations)}");
            }
        }

        private static void AppendFormality(StringBuilder sb, MachineTranslationOptions options)
        {
            switch (options.Formality)
            {
                case Formality.Informal:
                    sb.AppendLine("- Use the informal form of address (for German: du/dich/dein).");
                    break;
                case Formality.Formal:
                    sb.AppendLine("- Use the formal form of address (for German: Sie/Ihnen/Ihr).");
                    break;
            }
        }

        public static string LinesMessage(IReadOnlyList<string> texts) => JsonSerializer.Serialize(new { lines = texts }, Indented);

        public static string ItemsMessage(object items) => JsonSerializer.Serialize(new { items }, Indented);

        public static IReadOnlyList<string> ParseTranslations(string json, int expectedCount, string service)
        {
            List<string>? translations;
            try
            {
                using JsonDocument doc = JsonDocument.Parse(StripCodeFence(json));
                translations = doc.RootElement.GetProperty("translations").EnumerateArray().Select(e => e.GetString() ?? "").ToList();
            }
            catch (Exception ex) when (ex is JsonException or KeyNotFoundException or InvalidOperationException)
            {
                throw new MachineTranslationException($"{service}'s answer is not in the expected format.", ex);
            }
            if (translations.Count != expectedCount)
                throw new MachineTranslationException($"{service} returned {translations.Count} translations for {expectedCount} lines.");
            return translations;
        }

        /// <summary>Models without a JSON mode sometimes wrap the answer in ```json ... ```.</summary>
        public static string StripCodeFence(string text)
        {
            string t = text.Trim();
            if (!t.StartsWith("```", StringComparison.Ordinal))
                return t;
            int start = t.IndexOf('\n');
            int end = t.LastIndexOf("```", StringComparison.Ordinal);
            return start >= 0 && end > start ? t[(start + 1)..end].Trim() : t;
        }
    }
}
