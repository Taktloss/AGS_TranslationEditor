using System;
using System.Globalization;
using Avalonia.Data.Converters;
using Avalonia.Media;

namespace AGS_TranslationEditor.Converters
{
    /// <summary>Returns the theme's background for untranslated rows, transparent otherwise.</summary>
    public class BoolToBackgroundConverter : IValueConverter
    {
        public static readonly BoolToBackgroundConverter Instance = new();

        public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
            => value is true ? AGSFontEditor.Theming.AppTheme.Brush("Untranslated") : Brushes.Transparent;

        public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
            => throw new NotImplementedException();
    }
}
