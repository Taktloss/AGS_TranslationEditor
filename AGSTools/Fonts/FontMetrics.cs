using System;
using System.Collections.Generic;
using System.Text;

namespace AGSTools
{
    /// <summary>
    /// Text width and line breaking in a game font, in game pixels. Exact for WFN fonts; for TrueType
    /// fonts the advance widths are scaled to the font size, without hinting and kerning, so results
    /// can differ from the engine by a pixel or two.
    /// </summary>
    public sealed class FontMetrics
    {
        private readonly Func<int, int> _advanceTimes1000; // engine character code -> width × 1000

        private FontMetrics(Func<int, int> advanceTimes1000, int height, int lineHeight, bool isApproximate)
        {
            _advanceTimes1000 = advanceTimes1000;
            Height = height;
            LineHeight = lineHeight;
            IsApproximate = isApproximate;
        }

        /// <summary>Height of the font's characters.</summary>
        public int Height { get; }

        /// <summary>Distance between two lines: the font's line spacing setting, else its height.</summary>
        public int LineHeight { get; }

        /// <summary>True for TrueType fonts, whose widths are only estimated.</summary>
        public bool IsApproximate { get; }

        /// <summary>Metrics of a game font, or null if its size is unknown or the file could not be read.</summary>
        public static FontMetrics? For(GameFont font)
        {
            FontInfo? info = font.Info;
            if (font.Wfn is { } wfn && wfn.Glyphs.Count > 0)
            {
                int scale = info is { SizeIsMultiplier: true, Size: > 1 } ? info.Size : 1;
                int height = wfn.Height * scale;
                return new FontMetrics(code => (wfn.GetGlyph(code)?.Width ?? 0) * scale * 1000, height, SpacingFor(info, height), false);
            }
            if (font.Kind == FontKind.Ttf && font.Error == null && info is { SizeIsMultiplier: false, Size: > 0 })
            {
                TtfEditor ttf;
                try
                {
                    ttf = TtfEditor.Load(font.Data);
                }
                catch (AgsFormatException)
                {
                    return null;
                }
                int size = info.Size;
                int height = (int)Math.Ceiling((ttf.Ascender - ttf.Descender) * (double)size / ttf.UnitsPerEm);
                return new FontMetrics(
                    code => (int)Math.Round(ttf.GetAdvance(ttf.GetGlyphIndex(code)) * 1000.0 * size / ttf.UnitsPerEm),
                    height, SpacingFor(info, height), true);
            }
            return null;
        }

        private static int SpacingFor(FontInfo? info, int height) => info is { LineSpacing: > 0 } ? info.LineSpacing : height;

        /// <summary>Width of a single line of runtime text.</summary>
        public int MeasureWidth(string text, TranslationOptions options)
        {
            long total = 0;
            foreach (Rune rune in text.EnumerateRunes())
            {
                int code = FontCoverage.EngineCode(rune.ToString(), options);
                if (code >= 0)
                    total += _advanceTimes1000(code);
            }
            return (int)((total + 999) / 1000);
        }

        /// <summary>
        /// Splits runtime text into lines like the engine: "[" and newlines start a new line, and words
        /// move to the next line when the line would get wider than <paramref name="width"/>.
        /// A single word that is too wide stays on its own line.
        /// </summary>
        public List<string> Wrap(string text, int width, TranslationOptions options)
        {
            var lines = new List<string>();
            foreach (string paragraph in SplitForcedBreaks(text))
            {
                string line = "";
                foreach (string word in paragraph.Split(' '))
                {
                    string candidate = line.Length == 0 ? word : line + " " + word;
                    if (line.Length > 0 && MeasureWidth(candidate, options) > width)
                    {
                        lines.Add(line);
                        line = word;
                    }
                    else
                    {
                        line = candidate;
                    }
                }
                lines.Add(line);
            }
            return lines;
        }

        // "[" is the AGS line break; "\[" is a literal bracket
        private static IEnumerable<string> SplitForcedBreaks(string text)
        {
            var current = new StringBuilder();
            for (int i = 0; i < text.Length; i++)
            {
                char c = text[i];
                if (c == '\\' && i + 1 < text.Length && text[i + 1] == '[')
                {
                    current.Append('[');
                    i++;
                }
                else if (c is '[' or '\n')
                {
                    yield return current.ToString();
                    current.Clear();
                }
                else
                {
                    current.Append(c);
                }
            }
            yield return current.ToString();
        }
    }
}
