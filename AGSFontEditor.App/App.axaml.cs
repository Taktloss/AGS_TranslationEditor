using System.Linq;
using AGSFontEditor.Localization;
using AGSFontEditor.Views;
using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Markup.Xaml;

namespace AGSFontEditor.App;

public partial class App : Application
{
    public override void Initialize() => AvaloniaXamlLoader.Load(this);

    public override void OnFrameworkInitializationCompleted()
    {
        if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
        {
            Loc.SetLanguage(Loc.ReadSavedLanguage());
            var window = new FontEditorWindow();
            desktop.MainWindow = window;
            // Optional argument: a game file or a font file to open
            string? path = desktop.Args?.FirstOrDefault();
            if (path != null)
                window.Opened += async (s, e) => await window.LoadPath(path);
        }
        base.OnFrameworkInitializationCompleted();
    }
}
