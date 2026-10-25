using System;
using System.Buffers.Binary;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace AGSTools
{
    /// <summary>A point of a TrueType contour in font units.</summary>
    public readonly record struct TtfPoint(int X, int Y, bool OnCurve = true);

    /// <summary>
    /// The outline of a TrueType glyph: closed contours of on-curve points and quadratic control points.
    /// Outer contours run clockwise (y up), holes counter-clockwise.
    /// </summary>
    public sealed class TtfGlyphOutline
    {
        public TtfGlyphOutline(IEnumerable<IReadOnlyList<TtfPoint>>? contours = null) =>
            Contours = contours?.Where(c => c.Count > 0).ToList() ?? new List<IReadOnlyList<TtfPoint>>();

        public List<IReadOnlyList<TtfPoint>> Contours { get; }

        public bool IsEmpty => Contours.Count == 0;

        public int PointCount => Contours.Sum(c => c.Count);

        public int XMin => IsEmpty ? 0 : Contours.SelectMany(c => c).Min(p => p.X);
        public int XMax => IsEmpty ? 0 : Contours.SelectMany(c => c).Max(p => p.X);
        public int YMin => IsEmpty ? 0 : Contours.SelectMany(c => c).Min(p => p.Y);
        public int YMax => IsEmpty ? 0 : Contours.SelectMany(c => c).Max(p => p.Y);

        /// <summary>True if the outline has no curves, as in pixel fonts.</summary>
        public bool IsPolygonal => Contours.All(c => c.All(p => p.OnCurve));

        /// <summary>
        /// Nonzero winding number at a point (curves are flattened). Positive or negative values
        /// mean the point is inside the glyph.
        /// </summary>
        public int WindingAt(double x, double y)
        {
            int winding = 0;
            foreach (var contour in Contours)
            {
                var polygon = Flatten(contour);
                for (int i = 0; i < polygon.Count; i++)
                {
                    var (x0, y0) = polygon[i];
                    var (x1, y1) = polygon[(i + 1) % polygon.Count];
                    if (y0 <= y)
                    {
                        if (y1 > y && Cross(x0, y0, x1, y1, x, y) > 0)
                            winding--; // upward edge passing right of the point: clockwise contours count positive
                    }
                    else if (y1 <= y && Cross(x0, y0, x1, y1, x, y) < 0)
                        winding++;
                }
            }
            return winding;

            static double Cross(double x0, double y0, double x1, double y1, double x, double y) =>
                (x1 - x0) * (y - y0) - (x - x0) * (y1 - y0);
        }

        /// <summary>The contour as a polygon, with quadratic curves split into short lines.</summary>
        internal static List<(double X, double Y)> Flatten(IReadOnlyList<TtfPoint> contour)
        {
            var result = new List<(double X, double Y)>();
            int n = contour.Count;
            if (n == 0)
                return result;

            // Start at an on-curve point; a contour of only control points starts between the first two
            int start = -1;
            for (int i = 0; i < n; i++)
                if (contour[i].OnCurve) { start = i; break; }
            (double X, double Y) first = start >= 0
                ? (contour[start].X, contour[start].Y)
                : ((contour[0].X + contour[1 % n].X) / 2.0, (contour[0].Y + contour[1 % n].Y) / 2.0);

            // The points after the start, ending with the start point again
            var sequence = new List<(double X, double Y, bool OnCurve)>();
            for (int k = 1; k < n; k++)
            {
                TtfPoint p = contour[(Math.Max(start, 0) + k) % n];
                sequence.Add((p.X, p.Y, p.OnCurve));
            }
            if (start < 0)
                sequence.Add((contour[0].X, contour[0].Y, false));
            sequence.Add((first.X, first.Y, true));

            result.Add(first);
            var current = first;
            (double X, double Y)? control = null;
            foreach (var (x, y, onCurve) in sequence)
            {
                if (onCurve)
                {
                    if (control is { } c)
                        AddCurve(current, c, (x, y));
                    else
                        result.Add((x, y));
                    current = (x, y);
                    control = null;
                }
                else if (control is { } c)
                {
                    // Two control points in a row imply an on-curve point between them
                    var mid = ((c.X + x) / 2, (c.Y + y) / 2);
                    AddCurve(current, c, mid);
                    current = mid;
                    control = (x, y);
                }
                else
                    control = (x, y);
            }
            if (result.Count > 1 && result[^1] == result[0])
                result.RemoveAt(result.Count - 1);
            return result;

            void AddCurve((double X, double Y) from, (double X, double Y) c, (double X, double Y) to)
            {
                const int Steps = 8;
                for (int s = 1; s <= Steps; s++)
                {
                    double t = s / (double)Steps, u = 1 - t;
                    result.Add((u * u * from.X + 2 * u * t * c.X + t * t * to.X, u * u * from.Y + 2 * u * t * c.Y + t * t * to.Y));
                }
            }
        }
    }

    /// <summary>Reads and writes glyph records of the TrueType 'glyf' table.</summary>
    internal static class GlyfCodec
    {
        private const byte OnCurve = 0x01, XShort = 0x02, YShort = 0x04, Repeat = 0x08, XSame = 0x10, YSame = 0x20;

        /// <summary>Bounding box from the glyph header; all zero for an empty glyph.</summary>
        public static (short XMin, short YMin, short XMax, short YMax) Bounds(byte[] glyph) =>
            glyph.Length < 10
                ? default
                : (I16(glyph, 2), I16(glyph, 4), I16(glyph, 6), I16(glyph, 8));

        public static bool IsComposite(byte[] glyph) => glyph.Length >= 10 && I16(glyph, 0) < 0;

        /// <summary>Number of points and contours of a simple glyph (0 for empty and composite glyphs).</summary>
        public static (int Points, int Contours) Size(byte[] glyph)
        {
            if (glyph.Length < 10 || I16(glyph, 0) <= 0)
                return (0, 0);
            int contours = I16(glyph, 0);
            return (U16(glyph, 10 + (contours - 1) * 2) + 1, contours);
        }

        /// <summary>Glyph indices used as components by a composite glyph.</summary>
        public static IEnumerable<int> Components(byte[] glyph)
        {
            if (!IsComposite(glyph))
                yield break;
            int pos = 10;
            while (pos + 4 <= glyph.Length)
            {
                int flags = U16(glyph, pos);
                yield return U16(glyph, pos + 2);
                pos += 4 + ComponentArgsSize(flags);
                if ((flags & 0x20) == 0)
                    break;
            }
        }

        private static int ComponentArgsSize(int flags) =>
            ((flags & 0x01) != 0 ? 4 : 2)
            + ((flags & 0x08) != 0 ? 2 : (flags & 0x40) != 0 ? 4 : (flags & 0x80) != 0 ? 8 : 0);

        /// <summary>Decodes a glyph; composite glyphs are resolved through <paramref name="getGlyph"/>.</summary>
        public static TtfGlyphOutline Decode(byte[] glyph, Func<int, byte[]?> getGlyph, int depth = 0)
        {
            if (glyph.Length < 10)
                return new TtfGlyphOutline();
            try
            {
                return I16(glyph, 0) >= 0 ? DecodeSimple(glyph) : DecodeComposite(glyph, getGlyph, depth);
            }
            catch (Exception ex) when (ex is ArgumentOutOfRangeException or IndexOutOfRangeException)
            {
                throw new AgsFormatException("TrueType glyph data is truncated or corrupt.");
            }
        }

        private static TtfGlyphOutline DecodeSimple(byte[] glyph)
        {
            int contourCount = I16(glyph, 0);
            var ends = new int[contourCount];
            for (int i = 0; i < contourCount; i++)
                ends[i] = U16(glyph, 10 + i * 2);
            int pointCount = contourCount == 0 ? 0 : ends[^1] + 1;
            int pos = 10 + contourCount * 2;
            pos += 2 + U16(glyph, pos); // instructions

            var flags = new byte[pointCount];
            for (int i = 0; i < pointCount;)
            {
                byte f = glyph[pos++];
                flags[i++] = f;
                if ((f & Repeat) != 0)
                    for (int r = glyph[pos++]; r > 0 && i < pointCount; r--)
                        flags[i++] = f;
            }

            int[] ReadCoords(byte shortFlag, byte sameFlag)
            {
                var coords = new int[pointCount];
                int value = 0;
                for (int i = 0; i < pointCount; i++)
                {
                    byte f = flags[i];
                    if ((f & shortFlag) != 0)
                    {
                        int delta = glyph[pos++];
                        value += (f & sameFlag) != 0 ? delta : -delta;
                    }
                    else if ((f & sameFlag) == 0)
                    {
                        value += I16(glyph, pos);
                        pos += 2;
                    }
                    coords[i] = value;
                }
                return coords;
            }
            int[] xs = ReadCoords(XShort, XSame);
            int[] ys = ReadCoords(YShort, YSame);

            var contours = new List<IReadOnlyList<TtfPoint>>();
            int startPoint = 0;
            foreach (int end in ends)
            {
                var contour = new List<TtfPoint>();
                for (int i = startPoint; i <= end && i < pointCount; i++)
                    contour.Add(new TtfPoint(xs[i], ys[i], (flags[i] & OnCurve) != 0));
                contours.Add(contour);
                startPoint = end + 1;
            }
            return new TtfGlyphOutline(contours);
        }

        private static TtfGlyphOutline DecodeComposite(byte[] glyph, Func<int, byte[]?> getGlyph, int depth)
        {
            var result = new TtfGlyphOutline();
            if (depth > 8)
                return result; // a cycle or an absurdly deep nesting
            int pos = 10;
            while (true)
            {
                int flags = U16(glyph, pos), index = U16(glyph, pos + 2);
                pos += 4;
                int dx, dy;
                if ((flags & 0x01) != 0) { dx = I16(glyph, pos); dy = I16(glyph, pos + 2); pos += 4; }
                else { dx = (sbyte)glyph[pos]; dy = (sbyte)glyph[pos + 1]; pos += 2; }
                if ((flags & 0x02) == 0)
                    dx = dy = 0; // point matching is not supported, components keep their position

                double a = 1, b = 0, c = 0, d = 1;
                if ((flags & 0x08) != 0) { a = d = F2Dot14(glyph, pos); pos += 2; }
                else if ((flags & 0x40) != 0) { a = F2Dot14(glyph, pos); d = F2Dot14(glyph, pos + 2); pos += 4; }
                else if ((flags & 0x80) != 0) { a = F2Dot14(glyph, pos); b = F2Dot14(glyph, pos + 2); c = F2Dot14(glyph, pos + 4); d = F2Dot14(glyph, pos + 6); pos += 8; }

                byte[]? component = getGlyph(index);
                if (component != null)
                {
                    foreach (var contour in Decode(component, getGlyph, depth + 1).Contours)
                        result.Contours.Add(contour.Select(p => new TtfPoint(
                            (int)Math.Round(p.X * a + p.Y * c) + dx, (int)Math.Round(p.X * b + p.Y * d) + dy, p.OnCurve)).ToList());
                }
                if ((flags & 0x20) == 0)
                    return result;
            }
        }

        /// <summary>Encodes a simple glyph without instructions; an empty outline gives an empty record.</summary>
        public static byte[] Encode(TtfGlyphOutline outline)
        {
            if (outline.IsEmpty)
                return Array.Empty<byte>();
            if (outline.Contours.Count > short.MaxValue || outline.PointCount > ushort.MaxValue)
                throw new AgsFormatException("Glyph outline has too many points.");

            using var ms = new MemoryStream();
            void W16(int v) { ms.WriteByte((byte)(v >> 8)); ms.WriteByte((byte)v); }

            W16(outline.Contours.Count);
            W16(Clamp16(outline.XMin)); W16(Clamp16(outline.YMin)); W16(Clamp16(outline.XMax)); W16(Clamp16(outline.YMax));
            int end = -1;
            foreach (var contour in outline.Contours)
                W16(end += contour.Count);
            W16(0); // no instructions

            var flags = new List<byte>();
            var xBytes = new List<byte>();
            var yBytes = new List<byte>();
            int lastX = 0, lastY = 0;
            foreach (TtfPoint p in outline.Contours.SelectMany(c => c))
            {
                byte f = p.OnCurve ? OnCurve : (byte)0;
                f |= EncodeDelta(p.X - lastX, XShort, XSame, xBytes);
                f |= EncodeDelta(p.Y - lastY, YShort, YSame, yBytes);
                lastX = p.X;
                lastY = p.Y;
                flags.Add(f);
            }
            // Runs of equal flags are stored once with a repeat count
            for (int i = 0; i < flags.Count;)
            {
                int run = 1;
                while (i + run < flags.Count && flags[i + run] == flags[i] && run < 256)
                    run++;
                if (run > 1)
                {
                    ms.WriteByte((byte)(flags[i] | Repeat));
                    ms.WriteByte((byte)(run - 1));
                }
                else
                    ms.WriteByte(flags[i]);
                i += run;
            }
            ms.Write(xBytes.ToArray());
            ms.Write(yBytes.ToArray());
            return ms.ToArray();

            static int Clamp16(int v) => Math.Clamp(v, short.MinValue, short.MaxValue);
        }

        private static byte EncodeDelta(int delta, byte shortFlag, byte sameFlag, List<byte> bytes)
        {
            if (delta == 0)
                return sameFlag;
            if (delta is > -256 and < 256)
            {
                bytes.Add((byte)Math.Abs(delta));
                return (byte)(shortFlag | (delta > 0 ? sameFlag : 0));
            }
            if (delta < short.MinValue || delta > short.MaxValue)
                throw new AgsFormatException("Glyph coordinate is out of range.");
            bytes.Add((byte)(delta >> 8));
            bytes.Add((byte)delta);
            return 0;
        }

        private static short I16(byte[] data, int offset) => BinaryPrimitives.ReadInt16BigEndian(data.AsSpan(offset, 2));
        private static ushort U16(byte[] data, int offset) => BinaryPrimitives.ReadUInt16BigEndian(data.AsSpan(offset, 2));
        private static double F2Dot14(byte[] data, int offset) => I16(data, offset) / 16384.0;
    }
}
