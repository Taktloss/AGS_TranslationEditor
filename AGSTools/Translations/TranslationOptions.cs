using System;
using System.Collections.Generic;

namespace AGSTools
{
    /// <summary>Text direction override of a translation.</summary>
    public enum TextDirection
    {
        Default = -1,
        LeftToRight = 1,
        RightToLeft = 2,
    }

    /// <summary>
    /// Translation settings, stored as "//#Key=Value" lines in TRS files and as option blocks in TRA files
    /// (see Tools/data/tra_utils.cpp and Common/data/tra_file.cpp in the AGS sources).
    /// </summary>
    public sealed class TranslationOptions
    {
        /// <summary>Game's unique id; 0 lets the translation load for any game.</summary>
        public int GameUid { get; set; }

        /// <summary>Game name; empty lets the translation load for any game.</summary>
        public string GameName { get; set; } = "";

        /// <summary>Replacement for the normal font, or -1 for the game's default.</summary>
        public int NormalFont { get; set; } = -1;

        /// <summary>Replacement for the speech font, or -1 for the game's default.</summary>
        public int SpeechFont { get; set; } = -1;

        public TextDirection TextDirection { get; set; } = TextDirection.Default;

        /// <summary>Text encoding of the translation: "UTF-8", or empty/"ASCII" for 8-bit text.</summary>
        public string Encoding { get; set; } = "";

        /// <summary>Encoding of the game the translation was made for (informational).</summary>
        public string GameEncoding { get; set; } = "";

        /// <summary>Language hint such as "de" or "pt_BR".</summary>
        public string Language { get; set; } = "";

        /// <summary>Whether the engine translates Parser.Said() arguments automatically.</summary>
        public bool AutoTranslateParserSaid { get; set; }

        /// <summary>
        /// Font overrides ("//#Font3=File=x.ttf;Size=12") by font number, kept verbatim.
        /// They are preserved in TRS files but not compiled into TRA files.
        /// </summary>
        public SortedDictionary<int, string> FontOverrides { get; } = new();

        /// <summary>
        /// Codepage of 8-bit text (when <see cref="Encoding"/> is not UTF-8): the game's text codepage.
        /// Not stored in translation files, which is why it should be set from the game when known.
        /// </summary>
        public int AnsiCodepage { get; set; } = AgsTextEncoding.DefaultAnsiCodepage;

        public bool IsUtf8 => IsUtf8Name(Encoding);

        /// <summary>
        /// Encoding of the translation texts: UTF-8, the codepage named by <see cref="GameEncoding"/>,
        /// or <see cref="AnsiCodepage"/>.
        /// </summary>
        public System.Text.Encoding GetTextEncoding()
        {
            if (IsUtf8)
                return AgsTextEncoding.Utf8;
            int? hinted = AgsTextEncoding.ParseCodepage(GameEncoding);
            return AgsTextEncoding.ForCodepage(hinted is int cp && cp != AgsTextEncoding.Utf8Codepage ? cp : AnsiCodepage);
        }

        internal static bool IsUtf8Name(string? encoding) =>
            string.Equals(encoding, "UTF-8", StringComparison.OrdinalIgnoreCase) ||
            string.Equals(encoding, "UTF8", StringComparison.OrdinalIgnoreCase);

        public TranslationOptions Clone()
        {
            var copy = new TranslationOptions
            {
                GameUid = GameUid,
                GameName = GameName,
                NormalFont = NormalFont,
                SpeechFont = SpeechFont,
                TextDirection = TextDirection,
                Encoding = Encoding,
                GameEncoding = GameEncoding,
                Language = Language,
                AutoTranslateParserSaid = AutoTranslateParserSaid,
                AnsiCodepage = AnsiCodepage,
            };
            foreach (var kv in FontOverrides)
                copy.FontOverrides[kv.Key] = kv.Value;
            return copy;
        }
    }
}
