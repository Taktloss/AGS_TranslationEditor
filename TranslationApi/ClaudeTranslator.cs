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
    public sealed class ClaudeTranslator : IMachineTranslator, ILanguageModel
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
            string json = await CompleteJsonAsync(BuildSystemPrompt(options, texts), BuildUserMessage(texts), Schemas.Translations, cancellationToken).ConfigureAwait(false);
            return ParseResponse(json, texts.Count);
        }

        public async Task<string> CompleteJsonAsync(string systemPrompt, string userMessage, IReadOnlyDictionary<string, JsonElement> schema,
            CancellationToken cancellationToken = default)
        {
            var parameters = new MessageCreateParams
            {
                Model = _model,
                MaxTokens = 16000,
                // A refused request is retried server-side on a model chosen for the refusal category.
                Betas = [AnthropicBeta.ServerSideFallback2026_07_01],
                Fallbacks = new Default(),
                System = systemPrompt,
                OutputConfig = new BetaOutputConfig
                {
                    Effort = Effort.Medium,
                    Format = new BetaJsonOutputFormat { Schema = schema.ToDictionary(kv => kv.Key, kv => kv.Value) },
                },
                Messages = [new BetaUserMessageParam { Content = userMessage }],
            };

            BetaMessage response;
            try
            {
                response = await _client.Beta.Messages.Create(parameters, cancellationToken).ConfigureAwait(false);
            }
            catch (Exception ex) when (cancellationToken.IsCancellationRequested && ex is not OperationCanceledException)
            {
                // The SDK may report a cancelled request as an I/O error
                throw new OperationCanceledException("Cancelled.", ex, cancellationToken);
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
                throw new MachineTranslationException("Claude declined to answer for these lines.");
            if (response.StopReason == "max_tokens")
                throw new MachineTranslationException("Claude's answer was cut off; the lines are too long for one request.");
            return string.Concat(response.Content.Select(b => b.Value).OfType<BetaTextBlock>().Select(t => t.Text));
        }

        internal static string BuildSystemPrompt(MachineTranslationOptions options, IReadOnlyList<string> texts) =>
            TranslationPrompts.TranslateSystem(options, texts);

        internal static string BuildUserMessage(IReadOnlyList<string> texts) => TranslationPrompts.LinesMessage(texts);

        internal static IReadOnlyList<string> ParseResponse(string json, int expectedCount) =>
            TranslationPrompts.ParseTranslations(json, expectedCount, "Claude");
    }
}
