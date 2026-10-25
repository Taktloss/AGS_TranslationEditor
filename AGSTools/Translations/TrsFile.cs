using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;

namespace AGSTools
{
    /// <summary>
    /// Reads and writes AGS translation sources (.trs): alternating source and translation lines,
    /// "//" comments, "//#Key=Value" settings and "//$" annotations (see Tools/data/tra_utils.cpp).
    /// </summary>
    public static class TrsFile
    {
        private const string TagDefault = "DEFAULT";
        private static readonly byte[] Utf8Bom = { 0xEF, 0xBB, 0xBF };
        private static readonly Encoding StrictUtf8 = new UTF8Encoding(false, throwOnInvalidBytes: true);

        /// <summary>Reads a TRS file.</summary>
        /// <param name="ansiCodepage">Codepage of 8-bit files, i.e. the game's text codepage.</param>
        public static TranslationDocument Read(string path, int ansiCodepage = AgsTextEncoding.DefaultAnsiCodepage) =>
            Parse(File.ReadAllBytes(path), ansiCodepage);

        public static TranslationDocument Parse(byte[] content, int ansiCodepage = AgsTextEncoding.DefaultAnsiCodepage)
        {
            var doc = new TranslationDocument();
            doc.Options.AnsiCodepage = ansiCodepage;
            string text = Decode(content, AgsTextEncoding.ForCodepage(ansiCodepage));
            string[] lines = text.Split('\n');
            int count = lines.Length;
            // A trailing newline does not start another line
            if (count > 0 && lines[count - 1].Length == 0)
                count--;

            bool inHeader = true;
            for (int i = 0; i < count; i++)
            {
                string line = TrimCr(lines[i]);
                if (line.StartsWith("//", StringComparison.Ordinal))
                {
                    if (line.Length > 2 && line[2] == '#')
                        ReadOption(doc.Options, line[3..]);
                    continue; // comments and "//$" annotations
                }

                // Some tools put blank lines into the header. The AGS compiler would pair them with the
                // following line and shift every entry, so treat them as part of the header instead.
                if (inHeader && line.Length == 0)
                    continue;
                inHeader = false;

                // Like the AGS compiler, the line after a source line is always its translation,
                // even if it starts with "//"
                string translation = i + 1 < count ? TrimCr(lines[++i]) : "";
                if (line.Length > 0)
                    doc.Entries.TryAdd(line, translation);
            }
            return doc;
        }

        /// <summary>
        /// Writes a TRS file with the standard AGS header. The file is UTF-8 if the translation's
        /// encoding is UTF-8, otherwise in the 8-bit codepage. Returns warnings about characters that could not be stored.
        /// </summary>
        public static IReadOnlyList<string> Write(string path, TranslationDocument doc)
        {
            var warnings = new List<string>();
            byte[] content = Format(doc, warnings);
            AtomicFile.Write(path, stream => stream.Write(content));
            return warnings;
        }

        public static byte[] Format(TranslationDocument doc, List<string> warnings)
        {
            TranslationOptions o = doc.Options;
            var sb = new StringBuilder();
            void Line(string s) => sb.Append(s).Append("\r\n");

            Line("// AGS TRANSLATION SOURCE FILE");
            Line("// Format is alternating lines with original game text and replacement");
            Line("// text. If you don't want to translate a line, just leave the following");
            Line("// line blank. Lines starting with '//' are comments - DO NOT translate");
            Line("// them. Special characters such as [ and %s symbolise things within the");
            Line("// game, so should be left in an appropriate place in the message.");
            Line("// ");
            Line("// Game identification: this lets game to detect a translation meant");
            Line("// for another game. These may be left blank too.");
            Line($"//#GameID={(o.GameUid != 0 ? o.GameUid.ToString(CultureInfo.InvariantCulture) : "")}");
            Line($"//#GameName={o.GameName}");
            Line("// ** Translation settings are below");
            Line("// ** Leave them as \"DEFAULT\" to use the game settings");
            Line("// The normal font to use - DEFAULT or font number");
            Line($"//#NormalFont={FormatOptionalInt(o.NormalFont)}");
            Line("// The speech font to use - DEFAULT or font number");
            Line($"//#SpeechFont={FormatOptionalInt(o.SpeechFont)}");
            Line("// Text direction - DEFAULT, LEFT or RIGHT");
            Line($"//#TextDirection={FormatDirection(o.TextDirection)}");
            Line("// Text encoding hint - ASCII or UTF-8");
            Line($"//#Encoding={(o.Encoding.Length > 0 ? o.Encoding : "ASCII")}");
            if (o.GameEncoding.Length > 0)
                Line($"//#GameEncoding={o.GameEncoding}");
            Line("// Text language, use standard locale strings, like 'en', 'en_US', etc");
            Line($"//#Language={o.Language.Replace('-', '_')}");
            Line("// Whether engine should translate Parser.Said strings automatically - ON or OFF");
            Line($"//#AutoTranslateParserSaid={(o.AutoTranslateParserSaid ? "ON" : "OFF")}");
            foreach (var (font, value) in o.FontOverrides)
                Line($"//#Font{font}={value}");
            Line("//  ");
            Line("// ** REMEMBER, WRITE YOUR TRANSLATION IN THE EMPTY LINES, DO");
            Line("// ** NOT CHANGE THE EXISTING TEXT.");

            foreach (var (source, translation) in doc.Entries)
            {
                // A real line break would split the entry; store it as escape sequence instead
                Line(EscapeLineBreaks(source));
                Line(EscapeLineBreaks(translation));
            }

            string text = sb.ToString();
            Encoding encoding = o.GetTextEncoding();
            int unencodable = doc.Entries.Count(e => !AgsTextEncoding.CanEncode(encoding, e.Key) || !AgsTextEncoding.CanEncode(encoding, e.Value));
            if (unencodable > 0)
                warnings.Add($"{unencodable} entries contain characters that do not exist in codepage {encoding.CodePage} and were stored as '?'. Set the encoding to UTF-8 to keep them.");
            return encoding.GetBytes(text);
        }

