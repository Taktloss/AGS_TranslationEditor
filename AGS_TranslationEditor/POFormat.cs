using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace AGS_TranslationEditor
{
    public static class POFormat
    {
        static public void CreatePO(string filename, Dictionary<string, string> data)
        {
            using (StreamWriter sw = new StreamWriter(filename))
            {
                AddPOHeader(sw);
                foreach (KeyValuePair<string, string> entry in data)
                {
                    //remove quotes btw. change them to ' because of format issues
                    string msgid = entry.Key;
                    msgid = msgid.Replace('\"', '\'');
                    string msgstr = entry.Value;
                    msgstr = msgstr.Replace('\"', '\'');

                    sw.WriteLine("msgid \"{0}\"", msgid);
                    sw.WriteLine("msgstr \"{0}\"\n", msgstr);
                }
            }
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
            sw.WriteLine("\"Content-Type: text/plain; UTF-8\\n\"");
            sw.WriteLine("\"Content-Transfer-Encoding: 8bit\\n\"");
            sw.WriteLine("\"Language: de\\n\"");
            sw.WriteLine("\"X-Generator: AGS Translation Editor\\n\"");
            sw.WriteLine();
        }

        static public Dictionary<string, string> OpenPO(string filename)
        {
            Dictionary<string, string> translatedLines = new Dictionary<string, string>();
            List<string> list = new List<string>(File.ReadAllLines(filename));
            //Look for comments and remove them
            list.RemoveAll(str => str.StartsWith("#", StringComparison.Ordinal));
            //Remove Header
            list.RemoveRange(0, 12);
            //Remove empty lines
            list.RemoveAll(str => String.IsNullOrEmpty(str));

            // Go through all list data
            int length = list.Count();
            for (int i = 0; i < length;)
            {
                string msgid = list[i];
                i++;
                string msgstr = string.Empty;
                if (i < length)
                {
                    msgstr = list[i];
                    i++;
                }

                //Check for already existing entry/key
                if (!translatedLines.ContainsKey(msgid))
                    translatedLines.Add(msgid.Remove(0, msgid.IndexOf(' ') + 1).Replace("\"", string.Empty), msgstr.Remove(0, msgid.IndexOf(' ') + 1).Replace("\"", string.Empty));
            }
            return translatedLines;
        }
    }
}