using System;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Media;
using Avalonia.Media.Imaging;

namespace AGSFontEditor.Controls
{
    /// <summary>
    /// Shows a sprite zoomed by whole pixels on a checkerboard (for transparency). Dragging with the left
    /// button selects a rectangle; Ctrl+click picks the color of a pixel.
    /// </summary>
    public class SpriteCanvas : Control
    {
        public static readonly StyledProperty<Bitmap?> SourceProperty =
            AvaloniaProperty.Register<SpriteCanvas, Bitmap?>(nameof(Source));

        public static readonly StyledProperty<int> ZoomProperty =
            AvaloniaProperty.Register<SpriteCanvas, int>(nameof(Zoom), 1);

        public static readonly StyledProperty<PixelRect?> SelectionProperty =
            AvaloniaProperty.Register<SpriteCanvas, PixelRect?>(nameof(Selection), defaultBindingMode: Avalonia.Data.BindingMode.TwoWay);

        private static readonly IBrush Light = new SolidColorBrush(Color.Parse("#5A5A5A"));
        private static readonly IBrush Dark = new SolidColorBrush(Color.Parse("#444444"));
        private static readonly IPen SelectionPen = new Pen(Brushes.White, 1, new DashStyle(new double[] { 4, 4 }, 0));
        private static readonly IPen SelectionShadow = new Pen(Brushes.Black, 1);
        private static readonly IPen BorderPen = new Pen(new SolidColorBrush(Color.Parse("#007ACC")), 1);

        private PixelPoint? _dragStart;

        static SpriteCanvas()
        {
            AffectsRender<SpriteCanvas>(SourceProperty, ZoomProperty, SelectionProperty);
            AffectsMeasure<SpriteCanvas>(SourceProperty, ZoomProperty);
        }

        public Bitmap? Source
        {
            get => GetValue(SourceProperty);
            set => SetValue(SourceProperty, value);
        }

        public int Zoom
        {
            get => GetValue(ZoomProperty);
            set => SetValue(ZoomProperty, value);
        }

        /// <summary>The selected rectangle in sprite pixels; null for none.</summary>
        public PixelRect? Selection
        {
            get => GetValue(SelectionProperty);
            set => SetValue(SelectionProperty, value);
        }

        /// <summary>Raised by Ctrl+click with the pixel position.</summary>
        public event EventHandler<PixelPoint>? ColorPicked;

        private int Scale => Math.Max(1, Zoom);

        protected override Size MeasureOverride(Size availableSize) =>
            Source == null ? default : new Size(Source.PixelSize.Width * Scale, Source.PixelSize.Height * Scale);

        public override void Render(DrawingContext context)
        {
            if (Source is not { } source)
                return;
            int w = source.PixelSize.Width * Scale, h = source.PixelSize.Height * Scale;
            const int Cell = 8;
            context.FillRectangle(Light, new Rect(0, 0, w, h));
            for (int y = 0; y < h; y += Cell)
                for (int x = (y / Cell % 2) * Cell; x < w; x += Cell * 2)
                    context.FillRectangle(Dark, new Rect(x, y, Math.Min(Cell, w - x), Math.Min(Cell, h - y)));
            using (context.PushRenderOptions(new RenderOptions { BitmapInterpolationMode = BitmapInterpolationMode.None }))
                context.DrawImage(source, new Rect(0, 0, source.PixelSize.Width, source.PixelSize.Height), new Rect(0, 0, w, h));
            context.DrawRectangle(BorderPen, new Rect(-0.5, -0.5, w + 1, h + 1));
            if (Selection is { } sel && sel.Width > 0 && sel.Height > 0)
            {
                var r = new Rect(sel.X * Scale + 0.5, sel.Y * Scale + 0.5, sel.Width * Scale - 1, sel.Height * Scale - 1);
                context.DrawRectangle(SelectionShadow, r);
                context.DrawRectangle(SelectionPen, r);
            }
        }

        private PixelPoint? PixelAt(Point p)
        {
            if (Source is not { } source)
                return null;
            int x = Math.Clamp((int)Math.Floor(p.X / Scale), 0, source.PixelSize.Width - 1);
            int y = Math.Clamp((int)Math.Floor(p.Y / Scale), 0, source.PixelSize.Height - 1);
            return new PixelPoint(x, y);
        }

        protected override void OnPointerPressed(PointerPressedEventArgs e)
        {
            base.OnPointerPressed(e);
            PointerPoint point = e.GetCurrentPoint(this);
            if (PixelAt(point.Position) is not { } pixel)
                return;
            if (point.Properties.IsRightButtonPressed)
            {
                Selection = null;
                return;
            }
            if (!point.Properties.IsLeftButtonPressed)
                return;
            if ((e.KeyModifiers & KeyModifiers.Control) != 0)
            {
                ColorPicked?.Invoke(this, pixel);
                return;
            }
            _dragStart = pixel;
            Selection = new PixelRect(pixel.X, pixel.Y, 1, 1);
            e.Pointer.Capture(this);
        }

        protected override void OnPointerMoved(PointerEventArgs e)
        {
            base.OnPointerMoved(e);
            if (_dragStart is not { } start || PixelAt(e.GetPosition(this)) is not { } pixel)
                return;
            int x0 = Math.Min(start.X, pixel.X), y0 = Math.Min(start.Y, pixel.Y);
            Selection = new PixelRect(x0, y0, Math.Abs(pixel.X - start.X) + 1, Math.Abs(pixel.Y - start.Y) + 1);
        }

        protected override void OnPointerReleased(PointerReleasedEventArgs e)
        {
            base.OnPointerReleased(e);
            _dragStart = null;
            e.Pointer.Capture(null);
        }
    }
}
