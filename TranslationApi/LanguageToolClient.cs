using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;

namespace TranslationApi
{
    /// <summary>A spelling, grammar or style problem LanguageTool found in one text.</summary>
    /// <param name="Index">Index of the text in the list that was checked.</param>
    /// <param name="Offset">Position in that text (as written in the TRS).</param>
    public sealed record GrammarMatch(int Index, int Offset, int Length, string Message, IReadOnlyList<string> Replacements, string Category, string RuleId)
    {
        /// <summary>The text with the first replacement applied, or null if LanguageTool has none.</summary>
        public string? Apply(string text) =>
            Replacements.Count > 0 && Offset + Length <= text.Length ? text[..Offset] + Replacements[0] + text[(Offset + Length)..] : null;

        /// <summary>
        /// Replaces the occurrence of <paramref name="snippet"/> closest to where it was, so several corrections of one
        /// entry still apply after an earlier one changed its length; null if the snippet is gone.
        /// </summary>
        public static string? ApplyNear(string text, string snippet, int offset, string replacement)
        {
            if (snippet.Length == 0)
                return null;
            int best = -1;
            for (int i = text.IndexOf(snippet, StringComparison.Ordinal); i >= 0; i = text.IndexOf(snippet, i + 1, StringComparison.Ordinal))
                if (best < 0 || Math.Abs(i - offset) < Math.Abs(best - offset))
                    best = i;
            return best < 0 ? null : text[..best] + replacement + text[(best + snippet.Length)..];
        }
    }

    /// <summary>
    /// Checks texts with a LanguageTool server (https://languagetool.org): the public API, or a local server
    /// (java -jar languagetool-server.jar, usually http://localhost:8081). AGS markup is hidden from it, many texts go
    /// into one request, and the public API's limit of 20 requests a minute is kept.
    /// </summary>
    public sealed class LanguageToolClient
    {
        public const string PublicUrl = "https://api.languagetool.org";
        public const string LocalUrl = "http://localhost:8081";

        // The public API accepts 20 KB per request; local servers more, but smaller requests keep the progress moving
        private const int MaxRequestChars = 15000;
        private static readonly Regex MarkupPattern = new(@"^&\d+ |\\\[|%[-+ 0#]*\d*(?:\.\d+)?[sdifcxX]|@[A-Za-z0-9_]+@|\[");

        private readonly HttpClient _http;
        private readonly Uri _endpoint;
        private readonly bool _isPublic;
        private DateTime _lastRequest = DateTime.MinValue;

        public LanguageToolClient(string? baseUrl, HttpMessageHandler? handler = null)
        {
            string url = string.IsNullOrWhiteSpace(baseUrl) ? PublicUrl : baseUrl.Trim().TrimEnd('/');
            if (url.EndsWith("/v2/check", StringComparison.OrdinalIgnoreCase))
                url = url[..^"/v2/check".Length];
            if (!Uri.TryCreate(url + "/v2/check", UriKind.Absolute, out Uri? endpoint) || endpoint.Scheme is not ("http" or "https"))
                throw new MachineTranslationException($"\"{baseUrl}\" is not a valid LanguageTool address (e.g. {LocalUrl}).");
            _endpoint = endpoint;
            _isPublic = endpoint.Host.EndsWith("languagetool.org", StringComparison.OrdinalIgnoreCase);
            _http = handler != null ? new HttpClient(handler) : new HttpClient();
            _http.Timeout = TimeSpan.FromMinutes(2);
        }

        public string Name => _endpoint.Host;

        /// <summary>True for the public server, where the texts leave the computer.</summary>
        public bool IsPublic => _isPublic;

        /// <summary>
        /// Checks texts (as written in the TRS) in a language such as "de-DE" or "de". <paramref name="onMatches"/> gets the
        /// matches of every request as soon as it is done.
        /// </summary>
        public async Task CheckAsync(IReadOnlyList<string> texts, string language, Action<IReadOnlyList<GrammarMatch>> onMatches,
            IProgress<int>? progress = null, CancellationToken cancellationToken = default)
        {
            int index = 0;
            while (index < texts.Count)
            {
                cancellationToken.ThrowIfCancellationRequested();
                // Texts are joined with blank lines, so LanguageTool sees them as separate paragraphs
                var sb = new StringBuilder();
                var starts = new List<(int Index, int Start)>();
                do
                {
                    starts.Add((index, sb.Length));
                    sb.Append(Mask(texts[index])).Append("\n\n");
                    index++;
                }
                while (index < texts.Count && sb.Length + texts[index].Length < MaxRequestChars);

                string json = await PostAsync(sb.ToString(), language, cancellationToken).ConfigureAwait(false);
                onMatches(Parse(json, starts, texts));
                progress?.Report(index);
            }
        }