        /// <summary>
        /// Decodes TRS content: a UTF-8 BOM or "//#Encoding=UTF-8" means UTF-8, an explicit other encoding
        /// means 8-bit text; without a hint, content that is valid UTF-8 is treated as UTF-8.
        /// </summary>
        private static string Decode(byte[] content, Encoding ansi)
        {
            if (content.AsSpan().StartsWith(Utf8Bom))
                return AgsTextEncoding.Utf8.GetString(content, Utf8Bom.Length, content.Length - Utf8Bom.Length);

            string? hint = FindEncodingHint(content);
            if (hint != null)
                return TranslationOptions.IsUtf8Name(hint) ? AgsTextEncoding.Utf8.GetString(content) : ansi.GetString(content);

            try
            {
                return StrictUtf8.GetString(content);
            }
            catch (DecoderFallbackException)
            {
                return ansi.GetString(content);
            }
        }

        private static string? FindEncodingHint(byte[] content)
        {
            // Option lines are plain ASCII, so a byte-transparent decoding is good enough to find them
            foreach (string rawLine in Encoding.Latin1.GetString(content).Split('\n'))
            {
                string line = TrimCr(rawLine);
                if (!line.StartsWith("//", StringComparison.Ordinal))
                    break; // options are only in the header
                if (line.StartsWith("//#Encoding=", StringComparison.Ordinal))
                {
                    string value = line["//#Encoding=".Length..].Trim();
                    return value.Length > 0 ? value : null;
                }
            }
            return null;
        }

        private static void ReadOption(TranslationOptions options, string line)
        {
            int eq = line.IndexOf('=');
            string key = eq >= 0 ? line[..eq] : line;
            string value = eq >= 0 ? line[(eq + 1)..] : "";

            switch (key)
            {
                case "GameID":
                    options.GameUid = int.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out int uid) ? uid : 0;
                    break;
                case "GameName":
                    options.GameName = value;
                    break;
                case "NormalFont":
                    options.NormalFont = ParseOptionalInt(value);
                    break;
                case "SpeechFont":
                    options.SpeechFont = ParseOptionalInt(value);
                    break;
                case "TextDirection":
                    options.TextDirection = value switch
                    {
                        "LEFT" => TextDirection.LeftToRight,
                        "RIGHT" => TextDirection.RightToLeft,
                        _ => TextDirection.Default,
                    };
                    break;
                case "Encoding":
                    options.Encoding = value;
                    break;
                case "GameEncoding":
                    options.GameEncoding = value;
                    break;
                case "Language":
                    options.Language = value;
                    break;
                case "AutoTranslateParserSaid":
                    options.AutoTranslateParserSaid = string.Equals(value, "ON", StringComparison.OrdinalIgnoreCase);
                    break;
                default:
                    if (key.StartsWith("Font", StringComparison.Ordinal) &&
                        int.TryParse(key.AsSpan(4), NumberStyles.None, CultureInfo.InvariantCulture, out int font))
                        options.FontOverrides[font] = value;
                    break;
            }
        }

        private static int ParseOptionalInt(string value) =>
            value != TagDefault && int.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out int n) ? n : -1;

        private static string FormatOptionalInt(int value) =>
            value < 0 ? TagDefault : value.ToString(CultureInfo.InvariantCulture);

        private static string FormatDirection(TextDirection direction) => direction switch
        {
            TextDirection.LeftToRight => "LEFT",
            TextDirection.RightToLeft => "RIGHT",
            _ => TagDefault,
        };

        private static string EscapeLineBreaks(string text) =>
            text.IndexOfAny(LineBreaks) < 0 ? text : text.Replace("\r", "\\r").Replace("\n", "\\n");

        private static readonly char[] LineBreaks = { '\r', '\n' };

        private static string TrimCr(string line) => line.EndsWith('\r') ? line[..^1] : line;
    }
}
