using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading;
using System.Threading.Tasks;

namespace TranslationApi
{
    /// <summary>DeepL API (free or pro key). Line breaks and placeholders are sent as ignored XML tags.</summary>
    public sealed class DeepLTranslator : IMachineTranslator
    {
        private readonly string _apiKey;
        private readonly HttpClient _http;

        public DeepLTranslator(string apiKey, HttpMessageHandler? handler = null)
        {
            _apiKey = apiKey.Trim();
            _http = handler != null ? new HttpClient(handler) : new HttpClient();
        }

        public string Name => "DeepL";
        public int MaxBatchSize => 50;

        /// <summary>Free keys end with ":fx" and use a separate host.</summary>
        public string Endpoint => _apiKey.EndsWith(":fx", StringComparison.Ordinal)
            ? "https://api-free.deepl.com/v2/translate"
            : "https://api.deepl.com/v2/translate";

        public async Task<IReadOnlyList<string>> TranslateAsync(IReadOnlyList<string> texts, MachineTranslationOptions options, CancellationToken cancellationToken = default)
        {
            var body = new Request
            {
                Text = texts.Select(AgsMarkup.ToXml).ToList(),
                TargetLang = options.TargetLanguage.ToUpperInvariant(),
                SourceLang = options.SourceLanguage.Length > 0 ? options.SourceLanguage.ToUpperInvariant() : null,
                Formality = options.Formality switch
                {
                    Formality.Informal => "prefer_less",
                    Formality.Formal => "prefer_more",
                    _ => null,
                },
            };
            using var request = new HttpRequestMessage(HttpMethod.Post, Endpoint)
            {
                Content = JsonContent.Create(body, options: JsonOptions),
            };
            request.Headers.TryAddWithoutValidation("Authorization", "DeepL-Auth-Key " + _apiKey);

            HttpResponseMessage response;
            try
            {
                response = await _http.SendAsync(request, cancellationToken).ConfigureAwait(false);
            }
            catch (HttpRequestException ex)
            {
                throw new MachineTranslationException($"DeepL is not reachable: {ex.Message}", ex);
            }
            using (response)
            {
                if (!response.IsSuccessStatusCode)
                    throw new MachineTranslationException(ErrorText(response.StatusCode, await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false)));
                Response? result = await response.Content.ReadFromJsonAsync<Response>(JsonOptions, cancellationToken).ConfigureAwait(false);
                return result?.Translations?.Select(t => AgsMarkup.FromXml(t.Text)).ToList()
                    ?? throw new MachineTranslationException("DeepL sent an empty answer.");
            }
        }

        private static string ErrorText(HttpStatusCode status, string body) => (int)status switch
        {
            403 => "DeepL rejected the API key.",
            456 => "The DeepL quota for this month is used up.",
            429 => "Too many requests to DeepL; try again in a moment.",
            _ => $"DeepL error {(int)status}: {body}",
        };

        private static readonly JsonSerializerOptions JsonOptions = new()
        {
            PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower,
            DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        };

        private sealed class Request
        {
            public List<string> Text { get; init; } = new();
            public string TargetLang { get; init; } = "";
            public string? SourceLang { get; init; }
            public string? Formality { get; init; }
            public string TagHandling { get; init; } = "xml";
            public List<string> IgnoreTags { get; init; } = new() { "x" };
        }

        private sealed class Response
        {
            public List<Translation>? Translations { get; init; }
        }

        private sealed class Translation
        {
            public string Text { get; init; } = "";
        }
    }
}
