using System;

namespace AGSTools
{
    /// <summary>The size a text needs in a GUI control and the size the control has, in game pixels.</summary>
    /// <param name="Fits">True if the text is shown completely (or needs no more room than the original).</param>
    /// <param name="Size">Width (buttons, list items) or height (labels) the text needs.</param>
    /// <param name="Limit">Width or height of the control.</param>
    /// <param name="IsHeight">True for labels, which wrap their text and are measured by height.</param>
    public sealed record FitResult(bool Fits, int Size, int Limit, bool IsHeight);

    /// <summary>
    /// The room a GUI control has for one translated text, found by the translation check. <see cref="Measure"/> tells
    /// whether another wording fits, measured with the game font like the check does.
    /// </summary>
    public sealed class GuiFit
    {
        private readonly FontMetrics _font;
        private readonly TranslationOptions _options;
        private readonly string? _template;
        private readonly int _originalSize;

        internal GuiFit(GuiText control, FontMetrics font, TranslationOptions options, string originalRuntime, string? template)
        {
            Control = control;
            _font = font;
            _options = options;
            _template = template;
            _originalSize = control.Kind == GuiTextKind.Label
                ? font.Wrap(originalRuntime, control.Width, options).Count
                : font.MeasureWidth(originalRuntime, options);
        }

        public GuiText Control { get; }

        /// <summary>True for TrueType fonts, whose widths are estimated.</summary>
        public bool IsApproximate => _font.IsApproximate;

        /// <summary>Measures a translation as written in the TRS (with "[" line breaks).</summary>
        public FitResult Measure(string trsTranslation)
        {
            string text = TextEscaping.ToRuntime(trsTranslation);
            if (_template != null)
                text = _template.Replace("@OVERHOTSPOT@", text, StringComparison.Ordinal);
            if (Control.Kind == GuiTextKind.Label)
            {
                int lines = _font.Wrap(text, Control.Width, _options).Count;
                int needed = (lines - 1) * _font.LineHeight + _font.Height;
                return new FitResult(needed <= Control.Height || lines <= _originalSize, needed, Control.Height, IsHeight: true);
            }
            int width = _font.MeasureWidth(text, _options);
            return new FitResult(width <= Control.Width || width <= _originalSize, width, Control.Width, IsHeight: false);
        }
    }
}
