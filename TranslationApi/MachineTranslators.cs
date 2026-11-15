using System;

namespace TranslationApi
{
    /// <summary>Keys, models and addresses of the services; empty values mean "not set".</summary>
    public sealed record ServiceSettings
    {
        public string? DeepLKey { get; init; }
        public string? ClaudeKey { get; init; }
        public string? ClaudeModel { get; init; }
        /// <summary>Base address of an OpenAI-compatible API, e.g. http://localhost:11434/v1 for Ollama.</summary>
        public string? OpenAiUrl { get; init; }
        public string? OpenAiKey { get; init; }
        public string? OpenAiModel { get; init; }
    }

    /// <summary>
    /// Creates the configured service. Missing keys are taken from DEEPL_AUTH_KEY, ANTHROPIC_API_KEY or OPENAI_API_KEY;
    /// a missing OpenAI-compatible address and model from OPENAI_BASE_URL and OPENAI_MODEL.
    /// </summary>
    public static class MachineTranslators
    {
        public const string Google = "Google";
        public const string DeepL = "DeepL";
        public const string Claude = "Claude";
        public const string OpenAi = "OpenAI";

        public static readonly string[] Names = { Google, DeepL, Claude, OpenAi };

        /// <summary>The services that are language models and can also review and shorten texts.</summary>
        public static bool IsLanguageModel(string provider) =>
            provider.Equals(Claude, StringComparison.OrdinalIgnoreCase) || provider.Equals(OpenAi, StringComparison.OrdinalIgnoreCase);

        public static IMachineTranslator Create(string provider, string? deepLKey = null, string? claudeKey = null, string? claudeModel = null) =>
            Create(provider, new ServiceSettings { DeepLKey = deepLKey, ClaudeKey = claudeKey, ClaudeModel = claudeModel });

        public static IMachineTranslator Create(string provider, ServiceSettings settings)
        {
            if (provider.Equals(DeepL, StringComparison.OrdinalIgnoreCase))
                return new DeepLTranslator(Key(settings.DeepLKey, "DEEPL_AUTH_KEY", DeepL));
            if (IsLanguageModel(provider))
                return (IMachineTranslator)CreateLanguageModel(provider, settings);
            if (provider.Equals(Google, StringComparison.OrdinalIgnoreCase))
                return new GoogleTranslator();
            throw new MachineTranslationException($"Unknown translation service \"{provider}\" (use {string.Join(", ", Names)}).");
        }

        /// <exception cref="MachineTranslationException">The service is not a language model, or a key or model is missing.</exception>
        public static ILanguageModel CreateLanguageModel(string provider, ServiceSettings settings)
        {
            if (provider.Equals(Claude, StringComparison.OrdinalIgnoreCase))
                return new ClaudeTranslator(Key(settings.ClaudeKey, "ANTHROPIC_API_KEY", Claude), settings.ClaudeModel);
            if (provider.Equals(OpenAi, StringComparison.OrdinalIgnoreCase))
            {
                string? url = Value(settings.OpenAiUrl, "OPENAI_BASE_URL");
                string? key = Value(settings.OpenAiKey, "OPENAI_API_KEY");
                string? model = Value(settings.OpenAiModel, "OPENAI_MODEL");
                bool local = url != null && Uri.TryCreate(url, UriKind.Absolute, out Uri? uri) && uri.IsLoopback;
                if (key == null && !local && (url == null || url.Contains("openai.com", StringComparison.OrdinalIgnoreCase)))
                    throw new MachineTranslationException("OpenAI needs an API key: enter it in the settings or set OPENAI_API_KEY. Local servers (Ollama, LM Studio) need none.");
                return new OpenAiCompatibleTranslator(url, key, model ?? "");
            }
            throw new MachineTranslationException($"{provider} is not a language model; reviewing and shortening need {Claude} or {OpenAi} (also for local models).");
        }

        private static string? Value(string? value, string variable)
        {
            if (!string.IsNullOrWhiteSpace(value))
                return value.Trim();
            string? fromEnvironment = Environment.GetEnvironmentVariable(variable);
            return string.IsNullOrWhiteSpace(fromEnvironment) ? null : fromEnvironment.Trim();
        }

        private static string Key(string? key, string variable, string provider) =>
            Value(key, variable) ?? throw new MachineTranslationException($"{provider} needs an API key: enter it in the settings or set {variable}.");
    }
}
