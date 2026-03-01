using System;
using System.Globalization;
using Avalonia.Data.Converters;
using Avalonia.Media;

namespace AGS_TranslationEditor.Converters
{
    /// <summary>Returns a dark amber brush for untranslated rows, transparent otherwise.</summary>
    public class BoolToBackgroundConverter : IValueConverter
    {
        public static readonly BoolToBackgroundConverter Instance = new();

        public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
            => value is true
                ? new SolidColorBrush(Color.FromRgb(0x2D, 0x20, 0x00))  // dark amber – VS Code panel bg
                : Brushes.Transparent;

        public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
            => throw new NotImplementedException();
    }
}
