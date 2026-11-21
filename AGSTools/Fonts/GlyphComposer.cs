using System;
using System.Collections.Generic;
using System.Linq;

namespace AGSTools
{
    /// <summary>
    /// Builds accented letters a font lacks from its base letter, e.g. "ä" from "a" with two dots above.
    /// The result is a starting point to check in the font editor, not a designed glyph.
    /// </summary>
    public static class GlyphComposer
    {
        /// <summary>Letters with a diaeresis (two dots) and their base letter.</summary>
        public static IReadOnlyDictionary<char, char> Diaeresis { get; } = new Dictionary<char, char>
        {
            ['ä'] = 'a', ['ö'] = 'o', ['ü'] = 'u', ['ë'] = 'e',
            ['Ä'] = 'A', ['Ö'] = 'O', ['Ü'] = 'U', ['Ë'] = 'E',
        };

        /// <summary>
        /// The base letter's glyph with two dots above it. Dots get the letter's stroke width and darkest shade, with
        /// one empty row between dots and letter. If there is no room above (capitals), the letter is made shorter
        /// by leaving out rows that repeat the row above them, nearest to its middle first; its bottom stays on the
        /// baseline. Null if the glyph has no ink, is too narrow, or too short to make room.
        /// </summary>
        public static WfnGlyph? AddDiaeresis(WfnGlyph letter)
        {
            if (!InkBounds(letter, out int left, out int right, out int top, out int bottom))
                return null;
            int width = right - left + 1;
            int dot = Math.Clamp(StrokeWidth(letter, top, bottom), 1, Math.Max(1, (width - 1) / 3));
            if (width < 2 * dot + 1)
                return null;

            int needed = dot + 1; // dot rows and one empty row above the letter
            WfnGlyph result = letter.Clone();
            if (top < needed)
            {
                int missing = needed - top;
                // The letter must keep at least 3 rows
                if (bottom - top + 1 - missing < 3)
                    return null;
                result = DropRows(letter, top, bottom, missing);
                top += missing;
            }

            int ink = 0;
            for (int y = 0; y < letter.Height; y++)
                for (int x = 0; x < letter.Width; x++)
                    ink = Math.Max(ink, letter.GetShade(x, y));

            int margin = width >= 5 ? 1 : 0;
            int gap = Math.Max(1, width - 2 * dot - 2 * margin);
            int first = left + (width - (2 * dot + gap)) / 2;
            int second = first + dot + gap;
            int dotTop = top - 1 - dot;
            for (int y = dotTop; y < dotTop + dot; y++)
            {
                for (int i = 0; i < dot; i++)
                {
                    result.SetShade(first + i, y, ink);
                    result.SetShade(second + i, y, ink);
                }
            }
            return result;
        }

        /// <summary>True if any pixel of the glyph is drawn. Some fonts have empty glyphs for letters they lack.</summary>
        public static bool HasInk(WfnGlyph? glyph) => glyph != null && InkBounds(glyph, out _, out _, out _, out _);

        private static bool InkBounds(WfnGlyph glyph, out int left, out int right, out int top, out int bottom)
        {
            left = glyph.Width;
            top = glyph.Height;
            right = bottom = -1;
            for (int y = 0; y < glyph.Height; y++)
            {
                for (int x = 0; x < glyph.Width; x++)
                {
                    if (glyph.GetShade(x, y) == 0)
                        continue;
                    left = Math.Min(left, x);
                    right = Math.Max(right, x);
                    top = Math.Min(top, y);
                    bottom = Math.Max(bottom, y);
                }
            }
            return right >= 0;
        }

        // The most common length of horizontal ink runs that are not the whole row: the width of vertical strokes
        private static int StrokeWidth(WfnGlyph glyph, int top, int bottom)
        {
            var runs = new Dictionary<int, int>();
            for (int y = top; y <= bottom; y++)
            {
                int run = 0;
                for (int x = 0; x <= glyph.Width; x++)
                {
                    if (x < glyph.Width && glyph.GetShade(x, y) != 0)
                    {
                        run++;
                        continue;
                    }
                    if (run > 0)
                        runs[run] = runs.GetValueOrDefault(run) + 1;
                    run = 0;
                }
            }
            return runs.Count == 0 ? 1 : runs.OrderByDescending(r => r.Value).ThenBy(r => r.Key).First().Key;
        }

        // Leaves out `count` rows between top and bottom (not those two); rows above them move down
        private static WfnGlyph DropRows(WfnGlyph glyph, int top, int bottom, int count)
        {
            double middle = (top + bottom) / 2.0;
            var candidates = Enumerable.Range(top + 1, Math.Max(0, bottom - top - 1)).ToList();
            bool Repeats(int y) => Enumerable.Range(0, glyph.Width).All(x => glyph.GetShade(x, y) == glyph.GetShade(x, y - 1));
            var drop = candidates
                .OrderBy(y => Repeats(y) ? 0 : 1)
                .ThenBy(y => Math.Abs(y - middle))
                .Take(count)
                .ToHashSet();

            var result = new WfnGlyph(glyph.Width, glyph.Height, null, glyph.BitsPerPixel);
            int target = glyph.Height - 1;
            for (int y = glyph.Height - 1; y >= 0; y--)
            {
                if (drop.Contains(y))
                    continue;
                for (int x = 0; x < glyph.Width; x++)
                    result.SetShade(x, target, glyph.GetShade(x, y));
                target--;
            }
            return result;
        }
    }
}
