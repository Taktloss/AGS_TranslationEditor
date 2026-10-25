using System;
using System.Collections.Generic;
using System.Text;

namespace AGSTools
{
    /// <summary>Game data format versions (kGameVersion_* in Common/ac/game_version.h).</summary>
    internal static class GameDataVersion
    {
        public const int V230 = 12;
        public const int V254 = 21;
        public const int V255 = 22;
        public const int V256 = 24;
        public const int V261 = 26;
        public const int V270 = 31;
        public const int V272 = 32;
        public const int V301 = 36;
        public const int V310 = 37;
        public const int V320 = 41;
        public const int V330 = 43;
        public const int V341 = 48;
        public const int V341_2 = 49;
        public const int V350 = 50;
        public const int V351 = 3050100;
        public const int V360 = 3060000;
        public const int V362_07 = 3060207;
        public const int V362_12 = 3060212;
        public const int V363 = 3060300;
        public const int Current = 3060316;
    }

    /// <summary>A dialog topic with its option texts.</summary>
    public sealed record DialogInfo(string ScriptName, IReadOnlyList<string> Options);

    /// <summary>Kind of a GUI control that carries text.</summary>
    public enum GuiTextKind { Button, Label, ListBoxItem, TextBox }

    /// <summary>A text on a GUI control.</summary>
    public sealed record GuiText(GuiTextKind Kind, string ControlName, string Text);

    /// <summary>Settings of a game font (FontInfo in Common/ac/gamestructdefines.h).</summary>
    /// <param name="Size">Point size for TTF fonts (or a scaling multiplier, see <see cref="SizeIsMultiplier"/>).</param>
    /// <param name="Outline">-1 none, -10 automatic, otherwise the number of the outline font.</param>
    public sealed record FontInfo(int Number, int Size, int Outline, int YOffset, int LineSpacing, int Flags)
    {
        public const int NoOutline = -1;
        public const int AutoOutline = -10;

        public bool SizeIsMultiplier => (Flags & 0x01) != 0;
    }

    /// <summary>
    /// The parts of an AGS game's main data (game28.dta / ac2game.dta) that matter for translation.
    /// </summary>
    public sealed class GameData
    {
        /// <summary>Game data format version (kGameVersion_*).</summary>
        public int DataVersion { get; internal set; }

        /// <summary>Editor version the game was compiled with, e.g. "3.6.1.35".</summary>
        public string EngineVersion { get; internal set; } = "";

        /// <summary>Game name as the engine compares it with a translation's GameName.</summary>
        public string Title { get; internal set; } = "";

        /// <summary>Unique game id as the engine compares it with a translation's GameID.</summary>
        public int UniqueId { get; internal set; }

        public string Guid { get; internal set; } = "";

        /// <summary>Codepage of game texts (65001 = UTF-8, 0 = default 8-bit codepage).</summary>
        public int TextCodepage { get; internal set; }

        public bool IsUnicode => TextCodepage == AgsTextEncoding.Utf8Codepage;

        /// <summary>Encoding to decode texts with (UTF-8 or the game's 8-bit codepage).</summary>
        public Encoding TextEncoding => AgsTextEncoding.ForCodepage(TextCodepage);

        public List<string> CharacterNames { get; } = new();
        public List<string> InventoryNames { get; } = new();
        public List<string> GlobalMessages { get; } = new();
        public List<DialogInfo> Dialogs { get; } = new();

        /// <summary>Dialog lines of pre-3.1.1 games, which kept them outside of scripts.</summary>
        public List<string> OldDialogLines { get; } = new();

        public List<GuiText> GuiTexts { get; } = new();
        public List<string> ParserWords { get; } = new();

        /// <summary>Game information map (v3.6.3+: description, developer, genre, ...).</summary>
        public Dictionary<string, string> GameInfo { get; } = new(StringComparer.OrdinalIgnoreCase);

        /// <summary>Settings of the game fonts, indexed by font number.</summary>
        public List<FontInfo> Fonts { get; } = new();

        internal List<CompiledScript> Scripts { get; } = new();

        /// <summary>
        /// Null if all data was read; otherwise a description of the section where reading stopped.
        /// Everything read before that point is still available.
        /// </summary>
        public string? Error { get; internal set; }

        public bool IsComplete => Error == null;
    }
}
