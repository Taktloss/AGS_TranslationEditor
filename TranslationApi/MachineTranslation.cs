using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using AGSTools;

namespace TranslationApi
{
    public enum Formality { Default, Informal, Formal }

    public sealed class MachineTranslationOptions
    {
        /// <summary>Language code like "en"; empty to let the service detect it.</summary>
        public string SourceLanguage { get; init; } = "en";
        public string TargetLanguage { get; init; } = "de";
        public Formality Formality { get; init; }
        /// <summary>Game title, as context for services that understand it.</summary>
        public string GameTitle { get; init; } = "";
        public Glossary? Glossary { get; init; }

        /// <summary>English name of a language code ("de" → "German"), or the code itself.</summary>
        public static string LanguageName(string code)
        {
            try
            {
                return code.Length == 0 ? "the source language" : CultureInfo.GetCultureInfo(code).EnglishName;
            }
            catch (CultureNotFoundException)
            {
                return code;
            }
        }
    }

    /// <summary>A machine translation service.</summary>
    public interface IMachineTranslator
    {
        string Name { get; }

        /// <summary>How many texts one request may contain.</summary>
        int MaxBatchSize { get; }

        /// <summary>
        /// Translates texts without voice prefix and without leading or trailing spaces.
        /// They may contain "[" line breaks and placeholders, which the result must keep.
        /// </summary>
        Task<IReadOnlyList<string>> TranslateAsync(IReadOnlyList<string> texts, MachineTranslationOptions options, CancellationToken cancellationToken = default);
    }

    /// <summary>Thrown when a service rejects a request (bad key, quota, refusal).</summary>
    public sealed class MachineTranslationException : Exception
    {
        public MachineTranslationException(string message, Exception? inner = null) : base(message, inner) { }
    }

    public static class MachineTranslation
    {
        /// <summary>
        /// Translates TRS texts batch by batch. Voice prefixes and surrounding spaces are kept as in the original.
        /// <paramref name="onTranslated"/> gets (index, translation) as soon as a batch is done,
        /// so the finished part is kept when a later batch fails.
        /// </summary>
        public static async Task TranslateAsync(IMachineTranslator translator, IReadOnlyList<string> texts, MachineTranslationOptions options,
            Action<int, string> onTranslated, CancellationToken cancellationToken = default)
        {
            var pending = new List<(int Index, string Prefix, string Lead, string Core, string Trail)>();
            for (int i = 0; i < texts.Count; i++)
            {
                (string prefix, string body) = AgsMarkup.SplitVoicePrefix(texts[i]);
                string core = body.Trim(' ');
                if (core.Length == 0)
                {
                    onTranslated(i, texts[i]);
                    continue;
                }
                string lead = body[..body.IndexOf(core, StringComparison.Ordinal)];
                pending.Add((i, prefix, lead, core, body[(lead.Length + core.Length)..]));
            }

            foreach (var batch in pending.Chunk(Math.Max(1, translator.MaxBatchSize)))
            {
                cancellationToken.ThrowIfCancellationRequested();
                IReadOnlyList<string> result = await translator.TranslateAsync(batch.Select(p => p.Core).ToList(), options, cancellationToken).ConfigureAwait(false);
                if (result.Count != batch.Length)
                    throw new MachineTranslationException($"{translator.Name} returned {result.Count} translations for {batch.Length} texts.");
                for (int i = 0; i < batch.Length; i++)
                    onTranslated(batch[i].Index, batch[i].Prefix + batch[i].Lead + result[i].Trim(' ') + batch[i].Trail);
            }
        }
    }
}
