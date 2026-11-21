using System;
using System.Collections.Generic;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

namespace TranslationApi
{
    /// <summary>
    /// Any service with an OpenAI-style chat completions API: OpenAI, OpenRouter, Mistral, and local servers such as
    /// Ollama or LM Studio. Asks for an answer following a JSON schema; servers that do not support that get the
    /// schema in the prompt instead.
    /// </summary>
    public sealed class OpenAiCompatibleTranslator : IMachineTranslator, ILanguageModel
    {
        public const string DefaultUrl = "https://api.openai.com/v1";

        /// <summary>Well-known endpoints, for the settings.</summary>
        public static readonly IReadOnlyList<(string Name, string Url)> Presets = new[]
        {
            ("OpenAI", DefaultUrl),
            ("OpenRouter", "https://openrouter.ai/api/v1"),
            ("Mistral", "https://api.mistral.ai/v1"),
            ("Ollama", "http://localhost:11434/v1"),
            ("LM Studio", "http://localhost:1234/v1"),
        };

        internal enum JsonMode { Schema, JsonObject, PromptOnly }

        private readonly HttpClient _http;
        private readonly Uri _endpoint;
        private readonly string? _apiKey;
        private readonly string _model;
        private JsonMode _mode = JsonMode.Schema;

        /// <param name="baseUrl">API base, e.g. https://api.openai.com/v1 or http://localhost:11434/v1.</param>
        /// <param name="apiKey">Bearer key; local servers usually need none.</param>
        public OpenAiCompatibleTranslator(string? baseUrl, string? apiKey, string model, HttpMessageHandler? handler = null)
        {
            if (string.IsNullOrWhiteSpace(model))
                throw new MachineTranslationException("The OpenAI-compatible service needs a model name (e.g. gpt-5-mini, or llama3.3 for Ollama).");
            string url = string.IsNullOrWhiteSpace(baseUrl) ? DefaultUrl : baseUrl.Trim().TrimEnd('/');
            if (url.EndsWith("/chat/completions", StringComparison.OrdinalIgnoreCase))
                url = url[..^"/chat/completions".Length];
            if (!Uri.TryCreate(url + "/chat/completions", UriKind.Absolute, out Uri? endpoint) || endpoint.Scheme is not ("http" or "https"))
                throw new MachineTranslationException($"\"{baseUrl}\" is not a valid API address (e.g. {DefaultUrl}).");
            _endpoint = endpoint;
            _apiKey = string.IsNullOrWhiteSpace(apiKey) ? null : apiKey.Trim();
            _model = model.Trim();
            // Local models can take minutes for a batch
            _http = handler != null ? new HttpClient(handler) : new HttpClient();
            _http.Timeout = TimeSpan.FromMinutes(10);
        }

        public string Name => $"{_model} ({_endpoint.Host})";
        public int MaxBatchSize => 25;

        public async Task<IReadOnlyList<string>> TranslateAsync(IReadOnlyList<string> texts, MachineTranslationOptions options, CancellationToken cancellationToken = default)
        {
            string json = await CompleteJsonAsync(TranslationPrompts.TranslateSystem(options, texts), TranslationPrompts.LinesMessage(texts, options),
                Schemas.Translations, cancellationToken).ConfigureAwait(false);
            return TranslationPrompts.ParseTranslations(json, texts.Count, _model);
        }

        public async Task<string> CompleteJsonAsync(string systemPrompt, string userMessage, IReadOnlyDictionary<string, JsonElement> schema,
            CancellationToken cancellationToken = default)
        {
            while (true)
            {
                (HttpStatusCode status, string body) = await PostAsync(BuildRequest(systemPrompt, userMessage, schema, _mode), cancellationToken).ConfigureAwait(false);
                // Servers without structured output reject response_format: fall back step by step
                if (status == HttpStatusCode.BadRequest && _mode != JsonMode.PromptOnly && MentionsResponseFormat(body))
                {
                    _mode++;
                    continue;
                }
                if (status != HttpStatusCode.OK)
                    throw new MachineTranslationException(ErrorMessage(status, body));
                return ParseAnswer(body);
            }
        }

