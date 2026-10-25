using System;
using System.Collections.Generic;
using System.Linq;
using AGSTools;
using SkiaSharp;

namespace AGSFontEditor.Fonts
{
    /// <summary>A glyph drawn at game size: its pixels, the row of the line it starts at, and its advance.</summary>
    public sealed record GlyphPixels(WfnGlyph Bitmap, int Top, int Advance);

    /// <summary>
    /// A game font opened for viewing and editing. Glyphs are edited as monochrome bitmaps
    /// (<see cref="WfnGlyph"/>): WFN glyphs directly, TrueType glyphs on the font's pixel grid.
    /// </summary>
    public abstract class EditableFont : IDisposable
    {
        protected EditableFont(GameFont source) => Source = source;

        public GameFont Source { get; }

        public bool IsModified { get; protected set; }

        /// <summary>Height of a text line in pixels at game size.</summary>
        public abstract int LineHeight { get; }

        /// <summary>Character codes shown in the glyph grid.</summary>
        public abstract IReadOnlyList<int> GridCodes { get; }

        public abstract bool HasGlyph(int code);

        /// <summary>The glyph drawn for <paramref name="code"/> at game size, or null if the font has none.</summary>
        public abstract GlyphPixels? Render(int code);

        /// <summary>Why glyphs cannot be edited, or null if they can.</summary>
        public abstract string? EditBlocker { get; }

        /// <summary>True if all glyph bitmaps have the same height (the pixel grid of a TrueType font).</summary>
        public abstract bool FixedHeight { get; }

        /// <summary>Bitmap row that sits on the baseline, if the font has one.</summary>
        public abstract int? BaselineRow { get; }

        /// <summary>A copy of the glyph bitmap for editing, or null if the code has no glyph.</summary>
        public abstract WfnGlyph? GetEditGlyph(int code);

        /// <summary>An empty bitmap with a typical size for a new glyph.</summary>
        public abstract WfnGlyph CreateEmptyGlyph();

        public abstract void SetGlyph(int code, WfnGlyph glyph);

        public abstract void DeleteGlyph(int code);

        /// <summary>The font file with all changes.</summary>
        public abstract byte[] ToArray();

        /// <summary>Marks the current state as saved.</summary>
        public void MarkSaved() => IsModified = false;

        public virtual void Dispose() { }

        public static EditableFont Open(GameFont font) =>
            font.Error != null ? throw new AgsFormatException(font.Error)
            : font.Kind == FontKind.Wfn ? new WfnEditableFont(font)
            : new TtfEditableFont(font);
    }

    public sealed class WfnEditableFont : EditableFont
    {
        private readonly WfnFont _font;

        public WfnEditableFont(GameFont source) : base(source) => _font = WfnFont.Read(source.Data);

        public override int LineHeight => Math.Max(1, _font.Height);

        /// <summary>Printable codes up to 255 (so missing Latin-1 letters can be added) plus every code of the table.</summary>
        public override IReadOnlyList<int> GridCodes =>
            Enumerable.Range(0, Math.Max(256, _font.Glyphs.Count)).Where(c => c >= 0x20 || _font.HasGlyph(c)).ToList();

        public override bool HasGlyph(int code) => _font.HasGlyph(code);

        public override GlyphPixels? Render(int code) =>
            _font.HasGlyph(code) ? new GlyphPixels(_font.GetGlyph(code)!, 0, _font.GetGlyph(code)!.Width) : null;

        public override string? EditBlocker => null;
        public override bool FixedHeight => false;
        public override int? BaselineRow => null;

        public override WfnGlyph? GetEditGlyph(int code) => _font.HasGlyph(code) ? _font.GetGlyph(code)!.Clone() : null;

        public override WfnGlyph CreateEmptyGlyph()
        {
            WfnGlyph? n = _font.GetGlyph('n');
            return new WfnGlyph(n is { IsEmpty: false } ? n.Width : Math.Max(1, LineHeight / 2), LineHeight);
        }

