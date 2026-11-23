using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using Avalonia;
using Avalonia.Media;
using Avalonia.Styling;

namespace AGSFontEditor.Theming
{
    /// <summary>The colors of one theme. Windows use them as dynamic resources named "Th.&lt;Property&gt;".</summary>
    public sealed record Palette(string Name, bool IsDark,
        string Bg, string Panel, string Toolbar, string Input,
        string Border, string BorderSubtle, string BorderStrong,
        string Text, string TextBright, string TextStrong, string TextMuted, string TextDim,
        string Accent, string AccentMuted, string OnAccent,
        string Hover, string Pressed, string Selection,
        string Error, string Warning, string Info, string Success, string Special)
    {
        /// <summary>Background of rows that still need a translation.</summary>
        public string Untranslated => AppTheme.Mix(Bg, Warning, 0.1);
        public string SuccessBg => AppTheme.Mix(Bg, Success, IsDark ? 0.16 : 0.14);
        public string SuccessText => AppTheme.Mix(Text, Success, 0.6);
        public string InfoBg => AppTheme.Mix(Bg, Info, IsDark ? 0.12 : 0.10);
        public string ErrorBg => AppTheme.Mix(Bg, Error, IsDark ? 0.2 : 0.16);
        public string OnAccentMuted => AppTheme.Mix(Accent, OnAccent, 0.8);
        public string Overlay => "#CC" + Bg.TrimStart('#');
        /// <summary>Menus and other pop-ups: a little lighter than the panels in dark themes.</summary>
        public string Popup => IsDark ? AppTheme.Mix(Panel, Text, 0.05) : Bg;
    }

    /// <summary>
    /// The color themes of the translator and the font and image editors: the original dark and light look and the
    /// four Catppuccin flavors. <see cref="Apply"/> replaces the brushes at runtime, so open windows change at once.
    /// </summary>
    public static class AppTheme
    {
        public const string Dark = "Dark";
        public const string Light = "Light";

        public static readonly IReadOnlyList<Palette> All = new[]
        {
            new Palette(Dark, true, "#1E1E1E", "#252526", "#3C3C3C", "#3C3C3C", "#474747", "#2A2A2A", "#6A6A6A",
                "#CCCCCC", "#D4D4D4", "#FFFFFF", "#8A8A8A", "#5A5A5A", "#007ACC", "#0E4F80", "#FFFFFF",
                "#505050", "#094771", "#264F78", "#F48771", "#E8A060", "#75BEFF", "#89D185", "#C586C0"),
            new Palette(Light, false, "#FFFFFF", "#F3F3F3", "#E4E4E4", "#FFFFFF", "#C8C8C8", "#E5E5E5", "#A0A0A0",
                "#333333", "#1E1E1E", "#000000", "#6E6E6E", "#9A9A9A", "#007ACC", "#CCE4F7", "#FFFFFF",
                "#E0E0E0", "#C7DFF5", "#ADD6FF", "#C72E0F", "#B07800", "#0062A3", "#388A34", "#AF00DB"),
            // Catppuccin (https://catppuccin.com/palette): base, mantle, crust, surface0-2, text, overlay1, overlay0, accents
            Catppuccin("Catppuccin Mocha", true, "#1E1E2E", "#181825", "#11111B", "#313244", "#45475A", "#585B70",
                "#CDD6F4", "#7F849C", "#6C7086", "#CBA6F7", "#F38BA8", "#FAB387", "#89B4FA", "#A6E3A1", "#F5C2E7"),
            Catppuccin("Catppuccin Macchiato", true, "#24273A", "#1E2030", "#181926", "#363A4F", "#494D64", "#5B6078",
                "#CAD3F5", "#8087A2", "#6E738D", "#C6A0F6", "#ED8796", "#F5A97F", "#8AADF4", "#A6DA95", "#F5BDE6"),
            Catppuccin("Catppuccin Frappé", true, "#303446", "#292C3C", "#232634", "#414559", "#51576D", "#626880",
                "#C6D0F5", "#838BA7", "#737994", "#CA9EE6", "#E78284", "#EF9F76", "#8CAAEE", "#A6D189", "#F4B8E4"),
            Catppuccin("Catppuccin Latte", false, "#EFF1F5", "#E6E9EF", "#DCE0E8", "#CCD0DA", "#BCC0CC", "#ACB0BE",
                "#4C4F69", "#8C8FA1", "#9CA0B0", "#8839EF", "#D20F39", "#FE640B", "#1E66F5", "#40A02B", "#EA76CB"),
        };