        internal string BuildRequest(string systemPrompt, string userMessage, IReadOnlyDictionary<string, JsonElement> schema, JsonMode mode)
        {
            string system = mode == JsonMode.Schema
                ? systemPrompt
                : systemPrompt + "\n\nAnswer with a single JSON object and nothing else. It must follow this JSON schema:\n" + JsonSerializer.Serialize(schema);
            var request = new Dictionary<string, object>
            {
                ["model"] = _model,
                ["messages"] = new[]
                {
                    new { role = "system", content = system },
                    new { role = "user", content = userMessage },
                },
            };
            if (mode == JsonMode.Schema)
                request["response_format"] = new { type = "json_schema", json_schema = new { name = "answer", strict = true, schema } };
            else if (mode == JsonMode.JsonObject)
                request["response_format"] = new { type = "json_object" };
            return JsonSerializer.Serialize(request);
        }

        private async Task<(HttpStatusCode, string)> PostAsync(string json, CancellationToken cancellationToken)
        {
            using var request = new HttpRequestMessage(HttpMethod.Post, _endpoint) { Content = new StringContent(json, Encoding.UTF8, "application/json") };
            if (_apiKey != null)
                request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", _apiKey);
            try
            {
                using HttpResponseMessage response = await _http.SendAsync(request, cancellationToken).ConfigureAwait(false);
                return (response.StatusCode, await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false));
            }
            catch (HttpRequestException ex)
            {
                throw new MachineTranslationException($"{_endpoint.GetLeftPart(UriPartial.Authority)} is not reachable: {ex.Message}", ex);
            }
            catch (TaskCanceledException ex) when (!cancellationToken.IsCancellationRequested)
            {
                throw new MachineTranslationException($"{_endpoint.Host} did not answer in time.", ex);
            }
        }

        private static bool MentionsResponseFormat(string body) =>
            body.Contains("response_format", StringComparison.OrdinalIgnoreCase)
            || body.Contains("json_schema", StringComparison.OrdinalIgnoreCase)
            || body.Contains("json_object", StringComparison.OrdinalIgnoreCase);

        private string ErrorMessage(HttpStatusCode status, string body)
        {
            string detail = "";
            try
            {
                using JsonDocument doc = JsonDocument.Parse(body);
                if (doc.RootElement.TryGetProperty("error", out JsonElement error))
                    detail = error.ValueKind == JsonValueKind.String ? error.GetString() ?? ""
                        : error.TryGetProperty("message", out JsonElement m) ? m.GetString() ?? "" : "";
            }
            catch (JsonException)
            {
                detail = body.Length > 200 ? body[..200] : body;
            }
            string suffix = detail.Length > 0 ? $": {detail}" : "";
            return status switch
            {
                HttpStatusCode.Unauthorized or HttpStatusCode.Forbidden => $"{_endpoint.Host} rejected the API key{suffix}",
                HttpStatusCode.NotFound => $"{_endpoint.Host} does not know the model \"{_model}\" or the address is wrong{suffix}",
                HttpStatusCode.TooManyRequests => $"Too many requests to {_endpoint.Host}; try again in a moment{suffix}",
                _ => $"{_endpoint.Host} error {(int)status}{suffix}",
            };
        }

        private string ParseAnswer(string body)
        {
            try
            {
                using JsonDocument doc = JsonDocument.Parse(body);
                JsonElement choice = doc.RootElement.GetProperty("choices")[0];
                JsonElement message = choice.GetProperty("message");
                if (message.TryGetProperty("refusal", out JsonElement refusal) && refusal.ValueKind == JsonValueKind.String)
                    throw new MachineTranslationException($"{_model} declined to answer: {refusal.GetString()}");
                if (choice.TryGetProperty("finish_reason", out JsonElement finish) && finish.GetString() == "length")
                    throw new MachineTranslationException($"The answer of {_model} was cut off; the lines are too long for one request.");
                return TranslationPrompts.StripCodeFence(message.GetProperty("content").GetString() ?? "");
            }
            catch (Exception ex) when (ex is JsonException or KeyNotFoundException or InvalidOperationException or IndexOutOfRangeException)
            {
                throw new MachineTranslationException($"The answer of {_endpoint.Host} is not in the expected format.", ex);
            }
        }
    }
}
