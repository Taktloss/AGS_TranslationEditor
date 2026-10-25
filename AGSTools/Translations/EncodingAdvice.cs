using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace AGSTools
{
    /// <summary>
    /// Checks whether a translation's encoding works with the game's engine.
    /// </summary>
    /// <remarks>
    /// A UTF-8 translation for a game with 8-bit texts only works on Windows: the engine converts the
    /// translation keys to the game's codepage with <c>setlocale(LC_CTYPE, ".1252")</c>
    /// (Engine/ac/translation.cpp, StrUtil::ConvertUtf8ToAscii). Such locale names exist only on Windows;
    /// on Linux and macOS the conversion fails, every key becomes empty and no text is translated.
    /// </remarks>
    public static class EncodingAdvice
    {
        /// <param name="engineVersion">Engine version of the game, e.g. "3.6.1.35".</param>
        /// <param name="gameIsUnicode">True if the game itself stores its texts as UTF-8.</param>
        public static IEnumerable<string> Check(TranslationDocument doc, string? engineVersion, bool gameIsUnicode)
        {
            TranslationOptions options = doc.Options;
            if (!options.IsUtf8 || gameIsUnicode)
                yield break;

            if (Version.TryParse(engineVersion, out Version? version) && version < new Version(3, 6))
            {
                yield return $"UTF-8 translations need AGS 3.6 or later, but the game uses AGS {engineVersion}; "
                    + "it will show garbled special characters. Use an 8-bit translation.";
                yield break;
            }

            Encoding ansi = AgsTextEncoding.ForCodepage(options.AnsiCodepage);
            var outside = doc.Entries.Values
                .SelectMany(v => TextEscaping.ToRuntime(v).EnumerateRunes())
                .Where(r => r.Value >= 0x80 && !AgsTextEncoding.CanEncode(ansi, r.ToString()))
                .Select(r => r.ToString())
                .Distinct()
                .ToList();
            string advice = outside.Count == 0
                ? $" All characters of this translation exist in codepage {ansi.CodePage}: an 8-bit translation works everywhere."
                : $" Characters such as {string.Join(" ", outside.Take(5))} do not exist in codepage {ansi.CodePage}; "
                  + "keep UTF-8 only if the translation is meant for Windows.";
            yield return "The game stores its texts as 8-bit, so for a UTF-8 translation the engine converts the keys with a "
                + $"Windows locale (\".{ansi.CodePage}\"). The Linux and macOS engine cannot do that and shows the original texts." + advice;
        }
    }
}
