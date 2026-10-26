using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Text.RegularExpressions;

namespace TranslationApi
{
    /// <summary>
    /// AGS markup that machine translation must keep: the voice prefix "&amp;N ", "[" line breaks,
    /// "\[" literal brackets, format placeholders like "%s" and "@GAMETOKEN@".
    /// </summary>
    public static class AgsMarkup
    {
        private static readonly Regex VoicePrefixPattern = new(@"^&\d+ ");
        private static readonly Regex ProtectedPattern = new(@"\\\[|%[-+ 0#]*\d*(?:\.\d+)?[sdifcxX]|@[A-Za-z0-9_]+@");
        private static readonly Regex LineBreakPattern = new(@"(?<!\\)\[");
        private static readonly Regex XmlKeepPattern = new(@"<x>(.*?)</x>|<br\s*/>", RegexOptions.Singleline);

        /// <summary>Splits off the voice prefix ("&amp;12 "); the prefix is empty if there is none.</summary>
        public static (string Prefix, string Body) SplitVoicePrefix(string text)
        {
            Match m = VoicePrefixPattern.Match(text);
            return m.Success ? (m.Value, text[m.Length..]) : ("", text);
        }

        /// <summary>The text between line breaks ("[", but not "\[").</summary>
        public static string[] SplitLines(string text) => LineBreakPattern.Split(text);

        public static int CountLineBreaks(string text) => LineBreakPattern.Matches(text).Count;

        /// <summary>Placeholders and literal brackets in the text, in order.</summary>
        public static List<string> ProtectedParts(string text) => ProtectedPattern.Matches(text).Select(m => m.Value).ToList();

        /// <summary>
        /// XML for services with tag handling (DeepL): line breaks become &lt;br/&gt;,
        /// placeholders &lt;x&gt;…&lt;/x&gt; (an ignored tag), everything else is escaped.
        /// </summary>
        public static string ToXml(string text)
        {
            var parts = new List<string>();
            foreach (string line in SplitLines(text))
            {
                int pos = 0;
                var xml = new System.Text.StringBuilder();
                foreach (Match m in ProtectedPattern.Matches(line))
                {
                    xml.Append(WebUtility.HtmlEncode(line[pos..m.Index]));
                    xml.Append("<x>").Append(WebUtility.HtmlEncode(m.Value)).Append("</x>");
                    pos = m.Index + m.Length;
                }
                xml.Append(WebUtility.HtmlEncode(line[pos..]));
                parts.Add(xml.ToString());
            }
            return string.Join("<br/>", parts);
        }

        /// <summary>The inverse of <see cref="ToXml"/>.</summary>
        public static string FromXml(string xml)
        {
            var result = new System.Text.StringBuilder();
            int pos = 0;
            foreach (Match m in XmlKeepPattern.Matches(xml))
            {
                result.Append(WebUtility.HtmlDecode(xml[pos..m.Index]));
                result.Append(m.Groups[1].Success ? WebUtility.HtmlDecode(m.Groups[1].Value) : "[");
                pos = m.Index + m.Length;
            }
            result.Append(WebUtility.HtmlDecode(xml[pos..]));
            return result.ToString();
        }
    }
}
