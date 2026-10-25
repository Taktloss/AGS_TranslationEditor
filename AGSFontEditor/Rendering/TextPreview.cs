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
        public const uint Background = 0xFF1E1E1E, Ink = 0xFFE8E8E8, MissingMark = 0xC0E04040;

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
        public static TextPreview Render(EditableFont font, string text, TranslationOptions encoding)
        {
            const int Margin = 2;
            int lineHeight = font.LineHeight;
            int missingWidth = Math.Max(2, lineHeight / 2);
            var lines = text.Replace("\r\n", "\n").Split('\n');
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
                    if (glyph == null && rune.Value != ' ' && !missing.Contains(s))
                        missing.Add(s);
                    int width = glyph?.Advance ?? missingWidth;
                    placed.Add((glyph, x, line, width));
                    x += width;
                }
                maxWidth = Math.Max(maxWidth, x);
            }

            var canvas = new PixelCanvas(maxWidth + Margin * 2, lines.Length * lineHeight + Margin * 2, Background);
            foreach (var (glyph, x, line, width) in placed)
            {
                int y = Margin + line * lineHeight;
                if (glyph == null)
                    canvas.FillRect(Margin + x, y, width, lineHeight, MissingMark);
                else
                    canvas.DrawGlyph(glyph.Bitmap, Margin + x, y + glyph.Top, Ink);
            }
            return new TextPreview(canvas, missing);
        }
    }
}
