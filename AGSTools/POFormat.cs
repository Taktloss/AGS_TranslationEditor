using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;

namespace AGSTools
{
    public static class POFormat
    {
        public static void CreatePO(string filename, Dictionary<string, string> data)
        {
            using (StreamWriter sw = new StreamWriter(filename))
            {
                AddPOHeader(sw);
                foreach (KeyValuePair<string, string> entry in data)
                {
                    //change quotes to \" otherwise there will be format issues
                    string pattern = "\\s?(\"(\\w*?.+?)\")";
                    string msgid = Regex.Replace(entry.Key, pattern, " \"$2\""); //fix somehow the starting whitespace if string starts with quote^^
                    string msgstr = Regex.Replace(entry.Value, pattern, " \"$2\"");

                    sw.WriteLine("msgid \"{0}\"", msgid);
                    sw.WriteLine("msgstr \"{0}\"\n", msgstr);
                }
            }
        }

        public static Dictionary<string, string> OpenPO(string filename)
        {
            Dictionary<string, string> translatedLines = new Dictionary<string, string>();
            List<string> list = new List<string>(File.ReadAllLines(filename));
            //Remove comments
            list.RemoveAll(str => str.StartsWith("#", StringComparison.Ordinal));
            //Remove Header
            list.RemoveRange(0, 12);
            //Remove empty lines
            list.RemoveAll(str => String.IsNullOrEmpty(str));

            // Go through all list data
            int countEntries = list.Count();
            for (int i = 0; i < countEntries;)
            {
                string msgid = Regex.Match(list[i], "msgid \"(.+)\"").Groups[1].Value;
                i++;
                string msgstr = string.Empty;
                if (i < countEntries)
                {
                    msgstr = list[i];
                    msgstr = Regex.Match(msgstr, "msgstr \"(.+)\"").Groups[1].Value;
                    i++;
                }

                //check if key already exists
                if (!translatedLines.ContainsKey(msgid))
                {
                    translatedLines.Add(msgid, msgstr);
                }
            }
            return translatedLines;
        }

        private static void AddPOHeader(StreamWriter sw)
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
            sw.WriteLine("\"Language: de\\n\"");
            sw.WriteLine("\"X-Generator: AGS Translation Editor\\n\"");
            sw.WriteLine();
        }
    }
}