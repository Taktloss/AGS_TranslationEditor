using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using AGSFontEditor.Fonts;
using AGSTools;

namespace AGSFontEditor.Rendering
{
    /// <summary>A sample text drawn with a game font the way the engine looks up its characters.</summary>
    public sealed class TextPreview
    {
        public const uint Background = 0xFF1E1E1E, Ink = 0xFFE8E8E8, MissingMark = 0xC0E04040, WrapMark = 0xFF3A5A7A;

        private TextPreview(PixelCanvas canvas, IReadOnlyList<string> missing)
        {
            Canvas = canvas;
            Missing = missing;
        }

        public PixelCanvas Canvas { get; }

        /// <summary>Characters of the text the font has no glyph for (or that the encoding cannot represent).</summary>
        public IReadOnlyList<string> Missing { get; }

        /// <summary>
        /// Lays out <paramref name="text"/> line by line. Each character is looked up by the code the engine
        /// uses for it (<see cref="FontCoverage.EngineCode"/>); characters without a glyph are marked red.
        /// </summary>
        /// <param name="wrapWidth">If above 0, lines longer than this many pixels wrap at spaces, as in the game;
        /// a dim line marks the width.</param>
        public static TextPreview Render(EditableFont font, string text, TranslationOptions encoding, int wrapWidth = 0)
        {
            const int Margin = 2;
            int lineHeight = font.LineHeight;
            int missingWidth = Math.Max(2, lineHeight / 2);
            var lines = text.Replace("\r\n", "\n").Split('\n');
            if (wrapWidth > 0)
                lines = lines.SelectMany(l => Wrap(l, wrapWidth, s => Measure(font, s, encoding, missingWidth))).ToArray();
            var missing = new List<string>();

            // Glyphs and positions first, to size the canvas
            var placed = new List<(GlyphPixels? Glyph, int X, int Line, int Width)>();
            int maxWidth = 0;
            for (int line = 0; line < lines.Length; line++)
            {
                int x = 0;
                foreach (Rune rune in lines[line].EnumerateRunes())
                {
                    string s = rune.ToString();
                    int code = FontCoverage.EngineCode(s, encoding);
                    GlyphPixels? glyph = code >= 0 ? font.Render(code) : null;
                    // An empty glyph for a visible character is as good as none (some fonts have them for letters they lack)
                    if (glyph != null && !Rune.IsWhiteSpace(rune) && !GlyphComposer.HasInk(glyph.Bitmap))
                        glyph = null;
                    if (glyph == null && !Rune.IsWhiteSpace(rune) && !missing.Contains(s))
                        missing.Add(s);
                    int width = glyph?.Advance ?? missingWidth;
                    placed.Add((glyph, x, line, width));
                    x += width;
                }
                maxWidth = Math.Max(maxWidth, x);
            }

            var canvas = new PixelCanvas(Math.Max(maxWidth, wrapWidth) + Margin * 2, lines.Length * lineHeight + Margin * 2, Background);
            if (wrapWidth > 0)
                canvas.FillRect(Margin + wrapWidth, 0, 1, canvas.Height, WrapMark);
            foreach (var (glyph, x, line, width) in placed)
            {
                int y = Margin + line * lineHeight;
                if (glyph == null)
                    canvas.FillRect(Margin + x, y, width, lineHeight, MissingMark);
                else
                    canvas.DrawGlyph(glyph.Bitmap, Margin + x, y + glyph.Top, Ink, font.BaseShade);
            }
            return new TextPreview(canvas, missing);
        }

        /// <summary>Splits a line at spaces so that each part is at most <paramref name="width"/> wide; a single longer word stays whole.</summary>
        public static IEnumerable<string> Wrap(string line, int width, Func<string, int> measure)
        {
            string[] words = line.Split(' ');
            var current = new StringBuilder();
            foreach (string word in words)
            {
                if (current.Length > 0 && measure(current + " " + word) > width)
                {
                    yield return current.ToString();
                    current.Clear();
                }
                else if (current.Length > 0)
                {
                    current.Append(' ');
                }
                current.Append(word);
            }
            yield return current.ToString();
        }

        private static int Measure(EditableFont font, string text, TranslationOptions encoding, int missingWidth)
        {
            int width = 0;
            foreach (Rune rune in text.EnumerateRunes())
            {
                int code = FontCoverage.EngineCode(rune.ToString(), encoding);
                width += (code >= 0 ? font.Render(code)?.Advance : null) ?? missingWidth;
            }
            return width;
        }
    }
}