        /// <summary>Hides AGS markup behind spaces of the same length, so positions stay the same.</summary>
        internal static string Mask(string text) => MarkupPattern.Replace(text, m => new string(' ', m.Length));

        private static bool TouchesMarkup(string text, int offset, int length) =>
            MarkupPattern.Matches(text).Any(m => m.Index < offset + length && offset < m.Index + m.Length);

        internal static List<GrammarMatch> Parse(string json, IReadOnlyList<(int Index, int Start)> starts, IReadOnlyList<string> texts)
        {
            var result = new List<GrammarMatch>();
            try
            {
                using JsonDocument doc = JsonDocument.Parse(json);
                foreach (JsonElement m in doc.RootElement.GetProperty("matches").EnumerateArray())
                {
                    int offset = m.GetProperty("offset").GetInt32(), length = m.GetProperty("length").GetInt32();
                    var (index, start) = starts.LastOrDefault(s => s.Start <= offset);
                    if (offset - start + length > texts[index].Length)
                        continue;
                    JsonElement rule = m.GetProperty("rule");
                    string ruleId = rule.TryGetProperty("id", out JsonElement id) ? id.GetString() ?? "" : "";
                    // The masked markup is whitespace; complaints about it are noise
                    if ((ruleId.Contains("WHITESPACE", StringComparison.OrdinalIgnoreCase) || ruleId.Contains("SPACE", StringComparison.OrdinalIgnoreCase))
                        && Mask(texts[index]).Substring(offset - start, length).Trim().Length == 0)
                        continue;
                    // A match that covers part of the markup (e.g. "@." after "@TOTAL@ .") would break it when applied
                    if (TouchesMarkup(texts[index], offset - start, length))
                        continue;
                    var replacements = m.TryGetProperty("replacements", out JsonElement r)
                        ? r.EnumerateArray().Select(x => x.GetProperty("value").GetString() ?? "").Where(x => x.Length > 0).Take(5).ToList()
                        : new List<string>();
                    string category = rule.TryGetProperty("category", out JsonElement c) && c.TryGetProperty("name", out JsonElement n) ? n.GetString() ?? "" : "";
                    result.Add(new GrammarMatch(index, offset - start, length, m.GetProperty("message").GetString() ?? "", replacements, category, ruleId));
                }
            }
            catch (Exception ex) when (ex is JsonException or KeyNotFoundException or InvalidOperationException)
            {
                throw new MachineTranslationException("LanguageTool's answer is not in the expected format.", ex);
            }
            return result;
        }

        private async Task<string> PostAsync(string text, string language, CancellationToken cancellationToken)
        {
            if (_isPublic)
            {
                // Public API: at most 20 requests a minute
                TimeSpan wait = _lastRequest.AddSeconds(3.1) - DateTime.UtcNow;
                if (wait > TimeSpan.Zero)
                    await Task.Delay(wait, cancellationToken).ConfigureAwait(false);
                _lastRequest = DateTime.UtcNow;
            }
            var form = new FormUrlEncodedContent(new Dictionary<string, string>
            {
                ["text"] = text,
                ["language"] = string.IsNullOrWhiteSpace(language) ? "auto" : language.Replace('_', '-'),
                ["disabledRules"] = "WHITESPACE_RULE,UPPERCASE_SENTENCE_START,PUNCTUATION_PARAGRAPH_END",
            });
            try
            {
                using HttpResponseMessage response = await _http.PostAsync(_endpoint, form, cancellationToken).ConfigureAwait(false);
                string body = await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
                return response.StatusCode switch
                {
                    HttpStatusCode.OK => body,
                    HttpStatusCode.TooManyRequests => throw new MachineTranslationException("LanguageTool: too many requests; try again in a minute or use a local server."),
                    HttpStatusCode.RequestEntityTooLarge => throw new MachineTranslationException("LanguageTool: the text is too long for this server."),
                    _ => throw new MachineTranslationException($"LanguageTool error {(int)response.StatusCode}: {(body.Length > 200 ? body[..200] : body)}"),
                };
            }
            catch (HttpRequestException ex)
            {
                throw new MachineTranslationException($"LanguageTool at {_endpoint.GetLeftPart(UriPartial.Authority)} is not reachable: {ex.Message}", ex);
            }
            catch (TaskCanceledException ex) when (!cancellationToken.IsCancellationRequested)
            {
                throw new MachineTranslationException($"LanguageTool at {_endpoint.Host} did not answer in time.", ex);
            }
        }
    }
}
