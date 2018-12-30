using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace AGS_TranslationEditor
{
    public static class CSVFormat
    {
        static public void CreateCSV(string filename, Dictionary<string, string> data)
        {
            using (StreamWriter sw = new StreamWriter(filename))
            {
                foreach (KeyValuePair<string, string> entry in data)
                {
                    //remove quotes btw. change them to ' because of format issues
                    string msgid = entry.Key;
                    msgid = msgid.Replace('\"', '\'');
                    string msgstr = entry.Value;
                    msgstr = msgstr.Replace('\"', '\'');

                    sw.WriteLine("{0};{1}", msgid , msgstr);
                }
            }
        }

        static public Dictionary<string, string> OpenCSV(string filename)
        {
            Dictionary<string, string> translatedLines = new Dictionary<string, string>();
            List<string> list = new List<string>(File.ReadAllLines(filename));
            //Look for comments and remove them
            list.RemoveAll(str => str.StartsWith("#", StringComparison.Ordinal));
            //Remove Header
            //list.RemoveRange(0, 12);
            //Remove empty lines
            list.RemoveAll(str => String.IsNullOrEmpty(str));

            // Go through all list data
            int length = list.Count();



            for (int i = 0; i < length;i++)
            {
                string[] line = list[i].Split( ';', '\t');
                string msgid = line[0];
                string msgstr = line[1];
                
                //Check for already existing entry/key
                if (!translatedLines.ContainsKey(msgid))
                    translatedLines.Add(msgid.Replace("\"", string.Empty), msgstr.Replace("\"", string.Empty));
            }
            return translatedLines;
        }

    }
}
