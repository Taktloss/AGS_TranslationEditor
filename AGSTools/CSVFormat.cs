using System;
using System.Collections.Generic;
using System.IO;
using System.Text;

namespace AGSTools
{
    public static class CSVFormat
    {
        public static void CreateCSV(string filename, Dictionary<string, string> data, char seperator = ';')
        {
            using (StreamWriter sw = new StreamWriter(filename))
            {
                foreach (KeyValuePair<string, string> entry in data)
                {
                    string msgid = Quote(entry.Key, seperator);
                    string msgstr = Quote(entry.Value, seperator);
                    sw.WriteLine($"{msgid}{seperator}{msgstr}");
                }
            }
        }

        /// <summary>
        /// Reads a CSV/TSV file with source text in the first and translation in the second column.
        /// </summary>
        /// <param name="filename">Input filename</param>
        /// <param name="seperator">Column separator; if null, tab is used for .tsv files and ';' otherwise</param>
        public static Dictionary<string, string> OpenCSV(string filename, char? seperator = null)
        {
            Dictionary<string, string> translatedLines = new Dictionary<string, string>();

            char sep = seperator ??
                (string.Equals(Path.GetExtension(filename), ".tsv", StringComparison.OrdinalIgnoreCase) ? '\t' : ';');

            foreach (List<string> row in ReadRows(File.ReadAllText(filename), sep))
            {
                //Skip empty lines
                if (row.Count == 0 || (row.Count == 1 && row[0].Length == 0))
                    continue;

                string msgid = row[0];
                string msgstr = row.Count > 1 ? row[1] : string.Empty;

                //Check if key already exists
                translatedLines.TryAdd(msgid, msgstr);
            }
            return translatedLines;
        }

        /// <summary>
        /// Quotes a field if it contains the separator, quotes or line breaks (RFC 4180 style)
        /// </summary>
        private static string Quote(string field, char seperator)
        {
            if (field.IndexOf(seperator) < 0 && field.IndexOf('"') < 0 &&
                field.IndexOf('\n') < 0 && field.IndexOf('\r') < 0)
                return field;

            return "\"" + field.Replace("\"", "\"\"") + "\"";
        }

        /// <summary>
        /// Splits CSV text into rows of fields. Quoted fields may contain separators,
        /// doubled quotes and line breaks.
        /// </summary>
        private static IEnumerable<List<string>> ReadRows(string text, char seperator)
        {
            List<string> row = new List<string>();
            StringBuilder field = new StringBuilder();
            bool inQuotes = false;
            int i = 0;

            while (i < text.Length)
            {
                char c = text[i];

                if (inQuotes)
                {
                    if (c == '"')
                    {
                        if (i + 1 < text.Length && text[i + 1] == '"')
                        {
                            field.Append('"');
                            i += 2;
                            continue;
                        }
                        inQuotes = false;
                    }
                    else
                    {
                        field.Append(c);
                    }
                    i++;
                    continue;
                }

                if (c == '"' && field.Length == 0)
                {
                    inQuotes = true;
                }
                else if (c == seperator)
                {
                    row.Add(field.ToString());
                    field.Clear();
                }
                else if (c == '\r' || c == '\n')
                {
                    row.Add(field.ToString());
                    field.Clear();
                    yield return row;
                    row = new List<string>();

                    if (c == '\r' && i + 1 < text.Length && text[i + 1] == '\n')
                        i++;
                }
                else
                {
                    field.Append(c);
                }
                i++;
            }

            if (field.Length > 0 || row.Count > 0)
            {
                row.Add(field.ToString());
                yield return row;
            }
        }
    }
}