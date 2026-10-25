using System;
using System.Globalization;
using System.Text;

namespace AGSTools
{
    /// <summary>
    /// Text encodings of AGS games and translations: UTF-8, or an 8-bit Windows codepage
    /// (the game's text codepage; the AGS editor writes 8-bit TRS files in the system ANSI codepage).
    /// All supported 8-bit codepages map every byte to a character and back, so no data is lost.
    /// </summary>
    public static class AgsTextEncoding
    {
        public const int Utf8Codepage = 65001;
        public const int DefaultAnsiCodepage = 1252;

        public static readonly Encoding Utf8 = new UTF8Encoding(false);

        static AgsTextEncoding() => Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);

        /// <summary>
        /// Encoding for a codepage as stored in the game options. 0 (unset) means the default
        /// 8-bit codepage; unknown codepages fall back to it as well.
        /// </summary>
        public static Encoding ForCodepage(int codepage)
        {
            if (codepage == Utf8Codepage)
                return Utf8;
            if (codepage > 0)
            {
                try
                {
                    return Encoding.GetEncoding(codepage);
                }
                catch (Exception ex) when (ex is ArgumentException or NotSupportedException)
                {
                    // fall through to the default
                }
            }
            return Encoding.GetEncoding(DefaultAnsiCodepage);
        }

        /// <summary>
        /// Parses an encoding hint such as "UTF-8", "1251", "CP1251" or "windows-1251".
        /// Returns null for empty or unknown hints (including "ASCII", which means "8-bit, game codepage").
        /// </summary>
        public static int? ParseCodepage(string? hint)
        {
            if (string.IsNullOrWhiteSpace(hint))
                return null;
            hint = hint.Trim();
            if (TranslationOptions.IsUtf8Name(hint))
                return Utf8Codepage;

            string digits = hint;
            foreach (string prefix in new[] { "windows-", "cp" })
                if (digits.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
                    digits = digits[prefix.Length..];
            if (int.TryParse(digits, NumberStyles.None, CultureInfo.InvariantCulture, out int cp))
                return cp;

            try
            {
                int named = Encoding.GetEncoding(hint).CodePage;
                return named == Encoding.ASCII.CodePage ? null : named;
            }
            catch (ArgumentException)
            {
                return null;
            }
        }

        /// <summary>Whether <paramref name="text"/> can be stored in <paramref name="encoding"/> without loss.</summary>
        internal static bool CanEncode(Encoding encoding, string text)
        {
            if (encoding.CodePage == Utf8Codepage)
                return true;
            foreach (char c in text)
                if (c >= 0x80)
                    return encoding.GetString(encoding.GetBytes(text)) == text;
            return true;
        }
    }
}
