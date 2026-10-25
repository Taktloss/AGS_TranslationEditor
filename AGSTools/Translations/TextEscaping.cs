using System.Text;

namespace AGSTools
{
    /// <summary>
    /// Conversion between TRS text (as written in script source, with escape sequences)
    /// and runtime text (what the engine looks up, as stored in compiled scripts and TRA files).
    /// </summary>
    public static class TextEscaping
    {
        /// <summary>
        /// TRS → runtime, exactly like the AGS translation compiler:
        /// <c>StrUtil::Unescape(PreprocessLineForOldStyleLinebreaks(text))</c>.
        /// "\[" is kept as "\[" (the engine shows it as a literal bracket), "\n", "\t", "\"", "\\" etc.
        /// are resolved, and an unknown escape "\x" becomes "x".
        /// </summary>
        public static string ToRuntime(string trsText)
        {
            if (trsText.IndexOf('\\') < 0)
                return trsText;

            var sb = new StringBuilder(trsText.Length);
            for (int i = 0; i < trsText.Length; i++)
            {
                char c = trsText[i];
                if (c != '\\' || i + 1 >= trsText.Length)
                {
                    sb.Append(c);
                    continue;
                }

                char next = trsText[++i];
                switch (next)
                {
                    case '[': sb.Append("\\["); break; // old-style escaped bracket survives
                    case 'a': sb.Append('\a'); break;
                    case 'b': sb.Append('\b'); break;
                    case 'f': sb.Append('\f'); break;
                    case 'n': sb.Append('\n'); break;
                    case 'r': sb.Append('\r'); break;
                    case 't': sb.Append('\t'); break;
                    case 'v': sb.Append('\v'); break;
                    default: sb.Append(next); break; // \\ \' \" \? and unknown escapes
                }
            }
            return sb.ToString();
        }

        /// <summary>
        /// Runtime → TRS, the inverse of <see cref="ToRuntime"/>: backslashes and control characters
        /// are escaped so that compiling the TRS reproduces the original runtime text.
        /// </summary>
        public static string ToTrs(string runtimeText)
        {
            if (runtimeText.IndexOfAny(SpecialChars) < 0)
                return runtimeText;

            var sb = new StringBuilder(runtimeText.Length + 8);
            for (int i = 0; i < runtimeText.Length; i++)
            {
                char c = runtimeText[i];
                switch (c)
                {
                    case '\\':
                        // "\[" maps to itself; every other backslash must be doubled
                        bool beforeBracket = i + 1 < runtimeText.Length && runtimeText[i + 1] == '[';
                        sb.Append(beforeBracket ? "\\" : "\\\\");
                        break;
                    case '\n': sb.Append("\\n"); break;
                    case '\r': sb.Append("\\r"); break;
                    case '\t': sb.Append("\\t"); break;
                    case '\a': sb.Append("\\a"); break;
                    case '\b': sb.Append("\\b"); break;
                    case '\f': sb.Append("\\f"); break;
                    case '\v': sb.Append("\\v"); break;
                    default: sb.Append(c); break;
                }
            }
            return sb.ToString();
        }

        /// <summary>
        /// True if the text has a backslash before a character that is no escape sequence, e.g. "f\r"
        /// is fine but "m\ssen" is not. The AGS compiler drops such backslashes, so a TRS containing
        /// them was written for AGS before 3.5.1, which took backslashes literally.
        /// </summary>
        public static bool HasInvalidEscape(string trsText)
        {
            for (int i = trsText.IndexOf('\\'); i >= 0 && i + 1 < trsText.Length; i = trsText.IndexOf('\\', i + 1))
            {
                if (ValidEscapes.IndexOf(trsText[i + 1]) < 0)
                    return true;
                i++; // skip the escaped character, e.g. the second backslash of "\\"
            }
            return false;
        }

        private const string ValidEscapes = "abfnrtv\\'\"?[";

        private static readonly char[] SpecialChars = { '\\', '\n', '\r', '\t', '\a', '\b', '\f', '\v' };
    }
}
