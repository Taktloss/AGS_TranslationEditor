using System;
using System.Collections.Generic;
using System.IO;
using System.Text;

namespace AGSTools
{
    /// <summary>Minimal editing of INI files like acsetup.cfg that keeps all other lines as they are.</summary>
    internal static class IniFile
    {
        /// <summary>Value of <paramref name="key"/> in <paramref name="section"/>, or null.</summary>
        public static string? GetValue(string path, string section, string key)
        {
            if (!File.Exists(path))
                return null;
            string current = "";
            foreach (string raw in File.ReadAllLines(path))
            {
                string line = raw.Trim();
                if (line.StartsWith('[') && line.EndsWith(']'))
                    current = line[1..^1].Trim();
                else if (current.Equals(section, StringComparison.OrdinalIgnoreCase) && KeyOf(line) is { } k
                    && k.Equals(key, StringComparison.OrdinalIgnoreCase))
                    return line[(line.IndexOf('=') + 1)..].Trim();
            }
            return null;
        }

        /// <summary>Sets <paramref name="key"/> in <paramref name="section"/>, adding the section or key if needed.</summary>
        public static void SetValue(string path, string section, string key, string value)
        {
            string text = File.Exists(path) ? File.ReadAllText(path) : "";
            string newline = text.Contains("\r\n", StringComparison.Ordinal) ? "\r\n" : "\n";
            var lines = new List<string>(text.Split('\n'));
            for (int i = 0; i < lines.Count; i++)
                lines[i] = lines[i].TrimEnd('\r');
            if (lines.Count > 0 && lines[^1].Length == 0)
                lines.RemoveAt(lines.Count - 1);

            string entry = $"{key}={value}";
            int sectionStart = -1, sectionEnd = lines.Count;
            for (int i = 0; i < lines.Count; i++)
            {
                string line = lines[i].Trim();
                if (!line.StartsWith('[') || !line.EndsWith(']'))
                    continue;
                if (sectionStart >= 0)
                {
                    sectionEnd = i;
                    break;
                }
                if (line[1..^1].Trim().Equals(section, StringComparison.OrdinalIgnoreCase))
                    sectionStart = i;
            }

            if (sectionStart < 0)
            {
                lines.Add($"[{section}]");
                lines.Add(entry);
            }
            else
            {
                int keyLine = -1;
                for (int i = sectionStart + 1; i < sectionEnd; i++)
                    if (KeyOf(lines[i].Trim()) is { } k && k.Equals(key, StringComparison.OrdinalIgnoreCase))
                        keyLine = i;
                if (keyLine >= 0)
                    lines[keyLine] = entry;
                else
                    lines.Insert(sectionStart + 1, entry);
            }

            string result = string.Join(newline, lines) + newline;
            AtomicFile.Write(path, s => s.Write(new UTF8Encoding(false).GetBytes(result)));
        }

        private static string? KeyOf(string line)
        {
            int eq = line.IndexOf('=');
            return eq > 0 && !line.StartsWith(';') && !line.StartsWith('#') ? line[..eq].Trim() : null;
        }
    }
}
