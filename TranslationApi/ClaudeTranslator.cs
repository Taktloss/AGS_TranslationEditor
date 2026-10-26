using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Anthropic;
using Anthropic.Exceptions;
using Anthropic.Models.Beta;
using Anthropic.Models.Beta.Messages;

namespace TranslationApi
{
    /// <summary>
    /// Claude (Anthropic API). Gets lines in game order with the game title, the target formality and
    /// matching glossary terms, and answers with structured JSON so every line maps back to its entry.
    /// </summary>
    public sealed class ClaudeTranslator : IMachineTranslator
    {
        public const string DefaultModel = "claude-opus-5-5";

        private readonly AnthropicClient _client;
        private readonly string _model;

        public ClaudeTranslator(string apiKey, string? model = null, HttpMessageHandler? handler = null)
        {
            _client = handler != null
                ? new AnthropicClient { ApiKey = apiKey.Trim(), HttpClient = new HttpClient(handler), MaxRetries = 0 }
                : new AnthropicClient { ApiKey = apiKey.Trim() };
            _model = string.IsNullOrWhiteSpace(model) ? DefaultModel : model.Trim();
        }

        public string Name => "Claude";
        public int MaxBatchSize => 40;

        public async Task<IReadOnlyList<string>> TranslateAsync(IReadOnlyList<string> texts, MachineTranslationOptions options, CancellationToken cancellationToken = default)
        {
            var parameters = new MessageCreateParams
            {
                Model = _model,
                MaxTokens = 16000,
                // A refused request is retried server-side on a model chosen for the refusal category.
                Betas = [AnthropicBeta.ServerSideFallback2026_07_01],
                Fallbacks = new Default(),
                System = BuildSystemPrompt(options, texts),
                OutputConfig = new BetaOutputConfig
                {
                    Effort = Effort.Medium,
                    Format = new BetaJsonOutputFormat { Schema = ResponseSchema },
                },
                Messages = [new BetaUserMessageParam { Content = BuildUserMessage(texts) }],
            };

            BetaMessage response;
            try
            {
                response = await _client.Beta.Messages.Create(parameters, cancellationToken).ConfigureAwait(false);
            }
            catch (AnthropicUnauthorizedException ex)
            {
                throw new MachineTranslationException("The Anthropic API rejected the API key.", ex);
            }
            catch (AnthropicNotFoundException ex)
            {
                throw new MachineTranslationException($"Unknown Claude model \"{_model}\".", ex);
            }
            catch (AnthropicRateLimitException ex)
            {
                throw new MachineTranslationException("Too many requests to the Anthropic API; try again in a moment.", ex);
            }
            catch (AnthropicApiException ex)
            {
                throw new MachineTranslationException($"Anthropic API error: {ex.Message}", ex);
            }
            catch (AnthropicIOException ex)
            {
                throw new MachineTranslationException($"The Anthropic API is not reachable: {ex.Message}", ex);
            }

            if (response.StopReason == "refusal")
                throw new MachineTranslationException("Claude declined to translate these lines.");
            if (response.StopReason == "max_tokens")
                throw new MachineTranslationException("Claude's answer was cut off; the lines are too long for one request.");
            string json = string.Concat(response.Content.Select(b => b.Value).OfType<BetaTextBlock>().Select(t => t.Text));
            return ParseResponse(json, texts.Count);
        }

        internal static string BuildSystemPrompt(MachineTranslationOptions options, IReadOnlyList<string> texts)
        {
            var sb = new StringBuilder();
            string game = options.GameTitle.Length > 0 ? $"the adventure game \"{options.GameTitle}\"" : "an adventure game";
            sb.AppendLine($"You translate the dialogue and interface texts of {game} from {MachineTranslationOptions.LanguageName(options.SourceLanguage)} into {MachineTranslationOptions.LanguageName(options.TargetLanguage)}.");
            sb.AppendLine("The lines are in game order; neighbouring lines are often part of the same conversation, so use them as context. Translate each line on its own though: the answer needs exactly one translation per line, in the same order.");
            sb.AppendLine("Write natural, spoken language that fits the character and the situation. Short interface texts (menu items, buttons, inventory names) must stay short.");
            sb.AppendLine();
            sb.AppendLine("The lines contain engine markup that must stay intact:");
            sb.AppendLine("- \"[\" is a line break. Keep the same number of line breaks at matching places.");
            sb.AppendLine("- \"\\[\" is a literal bracket; keep it as it is.");
            sb.AppendLine("- Placeholders like %s, %d, %02d and tokens like @SCORE@ are filled in by the game. Keep every one of them unchanged, in an order that still makes sense.");
            sb.AppendLine("- Names of characters and places stay as they are unless the glossary says otherwise.");
            switch (options.Formality)
            {
                case Formality.Informal:
                    sb.AppendLine("- Use the informal form of address (for German: du/dich/dein).");
                    break;
                case Formality.Formal:
                    sb.AppendLine("- Use the formal form of address (for German: Sie/Ihnen/Ihr).");
                    break;
            }

            var terms = options.Glossary?.Terms.Where(t => texts.Any(text => text.Contains(t.Original, StringComparison.OrdinalIgnoreCase))).ToList();
            if (terms is { Count: > 0 })
            {
                sb.AppendLine();
                sb.AppendLine("Glossary (use these translations):");
                foreach (var term in terms)
                    sb.AppendLine($"- {term.Original} = {string.Join(" / ", term.Translations)}");
            }
            return sb.ToString();
        }

        internal static string BuildUserMessage(IReadOnlyList<string> texts) =>
            JsonSerializer.Serialize(new { lines = texts }, new JsonSerializerOptions { WriteIndented = true, Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping });

        internal static IReadOnlyList<string> ParseResponse(string json, int expectedCount)
        {
            List<string>? translations;
            try
            {
                using JsonDocument doc = JsonDocument.Parse(json);
                translations = doc.RootElement.GetProperty("translations").EnumerateArray().Select(e => e.GetString() ?? "").ToList();
            }
            catch (Exception ex) when (ex is JsonException or KeyNotFoundException or InvalidOperationException)
            {
                throw new MachineTranslationException("Claude's answer is not in the expected format.", ex);
            }
            if (translations.Count != expectedCount)
                throw new MachineTranslationException($"Claude returned {translations.Count} translations for {expectedCount} lines.");
            return translations;
        }

        private static readonly Dictionary<string, JsonElement> ResponseSchema = new()
        {
            ["type"] = JsonSerializer.SerializeToElement("object"),
            ["properties"] = JsonSerializer.SerializeToElement(new { translations = new { type = "array", items = new { type = "string" } } }),
            ["required"] = JsonSerializer.SerializeToElement(new[] { "translations" }),
            ["additionalProperties"] = JsonSerializer.SerializeToElement(false),
        };
    }
}
