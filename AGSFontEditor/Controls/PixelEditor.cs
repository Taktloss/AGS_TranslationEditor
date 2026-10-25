using System;
using AGSTools;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Media;

namespace AGSFontEditor.Controls
{
    /// <summary>
    /// Zoomed editor for a monochrome glyph bitmap. Left click toggles a pixel and drag paints with the
    /// new value; right click/drag clears. The bitmap is changed in place.
    /// </summary>
    public class PixelEditor : Control
    {
        public static readonly StyledProperty<WfnGlyph?> GlyphProperty =
            AvaloniaProperty.Register<PixelEditor, WfnGlyph?>(nameof(Glyph));

        public static readonly StyledProperty<int> ZoomProperty =
            AvaloniaProperty.Register<PixelEditor, int>(nameof(Zoom), 16);

        public static readonly StyledProperty<int?> BaselineRowProperty =
            AvaloniaProperty.Register<PixelEditor, int?>(nameof(BaselineRow));

        public static readonly StyledProperty<bool> IsEditableProperty =
            AvaloniaProperty.Register<PixelEditor, bool>(nameof(IsEditable), true);

        private static readonly IBrush Paper = new SolidColorBrush(Color.Parse("#2B2B2B"));
        private static readonly IBrush InkBrush = new SolidColorBrush(Color.Parse("#E8E8E8"));
        private static readonly IPen GridPen = new Pen(new SolidColorBrush(Color.Parse("#3E3E3E")), 1);
        private static readonly IPen BaselinePen = new Pen(new SolidColorBrush(Color.Parse("#007ACC")), 2);
        private static readonly IPen BorderPen = new Pen(new SolidColorBrush(Color.Parse("#6A6A6A")), 1);

        private bool? _paintValue;
        private (int X, int Y) _lastPixel = (-1, -1);

        static PixelEditor()
        {
            AffectsRender<PixelEditor>(GlyphProperty, ZoomProperty, BaselineRowProperty);
            AffectsMeasure<PixelEditor>(GlyphProperty, ZoomProperty);
        }

        public WfnGlyph? Glyph
        {
            get => GetValue(GlyphProperty);
            set => SetValue(GlyphProperty, value);
        }

        public int Zoom
        {
            get => GetValue(ZoomProperty);
            set => SetValue(ZoomProperty, value);
        }

        /// <summary>Row whose bottom edge is drawn as the baseline; null for none.</summary>
        public int? BaselineRow
        {
            get => GetValue(BaselineRowProperty);
            set => SetValue(BaselineRowProperty, value);
        }

        public bool IsEditable
        {
            get => GetValue(IsEditableProperty);
            set => SetValue(IsEditableProperty, value);
        }

        /// <summary>Raised before the first pixel of a stroke changes (to take an undo snapshot).</summary>
        public event EventHandler? StrokeStarted;

        /// <summary>Raised when a stroke that changed pixels ends.</summary>
        public event EventHandler? StrokeCompleted;

        /// <summary>Redraws after the bitmap was changed from outside.</summary>
        public void Refresh() => InvalidateVisual();

        protected override Size MeasureOverride(Size availableSize) =>
            Glyph == null ? new Size(0, 0) : new Size(Glyph.Width * Zoom + 1, Glyph.Height * Zoom + 1);

        public override void Render(DrawingContext context)
        {
            WfnGlyph? glyph = Glyph;
            if (glyph == null)
                return;
            int z = Math.Max(1, Zoom);
            var size = new Rect(0, 0, glyph.Width * z, glyph.Height * z);
            context.FillRectangle(Paper, size);
            for (int y = 0; y < glyph.Height; y++)
                for (int x = 0; x < glyph.Width; x++)
                    if (glyph[x, y])
                        context.FillRectangle(InkBrush, new Rect(x * z, y * z, z, z));
            if (z >= 4)
            {
                for (int x = 1; x < glyph.Width; x++)
                    context.DrawLine(GridPen, new Point(x * z + 0.5, 0), new Point(x * z + 0.5, size.Height));
                for (int y = 1; y < glyph.Height; y++)
                    context.DrawLine(GridPen, new Point(0, y * z + 0.5), new Point(size.Width, y * z + 0.5));
            }
            if (BaselineRow is int row && row >= 0 && row < glyph.Height)
                context.DrawLine(BaselinePen, new Point(0, (row + 1) * z), new Point(size.Width, (row + 1) * z));
            context.DrawRectangle(null, BorderPen, size.Inflate(0.5));
        }

        private (int X, int Y)? PixelAt(Point p)
        {
            if (Glyph == null)
                return null;
            int x = (int)Math.Floor(p.X / Math.Max(1, Zoom)), y = (int)Math.Floor(p.Y / Math.Max(1, Zoom));
            return x >= 0 && y >= 0 && x < Glyph.Width && y < Glyph.Height ? (x, y) : null;
        }

        protected override void OnPointerPressed(PointerPressedEventArgs e)
        {
            base.OnPointerPressed(e);
            if (!IsEditable || Glyph == null || PixelAt(e.GetPosition(this)) is not { } pixel)
                return;
            PointerPointProperties props = e.GetCurrentPoint(this).Properties;
            StrokeStarted?.Invoke(this, EventArgs.Empty);
            _paintValue = props.IsRightButtonPressed ? false : !Glyph[pixel.X, pixel.Y];
            _lastPixel = (-1, -1);
            Paint(pixel);
            e.Pointer.Capture(this);
            e.Handled = true;
        }

        protected override void OnPointerMoved(PointerEventArgs e)
        {
            base.OnPointerMoved(e);
            if (_paintValue != null && PixelAt(e.GetPosition(this)) is { } pixel)
                Paint(pixel);
        }

        protected override void OnPointerReleased(PointerReleasedEventArgs e)
        {
            base.OnPointerReleased(e);
            EndStroke(e.Pointer);
        }

        protected override void OnPointerCaptureLost(PointerCaptureLostEventArgs e)
        {
            base.OnPointerCaptureLost(e);
            EndStroke(null);
        }

        private void EndStroke(IPointer? pointer)
        {
            if (_paintValue == null)
                return;
            _paintValue = null;
            pointer?.Capture(null);
            StrokeCompleted?.Invoke(this, EventArgs.Empty);
        }

        private void Paint((int X, int Y) pixel)
        {
            if (pixel == _lastPixel || Glyph == null || _paintValue is not bool value)
                return;
            _lastPixel = pixel;
            if (Glyph[pixel.X, pixel.Y] == value)
                return;
            Glyph[pixel.X, pixel.Y] = value;
            InvalidateVisual();
        }
    }
}
