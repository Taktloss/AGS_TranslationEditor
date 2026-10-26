using System;

namespace TranslationApi
{
    /// <summary>Creates the configured service. Missing keys are taken from DEEPL_AUTH_KEY or ANTHROPIC_API_KEY.</summary>
    public static class MachineTranslators
    {
        public const string Google = "Google";
        public const string DeepL = "DeepL";
        public const string Claude = "Claude";

        public static readonly string[] Names = { Google, DeepL, Claude };

        public static IMachineTranslator Create(string provider, string? deepLKey = null, string? claudeKey = null, string? claudeModel = null)
        {
            if (provider.Equals(DeepL, StringComparison.OrdinalIgnoreCase))
                return new DeepLTranslator(Key(deepLKey, "DEEPL_AUTH_KEY", DeepL));
            if (provider.Equals(Claude, StringComparison.OrdinalIgnoreCase))
                return new ClaudeTranslator(Key(claudeKey, "ANTHROPIC_API_KEY", Claude), claudeModel);
            if (provider.Equals(Google, StringComparison.OrdinalIgnoreCase))
                return new GoogleTranslator();
            throw new MachineTranslationException($"Unknown translation service \"{provider}\" (use {string.Join(", ", Names)}).");
        }

        private static string Key(string? key, string variable, string provider)
        {
            if (!string.IsNullOrWhiteSpace(key))
                return key;
            string? fromEnvironment = Environment.GetEnvironmentVariable(variable);
            return !string.IsNullOrWhiteSpace(fromEnvironment)
                ? fromEnvironment
                : throw new MachineTranslationException($"{provider} needs an API key: enter it in the settings or set {variable}.");
        }
    }
}