        private static Palette Catppuccin(string name, bool dark, string @base, string mantle, string crust,
            string surface0, string surface1, string surface2, string text, string overlay1, string overlay0,
            string mauve, string red, string peach, string blue, string green, string pink) =>
            new(name, dark, @base, mantle, crust, surface0, surface1, surface0, surface2,
                text, text, text, overlay1, overlay0,
                mauve, Mix(@base, mauve, 0.35), crust,
                surface1, Mix(@base, mauve, 0.22), Mix(@base, blue, 0.25),
                red, peach, blue, green, pink);

        public static Palette Current { get; private set; } = All[0];

        /// <summary>The current brush for a palette color such as "Error", for code that cannot use a dynamic resource.</summary>
        public static IBrush Brush(string color) =>
            Application.Current?.Resources.TryGetValue("Th." + color, out object? brush) == true && brush is IBrush b ? b : Brushes.Gray;

        public static Palette Find(string? name) => All.FirstOrDefault(p => p.Name == name) ?? All[0];

        /// <summary>Sets the theme's brushes and the matching Fluent light or dark variant.</summary>
        public static void Apply(Application app, string? name)
        {
            Palette palette = Find(name);
            Current = palette;
            app.RequestedThemeVariant = palette.IsDark ? ThemeVariant.Dark : ThemeVariant.Light;
            // The brushes are changed in place, so that code holding one (converters, view models) follows the theme too
            foreach (var property in typeof(Palette).GetProperties().Where(p => p.PropertyType == typeof(string) && p.Name != nameof(Palette.Name)))
            {
                Color color = Color.Parse((string)property.GetValue(palette)!);
                if (app.Resources.TryGetValue("Th." + property.Name, out object? existing) && existing is SolidColorBrush brush)
                    brush.Color = color;
                else
                    app.Resources["Th." + property.Name] = new SolidColorBrush(color);
            }
            // Accent of the Fluent controls (focus borders, check boxes, sliders)
            app.Resources["SystemAccentColor"] = Color.Parse(palette.Accent);
            foreach (var (key, color) in FluentBrushes)
                app.Resources[key] = app.Resources["Th." + color];
            app.Resources["OverlayCornerRadius"] = new CornerRadius(8);
            app.Resources["MenuFlyoutPresenterThemePadding"] = new Thickness(4);
            app.Resources["MenuFlyoutPresenterBorderThemeThickness"] = new Thickness(1);
        }

        /// <summary>Fluent resources of menus and pop-ups, set to the theme's own brushes.</summary>
        private static readonly (string Key, string Color)[] FluentBrushes =
        {
            ("MenuFlyoutPresenterBackground", "Popup"),
            ("MenuFlyoutPresenterBorderBrush", "Border"),
            ("MenuFlyoutItemForeground", "Text"),
            ("MenuFlyoutItemForegroundPointerOver", "TextStrong"),
            ("MenuFlyoutItemForegroundPressed", "TextStrong"),
            ("MenuFlyoutItemForegroundDisabled", "TextDim"),
            ("MenuFlyoutItemBackgroundPointerOver", "AccentMuted"),
            ("MenuFlyoutItemBackgroundPressed", "AccentMuted"),
            ("MenuFlyoutItemKeyboardAcceleratorTextForeground", "TextMuted"),
            ("MenuFlyoutItemKeyboardAcceleratorTextForegroundPointerOver", "TextBright"),
            ("MenuFlyoutItemKeyboardAcceleratorTextForegroundDisabled", "TextDim"),
            ("MenuFlyoutSubItemChevron", "TextMuted"),
            ("MenuFlyoutSubItemChevronPointerOver", "TextStrong"),
            ("MenuFlyoutSubItemChevronSubMenuOpened", "TextStrong"),
            ("MenuFlyoutSeparatorBackground", "BorderSubtle"),
            ("DataGridCurrencyVisualPrimaryBrush", "Accent"),
            ("ToolTipBackground", "Popup"),
            ("ToolTipBorderBrush", "Border"),
            ("ToolTipForeground", "Text"),
        };

        /// <summary>The theme saved by the translator, for the standalone font editor.</summary>
        public static string ReadSaved()
        {
            try
            {
                string path = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
                    "AGSTranslationEditor", "settings.json");
                using var json = JsonDocument.Parse(File.ReadAllText(path));
                return json.RootElement.TryGetProperty("Theme", out var value) && value.ValueKind == JsonValueKind.String
                    ? value.GetString()! : Dark;
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException)
            {
                return Dark;
            }
        }

        /// <summary>Color between <paramref name="from"/> (0) and <paramref name="to"/> (1), as #RRGGBB.</summary>
        public static string Mix(string from, string to, double amount)
        {
            Color a = Color.Parse(from), b = Color.Parse(to);
            byte Lerp(byte x, byte y) => (byte)Math.Round(x + (y - x) * amount);
            return $"#{Lerp(a.R, b.R):X2}{Lerp(a.G, b.G):X2}{Lerp(a.B, b.B):X2}";
        }
    }
}
