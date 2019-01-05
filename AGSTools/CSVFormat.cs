using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading.Tasks;

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
                    string msgid = entry.Key;
                    string msgstr = entry.Value;

                    sw.WriteLine("{0}{1}{2}", msgid , seperator, msgstr);
                }
            }
        }

        public static Dictionary<string, string> OpenCSV(string filename)
        {
            Dictionary<string, string> translatedLines = new Dictionary<string, string>();
            List<string> list = new List<string>(File.ReadAllLines(filename));

            //Remove empty lines
            list.RemoveAll(str => String.IsNullOrEmpty(str));

            int length = list.Count();
            for (int i = 0; i < length; i++)
            {
                string[] line = list[i].Split( ';', '\t');
                string msgid = line[0];
                string msgstr = line[1];
                
                //Check for already existing entry/key
                if (!translatedLines.ContainsKey(msgid))
                    translatedLines.Add(msgid, msgstr);
            }
            return translatedLines;
        }

    }
}
