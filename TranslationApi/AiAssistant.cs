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

    /// <summary>A translation that does not fit its GUI controls, with the controls' room (from the translation check).</summary>
    public sealed record FitRequest(string Original, string Translation, IReadOnlyList<AGSTools.GuiFit> Fits);

    public enum FitOutcome
    {
        /// <summary>The suggestion fits every control and keeps the markup.</summary>
        Fits,
        /// <summary>The model's shortest answer still does not fit.</summary>
        StillTooBig,
        /// <summary>The model's answers lost or added placeholders or line breaks.</summary>
        MarkupChanged,
        /// <summary>The model did not answer for this entry.</summary>
        NoAnswer,
    }

    /// <summary>
    /// Result of <see cref="AiAssistant.ShortenToFitAsync"/>. <see cref="Suggestion"/> is set only when it fits and keeps the
    /// markup; <see cref="Before"/> and <see cref="After"/> are the size in pixels in the tightest control.
    /// </summary>
    public sealed record FitSuggestion(int Index, FitOutcome Outcome, string? Suggestion, string? Attempt, AGSTools.FitResult Before, AGSTools.FitResult? After);

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

        /// <summary>
        /// Shortens texts that do not fit their GUI controls and checks every answer: it must keep placeholders, tokens and
        /// "\[" unchanged, add no line breaks, and fit every control when measured with the game font. Entries whose
        /// answer fails get a second request with a tighter limit; what still fails is returned without a suggestion.
        /// </summary>
        public static async Task<List<FitSuggestion>> ShortenToFitAsync(ILanguageModel model, IReadOnlyList<FitRequest> items, MachineTranslationOptions options,
            IProgress<int>? progress = null, CancellationToken cancellationToken = default, int rounds = 2)
        {
            var before = items.Select(i => Tightest(i.Fits, i.Translation)).ToArray();
            var limits = items.Select((item, i) => EstimateCharacters(item.Translation, before[i])).ToArray();
            var results = new FitSuggestion?[items.Count];
            var lastAttempt = new (FitOutcome Outcome, string? Text, AGSTools.FitResult? After)[items.Count];
            List<int> pending = Enumerable.Range(0, items.Count).ToList();

            for (int round = 0; round < rounds && pending.Count > 0; round++)
            {
                var request = pending.Select(i => new ShortenItem(items[i].Original, items[i].Translation, limits[i])).ToList();
                List<AiSuggestion> answers = await ShortenAsync(model, request, options, null, cancellationToken).ConfigureAwait(false);
                var byIndex = answers.GroupBy(a => a.Index).ToDictionary(g => pending[g.Key], g => g.First().Suggestion);
                var stillPending = new List<int>();
                foreach (int i in pending)
                {
                    if (!byIndex.TryGetValue(i, out string? text))
                    {
                        lastAttempt[i] = lastAttempt[i].Text != null ? lastAttempt[i] : (FitOutcome.NoAnswer, null, null);
                        stillPending.Add(i);
                        continue;
                    }
                    if (!KeepsMarkup(items[i].Translation, text))
                    {
                        lastAttempt[i] = (FitOutcome.MarkupChanged, text, null);
                        stillPending.Add(i);
                        continue;
                    }
                    AGSTools.FitResult after = Tightest(items[i].Fits, text);
                    if (items[i].Fits.All(f => f.Measure(text).Fits))
                    {
                        results[i] = new FitSuggestion(i, FitOutcome.Fits, text, text, before[i], after);
                        continue;
                    }
                    lastAttempt[i] = (FitOutcome.StillTooBig, text, after);
                    limits[i] = Math.Max(1, Math.Min(limits[i] - 1, EstimateCharacters(text, after)));
                    stillPending.Add(i);
                }
                pending = stillPending;
                progress?.Report(items.Count - pending.Count);
            }
            foreach (int i in pending)
                results[i] = new FitSuggestion(i, lastAttempt[i].Outcome, null, lastAttempt[i].Text, before[i], lastAttempt[i].After);
            return results.Select(r => r!).ToList();
        }

        /// <summary>The measurement in the control with the least room (largest overflow).</summary>
        private static AGSTools.FitResult Tightest(IReadOnlyList<AGSTools.GuiFit> fits, string text) =>
            fits.Select(f => f.Measure(text)).OrderByDescending(r => r.Fits ? 0 : 1).ThenByDescending(r => (double)r.Size / Math.Max(1, r.Limit)).First();

        /// <summary>Characters that should fit, scaled from the text's length by the room it has; 5 % spare.</summary>
        private static int EstimateCharacters(string text, AGSTools.FitResult result)
        {
            int length = AgsMarkup.SplitVoicePrefix(text).Body.Trim(' ').Length;
            double ratio = result.Size > 0 ? Math.Min(1.0, (double)result.Limit / result.Size) : 1.0;
            return Math.Max(1, (int)Math.Floor(length * ratio * 0.95));
        }

        /// <summary>True if the shortened text keeps the voice prefix, the placeholders and "\[" and has no more line breaks.</summary>
        internal static bool KeepsMarkup(string translation, string shorter)
        {
            if (AgsMarkup.SplitVoicePrefix(translation).Prefix != AgsMarkup.SplitVoicePrefix(shorter).Prefix)
                return false;
            if (AgsMarkup.CountLineBreaks(shorter) > AgsMarkup.CountLineBreaks(translation))
                return false;
            var a = AgsMarkup.ProtectedParts(translation).OrderBy(p => p, StringComparer.Ordinal);
            var b = AgsMarkup.ProtectedParts(shorter).OrderBy(p => p, StringComparer.Ordinal);
            return a.SequenceEqual(b);
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
