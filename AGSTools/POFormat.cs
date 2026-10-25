using System;
using System.Collections.Generic;
using System.IO;
using System.Text;

namespace AGSTools
{
    public static class POFormat
    {
        public static void CreatePO(string filename, Dictionary<string, string> data, string language = "de")
        {
            using (StreamWriter sw = new StreamWriter(filename))
            {
                AddPOHeader(sw, language);

                foreach (KeyValuePair<string, string> entry in data)
                {
                    // An empty msgid is reserved for the PO header
                    if (entry.Key.Length == 0)
                        continue;

                    sw.WriteLine("msgid \"{0}\"", Escape(entry.Key));
                    sw.WriteLine("msgstr \"{0}\"", Escape(entry.Value));
                    sw.WriteLine();
                }
            }
        }

        public static Dictionary<string, string> OpenPO(string filename)
        {
            Dictionary<string, string> translatedLines = new Dictionary<string, string>();

            // Current entry state; "target" is the field that "..." continuation lines append to
            StringBuilder? msgid = null;
            StringBuilder? msgstr = null;
            StringBuilder? target = null;

            void Commit()
            {
                // msgid "" is the header entry, not a translation
                if (msgid != null && msgid.Length > 0)
                    translatedLines.TryAdd(msgid.ToString(), msgstr?.ToString() ?? string.Empty);
                msgid = null;
                msgstr = null;
                target = null;
            }

            foreach (string rawLine in File.ReadLines(filename))
            {
                string line = rawLine.Trim();

                //Skip empty lines and comments (also obsolete "#~" entries)
                if (line.Length == 0 || line.StartsWith('#'))
                    continue;

                if (line.StartsWith('"'))
                {
                    target?.Append(Unescape(Unquote(line)));
                }
                else if (line.StartsWith("msgctxt", StringComparison.Ordinal))
                {
                    // Context starts a new entry; the context itself is not needed
                    Commit();
                    target = null;
                }
                else if (line.StartsWith("msgid_plural", StringComparison.Ordinal))
                {
                    target = null;
                }
                else if (line.StartsWith("msgid", StringComparison.Ordinal))
                {
                    if (msgstr != null)
                        Commit();
                    msgid = new StringBuilder(Unescape(Unquote(line)));
                    target = msgid;
                }
                else if (line.StartsWith("msgstr[0]", StringComparison.Ordinal) ||
                         line.StartsWith("msgstr ", StringComparison.Ordinal) ||
                         line.StartsWith("msgstr\"", StringComparison.Ordinal))
                {
                    msgstr = new StringBuilder(Unescape(Unquote(line)));
                    target = msgstr;
                }
                else
                {
                    // Other plural forms (msgstr[1]...) are ignored
                    target = null;
                }
            }
            Commit();

            return translatedLines;
        }

        /// <summary>
        /// Returns the text between the first and the last quote of a PO line
        /// </summary>
        private static string Unquote(string line)
        {
            int first = line.IndexOf('"');
            int last = line.LastIndexOf('"');
            return first >= 0 && last > first ? line.Substring(first + 1, last - first - 1) : string.Empty;
        }

        private static string Escape(string text)
        {
            StringBuilder sb = new StringBuilder(text.Length);
            foreach (char c in text)
            {
                switch (c)
                {
                    case '\\': sb.Append("\\\\"); break;
                    case '"': sb.Append("\\\""); break;
                    case '\n': sb.Append("\\n"); break;
                    case '\r': sb.Append("\\r"); break;
                    case '\t': sb.Append("\\t"); break;
                    default: sb.Append(c); break;
                }
            }
            return sb.ToString();
        }

        private static string Unescape(string text)
        {
            if (text.IndexOf('\\') < 0)
                return text;

            StringBuilder sb = new StringBuilder(text.Length);
            for (int i = 0; i < text.Length; i++)
            {
                char c = text[i];
                if (c != '\\' || i + 1 >= text.Length)
                {
                    sb.Append(c);
                    continue;
                }

                char next = text[++i];
                switch (next)
                {
                    case 'n': sb.Append('\n'); break;
                    case 'r': sb.Append('\r'); break;
                    case 't': sb.Append('\t'); break;
                    case '"': sb.Append('"'); break;
                    case '\\': sb.Append('\\'); break;
                    default: sb.Append('\\').Append(next); break;
                }
            }
            return sb.ToString();
        }

        private static void AddPOHeader(StreamWriter sw, string language = "de")
        {
            sw.WriteLine("msgid \"\"");
            sw.WriteLine("msgstr \"\"");
            sw.WriteLine("\"Project-Id-Version: \\n\"");
            sw.WriteLine("\"PO-Revision-Date: \\n\"");
            sw.WriteLine("\"Last-Translator: \\n\"");
            sw.WriteLine("\"Language-Team: \\n\"");
            sw.WriteLine("\"MIME-Version: 1.0\\n\"");
            sw.WriteLine("\"Content-Type: text/plain; charset=UTF-8\\n\"");
            sw.WriteLine("\"Content-Transfer-Encoding: 8bit\\n\"");
            sw.WriteLine($"\"Language: {language}\\n\"");
            sw.WriteLine("\"X-Generator: AGS Translation Editor\\n\"");
            sw.WriteLine();
        }
    }
}