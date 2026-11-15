using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

namespace TranslationApi
{
    /// <summary>A translation the language model suggests for an entry, with the reason.</summary>
    /// <param name="Index">Index into the list that was given to the assistant.</param>
    public sealed record AiSuggestion(int Index, string Problem, string Suggestion);

    /// <summary>An entry to review: the TRS original and its translation.</summary>
    public sealed record ReviewItem(string Original, string Translation);

    /// <summary>An entry whose translation is too long, with the number of characters that fit.</summary>
    public sealed record ShortenItem(string Original, string Translation, int MaxCharacters);

    /// <summary>
    /// Work for a language model beyond filling empty entries: reviewing translations and shortening texts that do
    /// not fit. Voice prefixes and surrounding spaces are taken off before sending and put back on the suggestions.
    /// </summary>
    public static class AiAssistant
    {
        public const int ReviewBatchSize = 30;
        public const int ShortenBatchSize = 30;

        private static readonly Dictionary<string, JsonElement> ReviewSchema = Schemas.Object(new
        {
            problems = new
            {
                type = "array",
                items = new
                {
                    type = "object",
                    properties = new { id = new { type = "integer" }, problem = new { type = "string" }, suggestion = new { type = "string" } },
                    required = new[] { "id", "problem", "suggestion" },
                    additionalProperties = false,
                },
            },
        }, "problems");

        private static readonly Dictionary<string, JsonElement> ShortenSchema = Schemas.Object(new
        {
            results = new
            {
                type = "array",
                items = new
                {
                    type = "object",
                    properties = new { id = new { type = "integer" }, text = new { type = "string" } },
                    required = new[] { "id", "text" },
                    additionalProperties = false,
                },
            },
        }, "results");

        /// <summary>
        /// Asks the model to review translations batch by batch. <paramref name="onSuggestions"/> gets the findings of
        /// every batch as soon as it is done, so earlier findings are kept when a later batch fails.
        /// </summary>
        public static async Task ReviewAsync(ILanguageModel model, IReadOnlyList<ReviewItem> items, MachineTranslationOptions options,
            Action<IReadOnlyList<AiSuggestion>> onSuggestions, IProgress<int>? progress = null, CancellationToken cancellationToken = default)
        {
            int done = 0;
            foreach (var batch in items.Select((item, index) => (item, index)).Chunk(ReviewBatchSize))
            {
                cancellationToken.ThrowIfCancellationRequested();
                var parts = batch.Select(b => (b.index, Original: Split(b.item.Original), Translation: Split(b.item.Translation))).ToList();
                string message = TranslationPrompts.ItemsMessage(parts.Select((p, i) => new { id = i, original = p.Original.Core, translation = p.Translation.Core }));
                string json = await model.CompleteJsonAsync(TranslationPrompts.ReviewSystem(options, parts.Select(p => p.Original.Core).ToList()), message,
                    ReviewSchema, cancellationToken).ConfigureAwait(false);

                var found = new List<AiSuggestion>();
                foreach (JsonElement e in Array(json, "problems", model.Name))
                {
                    if (!TryId(e, parts.Count, out int id) || !e.TryGetProperty("suggestion", out JsonElement s) || s.GetString() is not { Length: > 0 } suggestion)
                        continue;
                    var p = parts[id];
                    string full = p.Translation.Wrap(suggestion.Trim(' '));
                    if (full != items[p.index].Translation)
                        found.Add(new AiSuggestion(p.index, e.TryGetProperty("problem", out JsonElement pr) ? pr.GetString() ?? "" : "", full));
                }
                onSuggestions(found);
                done += batch.Length;
                progress?.Report(done);
            }
        }

        /// <summary>Asks the model for shorter translations; one suggestion per item it answered.</summary>
        public static async Task<List<AiSuggestion>> ShortenAsync(ILanguageModel model, IReadOnlyList<ShortenItem> items, MachineTranslationOptions options,
            IProgress<int>? progress = null, CancellationToken cancellationToken = default)
        {
            var result = new List<AiSuggestion>();
            int done = 0;
            foreach (var batch in items.Select((item, index) => (item, index)).Chunk(ShortenBatchSize))
            {
                cancellationToken.ThrowIfCancellationRequested();
                var parts = batch.Select(b => (b.index, Original: Split(b.item.Original), Translation: Split(b.item.Translation), b.item.MaxCharacters)).ToList();
                string message = TranslationPrompts.ItemsMessage(parts.Select((p, i) => new
                {
                    id = i,
                    original = p.Original.Core,
                    translation = p.Translation.Core,
                    max_characters = p.MaxCharacters,
                }));
                string json = await model.CompleteJsonAsync(TranslationPrompts.ShortenSystem(options), message, ShortenSchema, cancellationToken).ConfigureAwait(false);
                foreach (JsonElement e in Array(json, "results", model.Name))
                {
                    if (!TryId(e, parts.Count, out int id) || !e.TryGetProperty("text", out JsonElement t) || t.GetString() is not { Length: > 0 } text)
                        continue;
                    var p = parts[id];
                    string shorter = text.Trim(' ');
                    string note = shorter.Length <= p.MaxCharacters
                        ? $"{p.Translation.Core.Length} → {shorter.Length} characters (fits: {p.MaxCharacters})"
                        : $"{p.Translation.Core.Length} → {shorter.Length} characters, still more than {p.MaxCharacters}";
                    result.Add(new AiSuggestion(p.index, note, p.Translation.Wrap(shorter)));
                }
                done += batch.Length;
                progress?.Report(done);
            }
            return result;
        }

        private static List<JsonElement> Array(string json, string property, string service)
        {
            try
            {
                using JsonDocument doc = JsonDocument.Parse(TranslationPrompts.StripCodeFence(json));
                return doc.RootElement.GetProperty(property).EnumerateArray().Select(e => e.Clone()).ToList();
            }
            catch (Exception ex) when (ex is JsonException or KeyNotFoundException or InvalidOperationException)
            {
                throw new MachineTranslationException($"The answer of {service} is not in the expected format.", ex);
            }
        }

        private static bool TryId(JsonElement e, int count, out int id)
        {
            id = -1;
            return e.ValueKind == JsonValueKind.Object && e.TryGetProperty("id", out JsonElement v) && v.TryGetInt32(out id) && id >= 0 && id < count;
        }

        private static Parts Split(string text)
        {
            (string prefix, string body) = AgsMarkup.SplitVoicePrefix(text);
            string core = body.Trim(' ');
            string lead = core.Length == 0 ? body : body[..body.IndexOf(core, StringComparison.Ordinal)];
            return new Parts(prefix, lead, core, body[(lead.Length + core.Length)..]);
        }

        private sealed record Parts(string Prefix, string Lead, string Core, string Trail)
        {
            public string Wrap(string core) => Prefix + Lead + core + Trail;
        }
    }
}
