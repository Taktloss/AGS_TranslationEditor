using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Documents;
using Avalonia.Media;

namespace AGSFontEditor.Controls
{
    /// <summary>
    /// A line icon: <see cref="Data"/> is drawn on a 24×24 grid with round strokes, scaled to the control's size,
    /// in the inherited text color, so it follows the theme and the hover and disabled state of its button.
    /// </summary>
    public class LineIcon : Control
    {
        public static readonly StyledProperty<Geometry?> DataProperty =
            AvaloniaProperty.Register<LineIcon, Geometry?>(nameof(Data));

        public static readonly StyledProperty<IBrush?> ForegroundProperty =
            TextElement.ForegroundProperty.AddOwner<LineIcon>();

        /// <summary>Line width on the 24×24 grid.</summary>
        public static readonly StyledProperty<double> StrokeWidthProperty =
            AvaloniaProperty.Register<LineIcon, double>(nameof(StrokeWidth), 2);

        static LineIcon()
        {
            AffectsRender<LineIcon>(DataProperty, ForegroundProperty, StrokeWidthProperty);
            WidthProperty.OverrideDefaultValue<LineIcon>(16);
            HeightProperty.OverrideDefaultValue<LineIcon>(16);
        }

        public Geometry? Data
        {
            get => GetValue(DataProperty);
            set => SetValue(DataProperty, value);
        }

        public IBrush? Foreground
        {
            get => GetValue(ForegroundProperty);
            set => SetValue(ForegroundProperty, value);
        }

        public double StrokeWidth
        {
            get => GetValue(StrokeWidthProperty);
            set => SetValue(StrokeWidthProperty, value);
        }

        public override void Render(DrawingContext context)
        {
            if (Data == null || Foreground == null)
                return;
            double scale = System.Math.Min(Bounds.Width, Bounds.Height) / 24;
            var pen = new Pen(Foreground, StrokeWidth, lineCap: PenLineCap.Round, lineJoin: PenLineJoin.Round);
            using (context.PushTransform(Matrix.CreateScale(scale, scale)))
                context.DrawGeometry(null, pen, Data);
        }
    }
}
