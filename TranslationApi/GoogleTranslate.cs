using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using Newtonsoft.Json.Linq;

namespace TranslationApi
{
    /// <summary>
    /// The free Google Translate web endpoint (no key). Unofficial: it may be rate limited or change without notice.
    /// Every line between "[" breaks is translated on its own so the breaks stay where they are.
    /// </summary>
    public class GoogleTranslator : IMachineTranslator
    {
        private readonly HttpClient _http;

        public GoogleTranslator(HttpMessageHandler? handler = null)
        {
            _http = handler != null ? new HttpClient(handler) : new HttpClient();
        }

        public string Name => "Google";
        public int MaxBatchSize => 1;

        public async Task<IReadOnlyList<string>> TranslateAsync(IReadOnlyList<string> texts, MachineTranslationOptions options, CancellationToken cancellationToken = default)
        {
            string from = options.SourceLanguage.Length > 0 ? options.SourceLanguage : "auto";
            var results = new List<string>();
            foreach (string text in texts)
            {
                var lines = new List<string>();
                foreach (string line in AgsMarkup.SplitLines(text))
                    lines.Add(line.Trim().Length == 0 ? line : await TranslateLineAsync(line, from, options.TargetLanguage, cancellationToken).ConfigureAwait(false));
                results.Add(string.Join("[", lines));
            }
            return results;
        }

        private async Task<string> TranslateLineAsync(string text, string from, string to, CancellationToken cancellationToken)
        {
            string url = $"https://translate.googleapis.com/translate_a/single?client=gtx&sl={from}&tl={to}&dt=t&q={WebUtility.UrlEncode(text)}";
            string result;
            try
            {
                result = await _http.GetStringAsync(url, cancellationToken).ConfigureAwait(false);
            }
            catch (HttpRequestException ex)
            {
                throw new MachineTranslationException(ex.StatusCode == HttpStatusCode.TooManyRequests
                    ? "Google limits the requests; try again later or use DeepL or Claude."
                    : $"Google Translate failed: {ex.Message}", ex);
            }
            // [[["translated part","original part",...],...],...]
            return string.Concat(JArray.Parse(result)[0].Select(part => part[0]?.Value<string>() ?? ""));
        }
    }
}