        public override void SetGlyph(int code, WfnGlyph glyph)
        {
            _font.SetGlyph(code, glyph.Clone());
            IsModified = true;
        }

        public override void DeleteGlyph(int code)
        {
            if (code < _font.Glyphs.Count)
                _font.SetGlyph(code, null);
            IsModified = true;
        }

        public override byte[] ToArray() => _font.ToArray();
    }

    public sealed class TtfEditableFont : EditableFont
    {
        private readonly TtfEditor? _editor;
        private readonly string? _editorError;
        private readonly TtfFont _reader;
        private TtfPixelGrid? _grid;
        private SKTypeface? _typeface;
        private SKFont? _font;
        private byte[] _data;

        public TtfEditableFont(GameFont source) : base(source)
        {
            _data = source.Data;
            _reader = source.Ttf ?? TtfFont.Read(source.Data);
            try
            {
                _editor = TtfEditor.Load(source.Data);
                _grid = TtfPixelGrid.Detect(_editor);
            }
            catch (AgsFormatException ex)
            {
                _editorError = ex.Message;
            }
            PixelSize = source.Info is { Size: > 0, SizeIsMultiplier: false } info ? info.Size
                : _grid != null ? Math.Max(1, _reader.UnitsPerEm / _grid.Size)
                : 16;
            CreateTypeface();
        }

        /// <summary>Font size in pixels per em, as set in the game (or the pixel grid's size).</summary>
        public int PixelSize { get; }

        /// <summary>The pixel grid used for editing, or null if the font is not a pixel font.</summary>
        public TtfPixelGrid? Grid => _grid;

        /// <summary>Font units per em.</summary>
        public int UnitsPerEm => _reader.UnitsPerEm;

        /// <summary>Edits glyphs on a grid of <paramref name="units"/> font units per pixel.</summary>
        public void SetGridSize(int units)
        {
            if (_editor == null || units <= 0)
                return;
            _grid = TtfPixelGrid.ForSize(_editor, units);
        }

        private void CreateTypeface()
        {
            _font?.Dispose();
            _typeface?.Dispose();
            using (var data = SKData.CreateCopy(_data))
                _typeface = SKTypeface.FromData(data);
            _font = _typeface == null ? null : new SKFont(_typeface, PixelSize)
            {
                Edging = SKFontEdging.Alias,
                Hinting = SKFontHinting.None,
                Subpixel = false,
                LinearMetrics = false,
            };
            if (_font != null)
            {
                SKFontMetrics m = _font.Metrics;
                Ascent = (int)Math.Ceiling(-m.Ascent);
                LineHeightPx = Math.Max(1, Ascent + (int)Math.Ceiling(m.Descent));
            }
        }

        private int Ascent { get; set; }
        private int LineHeightPx { get; set; } = 1;

        public override int LineHeight => LineHeightPx;

        public override IReadOnlyList<int> GridCodes =>
            Enumerable.Range(0x20, 0xE0).Concat(CodePoints.Where(c => c >= 0x20)).Distinct().OrderBy(c => c).ToList();

        private IEnumerable<int> CodePoints => (IEnumerable<int>?)_editor?.CodePoints ?? _reader.CodePoints;

        private int GlyphIndex(int code) => _editor?.GetGlyphIndex(code) ?? _reader.GetGlyphIndex(code);

        public override bool HasGlyph(int code) => GlyphIndex(code) != 0;

