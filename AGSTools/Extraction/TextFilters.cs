using System;
using System.Collections.Generic;
using System.Text.RegularExpressions;

namespace AGSTools
{
    /// <summary>Heuristics that separate player-visible text from technical strings.</summary>
    internal static class TextFilters
    {
        private static readonly string[] InternalPrefixes = { "__", "$", "AGS", "Obj_" };

        private static readonly HashSet<string> KnownInternalStrings = new(StringComparer.Ordinal)
        {
            "on_event", "on_key_press", "on_mouse_click", "game_start", "repeatedly_execute",
            "repeatedly_execute_always", "late_repeatedly_execute_always", "on_call",
            "dialog_request", "getplayercharacter", "IsGamePaused",
        };

        // Default names the AGS editor gives to unnamed room areas
        private static readonly Regex PlaceholderRe = new(
            @"^(No hotspot|Hotspot \d+|Object \d+|Region \d+|Walkable area \d+|Walk-behind area \d+)$",
            RegexOptions.Compiled | RegexOptions.IgnoreCase);

        /// <summary>
        /// Texts starting with "//" cannot be stored in a TRS file (they would be read as comments),
        /// so the AGS editor never includes them either.
        /// </summary>
        public static bool IsStorable(string text) =>
            !string.IsNullOrWhiteSpace(text) && !text.StartsWith("//", StringComparison.Ordinal);

        /// <summary>Texts without any letter ("5", "+10", "#") need no translation.</summary>
        public static bool HasLetters(string text)
        {
            foreach (char c in text)
                if (char.IsLetter(c))
                    return true;
            return false;
        }

        public static bool IsPlaceholderName(string text) => PlaceholderRe.IsMatch(text);

        /// <summary>
        /// Whether a script string literal is likely shown to the player, as opposed to
        /// function names, file names or other identifiers.
        /// </summary>
        public static bool IsLikelyDisplayText(string s)
        {
            if (string.IsNullOrWhiteSpace(s) || s.Length < 2)
                return false;
            if (s.IndexOf('\0') >= 0)
                return false;

            foreach (string prefix in InternalPrefixes)
                if (s.StartsWith(prefix, StringComparison.Ordinal))
                    return false;
            if (KnownInternalStrings.Contains(s))
                return false;

            bool hasSpace = s.IndexOf(' ') >= 0;
            bool hasPunctuation = false;
            int printable = 0;
            foreach (char c in s)
            {
                if (char.IsLetterOrDigit(c))
                {
                    printable++;
                }
                else if (c is ' ' or ',' or '.' or '!' or '?' or '\'' or '"' or '-' or ':' or ';')
                {
                    printable++;
                    hasPunctuation = true;
                }
                else if (c >= 0x20 && c <= 0x7E || c >= 0xA0)
                {
                    printable++;
                }
            }

            if ((double)printable / s.Length < 0.85)
                return false;

            // A single word is likely an identifier, unless it looks like a name ("Wrench")
            if (!hasSpace && !hasPunctuation)
                return s.Length <= 25 && char.IsUpper(s[0]);

            return true;
        }
    }
}
