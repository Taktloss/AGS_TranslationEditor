using System;
using System.Collections.Generic;
using System.Linq;

namespace AGSTools
{
    /// <summary>
    /// The pixel grid of a TrueType pixel font: every pixel is a square of <see cref="Size"/> font units,
    /// aligned to the origin. Converts glyph outlines to bitmaps and back, so pixel glyphs can be edited
    /// like WFN glyphs.
    /// </summary>
    /// <remarks>
    /// Bitmap row 0 is the top row; its top edge is <see cref="TopRow"/> pixels above the baseline.
    /// Column 0 starts at x = 0 (ink left of the origin is not represented).
    /// </remarks>
    public sealed class TtfPixelGrid
    {
        public TtfPixelGrid(int size, int topRow, int rows)
        {
            if (size <= 0) throw new ArgumentOutOfRangeException(nameof(size));
            if (rows <= 0) throw new ArgumentOutOfRangeException(nameof(rows));
            Size = size;
            TopRow = topRow;
            Rows = rows;
        }

        /// <summary>Font units per pixel.</summary>
        public int Size { get; }

        /// <summary>Pixels from the baseline up to the top edge of bitmap row 0.</summary>
        public int TopRow { get; }

        /// <summary>Bitmap height in pixels.</summary>
        public int Rows { get; }

        /// <summary>Bitmap row whose bottom edge is the baseline.</summary>
        public int BaselineRow => TopRow - 1;

        /// <summary>
        /// Finds the grid of a pixel font: the largest unit size that at least 90% of all outline
        /// coordinates are a multiple of. Returns null for fonts with curves or without a clear grid.
        /// </summary>
        public static TtfPixelGrid? Detect(TtfEditor font)
        {
            var outlines = font.SimpleOutlines().ToList();
            var points = outlines.SelectMany(o => o.Contours.SelectMany(c => c)).ToList();
            if (points.Count == 0 || points.Count(p => !p.OnCurve) > points.Count / 20)
                return null;

            var coords = points.SelectMany(p => new[] { Math.Abs(p.X), Math.Abs(p.Y) }).ToList();
            int threshold = coords.Count * 9 / 10;
            // At least 4 and at most 256 pixels per em
            for (int size = font.UnitsPerEm / 4; size >= Math.Max(2, font.UnitsPerEm / 256); size--)
            {
                if (coords.Count(c => c % size == 0) >= threshold)
                    return ForSize(font, size, outlines);
            }
            return null;
        }

        /// <summary>A grid of <paramref name="size"/> units per pixel covering the font's ascender to descender.</summary>
        public static TtfPixelGrid ForSize(TtfEditor font, int size) => ForSize(font, size, font.SimpleOutlines().ToList());

        private static TtfPixelGrid ForSize(TtfEditor font, int size, List<TtfGlyphOutline> outlines)
        {
            int top = Math.Max(font.Ascender, outlines.Select(o => o.YMax).DefaultIfEmpty(0).Max());
            int bottom = Math.Min(font.Descender, outlines.Select(o => o.YMin).DefaultIfEmpty(0).Min());
            int topRow = (int)Math.Ceiling(top / (double)size);
            int bottomRow = (int)Math.Floor(bottom / (double)size);
            return new TtfPixelGrid(size, topRow, Math.Max(1, topRow - bottomRow));
        }

        /// <summary>
        /// Draws a glyph onto the grid: a pixel is set if its center is inside the outline. The bitmap is
        /// as wide as the advance width (or the ink, if wider) and <see cref="Rows"/> high.
        /// </summary>
        public WfnGlyph Rasterize(TtfGlyphOutline outline, int advanceWidth)
        {
            int width = Math.Max(Ceil(advanceWidth), outline.IsEmpty ? 0 : Ceil(outline.XMax));
            var bitmap = new WfnGlyph(width, Rows);
            if (outline.IsEmpty)
                return bitmap;
            for (int row = 0; row < Rows; row++)
            {
                double y = (TopRow - row - 0.5) * Size;
                if (y > outline.YMax || y < outline.YMin)
                    continue;
                for (int col = 0; col < width; col++)
                    bitmap[col, row] = outline.WindingAt((col + 0.5) * Size, y) != 0;
            }
            return bitmap;
        }