        public override GlyphPixels? Render(int code)
        {
            int glyph = GlyphIndex(code);
            if (glyph == 0 || _font == null)
                return null;

            ushort[] glyphs = { (ushort)glyph };
            var widths = new float[1];
            var bounds = new SKRect[1];
            _font.GetGlyphWidths(glyphs, widths, bounds);
            int advance = (int)Math.Round(widths[0]);
            SKRect b = bounds[0];
            // Bitmap from the line top, wide enough for the ink (which may extend past the advance)
            int left = Math.Min(0, (int)Math.Floor(b.Left));
            int width = Math.Max(Math.Max(advance, (int)Math.Ceiling(b.Right)) - left, 1);
            int top = Math.Min(0, Ascent + (int)Math.Floor(b.Top));
            int height = Math.Max(LineHeight, Ascent + (int)Math.Ceiling(b.Bottom)) - top;

            using var bitmap = new SKBitmap(width, height, SKColorType.Gray8, SKAlphaType.Opaque);
            using (var canvas = new SKCanvas(bitmap))
            using (var paint = new SKPaint { Color = SKColors.White, IsAntialias = false })
            using (var builder = new SKTextBlobBuilder())
            {
                canvas.Clear(SKColors.Black);
                var run = builder.AllocateRun(_font, 1, -left, Ascent - top);
                run.SetGlyphs(glyphs);
                using SKTextBlob? blob = builder.Build();
                if (blob != null)
                    canvas.DrawText(blob, 0, 0, paint);
            }
            var pixels = new WfnGlyph(width, height);
            for (int y = 0; y < height; y++)
                for (int x = 0; x < width; x++)
                    pixels[x, y] = bitmap.GetPixel(x, y).Red >= 128;
            return new GlyphPixels(pixels, top, advance);
        }

        public override string? EditBlocker =>
            _editorError != null ? $"This font cannot be edited: {_editorError}"
            : _grid == null ? "Not a pixel font (outlines with curves or no common pixel grid). Set a pixel grid size to edit it anyway."
            : null;

        public override bool FixedHeight => true;
        public override int? BaselineRow => _grid?.BaselineRow;

        public override WfnGlyph? GetEditGlyph(int code)
        {
            if (_editor == null || _grid == null)
                return null;
            int glyph = _editor.GetGlyphIndex(code);
            return glyph == 0 ? null : _grid.Rasterize(_editor, glyph);
        }

        public override WfnGlyph CreateEmptyGlyph()
        {
            if (_editor == null || _grid == null)
                throw new InvalidOperationException(EditBlocker);
            int n = _editor.GetGlyphIndex('n');
            int width = n != 0 ? (_editor.GetAdvance(n) + _grid.Size - 1) / _grid.Size : Math.Max(1, _grid.Rows / 2);
            return new WfnGlyph(width, _grid.Rows);
        }

        public override void SetGlyph(int code, WfnGlyph glyph)
        {
            if (_editor == null || _grid == null)
                throw new InvalidOperationException(EditBlocker);
            if (glyph.Height != _grid.Rows)
                glyph = glyph.Resize(glyph.Width, _grid.Rows);

            // Keep the original advance unless the width was changed (it need not be a multiple of the grid)
            int advance = glyph.Width * _grid.Size;
            int old = _editor.GetGlyphIndex(code);
            if (old != 0)
            {
                var outline = _editor.GetOutline(old);
                int oldAdvance = _editor.GetAdvance(old);
                int oldWidth = Math.Max((oldAdvance + _grid.Size - 1) / _grid.Size, outline.IsEmpty ? 0 : (outline.XMax + _grid.Size - 1) / _grid.Size);
                if (oldWidth == glyph.Width)
                    advance = oldAdvance;
            }
            _editor.SetGlyph(code, _grid.ToOutline(glyph), advance);
            Changed();
        }

        public override void DeleteGlyph(int code)
        {
            if (_editor?.RemoveCodePoint(code) == true)
                Changed();
        }

        private void Changed()
        {
            IsModified = true;
            _data = _editor!.ToArray();
            CreateTypeface();
        }

        public override byte[] ToArray() => _editor?.IsModified == true ? _data : Source.Data;

        public override void Dispose()
        {
            _font?.Dispose();
            _typeface?.Dispose();
        }
    }
}
