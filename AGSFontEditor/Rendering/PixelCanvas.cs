using System;
using System.Runtime.InteropServices;
using AGSTools;
using Avalonia;
using Avalonia.Media.Imaging;
using Avalonia.Platform;

namespace AGSFontEditor.Rendering
{
    /// <summary>
    /// A small ARGB pixel buffer for drawing glyphs pixel-exactly; converted to an Avalonia bitmap with
    /// integer (nearest neighbour) scaling, so low-resolution fonts stay sharp.
    /// </summary>
    public sealed class PixelCanvas
    {
        private readonly uint[] _pixels;

        public PixelCanvas(int width, int height, uint background)
        {
            Width = Math.Max(1, width);
            Height = Math.Max(1, height);
            _pixels = new uint[Width * Height];
            Array.Fill(_pixels, background);
        }

        public int Width { get; }
        public int Height { get; }

        public void FillRect(int x, int y, int width, int height, uint color)
        {
            for (int py = Math.Max(0, y); py < Math.Min(Height, y + height); py++)
                for (int px = Math.Max(0, x); px < Math.Min(Width, x + width); px++)
                    _pixels[py * Width + px] = Blend(_pixels[py * Width + px], color);
        }

        /// <summary>
        /// Draws the set pixels of a glyph with its top-left corner at (<paramref name="x"/>, <paramref name="y"/>).
        /// Shaded glyphs are drawn like the engine does in 32-bit games: <paramref name="baseShade"/> in
        /// <paramref name="color"/>, each shade above or below it 17 steps lighter or darker per channel.
        /// </summary>
        public void DrawGlyph(WfnGlyph glyph, int x, int y, uint color, int baseShade = WfnFont.DefaultBaseShade)
        {
            var shades = new uint[glyph.MaxShade + 1];
            for (int s = 1; s < shades.Length; s++)
                shades[s] = glyph.BitsPerPixel == 1 ? color : ShadeColor(color, s, baseShade);
            for (int gy = 0; gy < glyph.Height; gy++)
            {
                int py = y + gy;
                if (py < 0 || py >= Height)
                    continue;
                for (int gx = 0; gx < glyph.Width; gx++)
                {
                    int px = x + gx;
                    if (px >= 0 && px < Width && glyph.GetShade(gx, gy) is int shade and > 0)
                        _pixels[py * Width + px] = shades[shade];
                }
            }
        }

        /// <summary>The color of <paramref name="shade"/> for a text in <paramref name="color"/> (ARGB).</summary>
        public static uint ShadeColor(uint color, int shade, int baseShade)
        {
            int offset = (shade - baseShade) * 17;
            uint Channel(int shift) => (uint)Math.Clamp((int)((color >> shift) & 0xFF) + offset, 0, 255) << shift;
            return (color & 0xFF000000) | Channel(16) | Channel(8) | Channel(0);
        }

        /// <summary>Alpha-blends <paramref name="top"/> (ARGB, straight alpha) over an opaque color.</summary>
        private static uint Blend(uint bottom, uint top)
        {
            uint a = top >> 24;
            if (a == 255) return top;
            if (a == 0) return bottom;
            uint Mix(int shift) => ((((top >> shift) & 0xFF) * a + ((bottom >> shift) & 0xFF) * (255 - a)) / 255) << shift;
            return 0xFF000000 | Mix(16) | Mix(8) | Mix(0);
        }

        /// <summary>The canvas as a bitmap, every pixel drawn as a <paramref name="scale"/> x <paramref name="scale"/> square.</summary>
        public WriteableBitmap ToBitmap(int scale = 1)
        {
            scale = Math.Max(1, scale);
            var bitmap = new WriteableBitmap(new PixelSize(Width * scale, Height * scale), new Vector(96, 96), PixelFormat.Bgra8888, AlphaFormat.Opaque);
            using ILockedFramebuffer fb = bitmap.Lock();
            var row = new int[Width * scale];
            for (int y = 0; y < Height; y++)
            {
                for (int x = 0; x < Width; x++)
                    row.AsSpan(x * scale, scale).Fill(unchecked((int)_pixels[y * Width + x]));
                for (int s = 0; s < scale; s++)
                    Marshal.Copy(row, 0, fb.Address + (y * scale + s) * fb.RowBytes, row.Length);
            }
            return bitmap;
        }
    }
}