        public WfnGlyph Rasterize(TtfEditor font, int glyph) => Rasterize(font.GetOutline(glyph), font.GetAdvance(glyph));

        private int Ceil(int units) => (units + Size - 1) / Size;

        /// <summary>
        /// Outline of the set pixels: the boundary of each connected area as one contour with only corner
        /// points (clockwise around ink, counter-clockwise around holes).
        /// </summary>
        public TtfGlyphOutline ToOutline(WfnGlyph bitmap)
        {
            bool Ink(int x, int y) => x >= 0 && y >= 0 && x < bitmap.Width && y < bitmap.Height && bitmap[x, y];

            // Boundary edges between ink and background on the pixel corner lattice (y down), oriented so
            // that ink is on the right when viewed with y up (TrueType's clockwise outer contours)
            var outgoing = new Dictionary<(int X, int Y), List<(int X, int Y)>>();
            void Edge((int, int) from, (int, int) to)
            {
                if (!outgoing.TryGetValue(from, out var list))
                    outgoing[from] = list = new List<(int, int)>();
                list.Add(to);
            }
            for (int y = 0; y < bitmap.Height; y++)
            {
                for (int x = 0; x < bitmap.Width; x++)
                {
                    if (!bitmap[x, y])
                        continue;
                    if (!Ink(x - 1, y)) Edge((x, y + 1), (x, y));         // left side, upwards
                    if (!Ink(x, y - 1)) Edge((x, y), (x + 1, y));         // top, to the right
                    if (!Ink(x + 1, y)) Edge((x + 1, y), (x + 1, y + 1)); // right side, downwards
                    if (!Ink(x, y + 1)) Edge((x + 1, y + 1), (x, y + 1)); // bottom, to the left
                }
            }

            var contours = new List<IReadOnlyList<TtfPoint>>();
            // Start each contour at its top-left corner for a stable result
            foreach (var start in outgoing.Keys.OrderBy(k => k.Y).ThenBy(k => k.X).ToList())
            {
                while (outgoing.TryGetValue(start, out var first) && first.Count > 0)
                {
                    var path = new List<(int X, int Y)> { start };
                    var current = start;
                    (int X, int Y) direction = (0, 0);
                    while (true)
                    {
                        var options = outgoing[current];
                        // Where two areas touch at a corner, turn so the areas get separate contours
                        var next = options.Count == 1 ? options[0] : options.OrderBy(o => Turn(direction, (o.X - current.X, o.Y - current.Y))).First();
                        options.Remove(next);
                        if (options.Count == 0)
                            outgoing.Remove(current);
                        direction = (next.X - current.X, next.Y - current.Y);
                        current = next;
                        if (current == start)
                            break;
                        path.Add(current);
                    }
                    contours.Add(Simplify(path).Select(p => new TtfPoint(p.X * Size, (TopRow - p.Y) * Size)).ToList());
                }
            }
            return new TtfGlyphOutline(contours);

            // 0 = right turn (on the y-down lattice, clockwise on screen), 1 = straight, 2 = left turn
            static int Turn((int X, int Y) from, (int X, int Y) to)
            {
                int cross = from.X * to.Y - from.Y * to.X;
                return cross > 0 ? 0 : cross == 0 ? 1 : 2;
            }
        }

        /// <summary>Removes points in the middle of straight lines.</summary>
        private static List<(int X, int Y)> Simplify(List<(int X, int Y)> path)
        {
            var result = new List<(int X, int Y)>();
            for (int i = 0; i < path.Count; i++)
            {
                var prev = path[(i + path.Count - 1) % path.Count];
                var p = path[i];
                var next = path[(i + 1) % path.Count];
                if ((p.X - prev.X) * (next.Y - p.Y) != (p.Y - prev.Y) * (next.X - p.X))
                    result.Add(p);
            }
            return result;
        }
    }
}
