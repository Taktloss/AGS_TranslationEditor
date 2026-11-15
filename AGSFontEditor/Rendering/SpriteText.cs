using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using AGSFontEditor.Fonts;
using AGSTools;

namespace AGSFontEditor.Rendering
{
    public enum TextAlign { Left, Center, Right }

    /// <summary>Draws text with a game font into a sprite, pixel by pixel like the engine draws it.</summary>
    public static class SpriteText
    {
        /// <summary>
        /// Draws <paramref name="text"/> (one or more lines) into <paramref name="image"/>. Lines start at
        /// <paramref name="y"/> and are aligned within the span from <paramref name="x"/> to <paramref name="x"/> + <paramref name="width"/>.
        /// With an <paramref name="outline"/> color, every glyph is first drawn one pixel around in that color.
        /// </summary>
        /// <returns>The characters the font has no glyph for.</returns>
        public static List<string> Draw(SpriteImage image, EditableFont font, string text, int x, int y, int width,
            TextAlign align, uint color, uint? outline, TranslationOptions encoding)
        {
            var missing = new List<string>();
            int lineY = y;
            foreach (string line in text.Replace("\r\n", "\n").Split('\n'))
            {
                var glyphs = new List<(GlyphPixels? Glyph, int Advance)>();
                foreach (Rune rune in line.EnumerateRunes())
                {
                    string s = rune.ToString();
                    int code = FontCoverage.EngineCode(s, encoding);
                    GlyphPixels? glyph = code >= 0 ? font.Render(code) : null;
                    if (glyph == null && rune.Value != ' ' && !missing.Contains(s))
                        missing.Add(s);
                    glyphs.Add((glyph, glyph?.Advance ?? Math.Max(2, font.LineHeight / 3)));
                }
                int lineWidth = glyphs.Sum(g => g.Advance);
                int penX = align switch
                {
                    TextAlign.Center => x + (width - lineWidth) / 2,
                    TextAlign.Right => x + width - lineWidth,
                    _ => x,
                };
                if (outline is uint o)
                {
                    int px = penX;
                    foreach (var (glyph, advance) in glyphs)
                    {
                        if (glyph != null)
                            for (int dy = -1; dy <= 1; dy++)
                                for (int dx = -1; dx <= 1; dx++)
                                    if (dx != 0 || dy != 0)
                                        DrawGlyph(image, glyph.Bitmap, px + dx, lineY + glyph.Top + dy, o, font.BaseShade, solid: true);
                        px += advance;
                    }
                }
                foreach (var (glyph, advance) in glyphs)
                {
                    if (glyph != null)
                        DrawGlyph(image, glyph.Bitmap, penX, lineY + glyph.Top, color, font.BaseShade, solid: false);
                    penX += advance;
                }
                lineY += font.LineHeight;
            }
            return missing;
        }

        /// <summary>Width and height of the text block in pixels.</summary>
        public static (int Width, int Height) Measure(EditableFont font, string text, TranslationOptions encoding)
        {
            string[] lines = text.Replace("\r\n", "\n").Split('\n');
            int width = 0;
            foreach (string line in lines)
            {
                int w = 0;
                foreach (Rune rune in line.EnumerateRunes())
                {
                    int code = FontCoverage.EngineCode(rune.ToString(), encoding);
                    w += (code >= 0 ? font.Render(code)?.Advance : null) ?? Math.Max(2, font.LineHeight / 3);
                }
                width = Math.Max(width, w);
            }
            return (width, lines.Length * font.LineHeight);
        }

        private static void DrawGlyph(SpriteImage image, WfnGlyph glyph, int x, int y, uint color, int baseShade, bool solid)
        {
            for (int gy = 0; gy < glyph.Height; gy++)
            {
                int py = y + gy;
                if (py < 0 || py >= image.Height)
                    continue;
                for (int gx = 0; gx < glyph.Width; gx++)
                {
                    int px = x + gx;
                    int shade = glyph.GetShade(gx, gy);
                    if (px < 0 || px >= image.Width || shade == 0)
                        continue;
                    image[px, py] = solid || glyph.BitsPerPixel == 1 ? color : PixelCanvas.ShadeColor(color, shade, baseShade);
                }
            }
        }
    }
}
