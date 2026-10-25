using System;
using Avalonia;

namespace AGSFontEditor.App;

sealed class Program
{
    // Usage: AGSFontEditor.App [game file | font file]
    [STAThread]
    public static void Main(string[] args) => BuildAvaloniaApp().StartWithClassicDesktopLifetime(args);

    // Also used by the visual designer
    public static AppBuilder BuildAvaloniaApp()
        => AppBuilder.Configure<App>()
            .UsePlatformDetect()
            .WithInterFont()
            .LogToTrace();
}
