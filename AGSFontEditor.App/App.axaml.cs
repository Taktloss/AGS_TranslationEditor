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
            string[] args = desktop.Args ?? System.Array.Empty<string>();
            if (args.FirstOrDefault() == "--images")
            {
                // --images [game]: the image (sprite) editor instead of the font editor
                var images = new SpriteEditorWindow();
                desktop.MainWindow = images;
                if (args.Length > 1)
                    images.Opened += async (s, e) => await images.ViewModel.LoadGameAsync(args[1]);
                base.OnFrameworkInitializationCompleted();
                return;
            }
            var window = new FontEditorWindow();
            desktop.MainWindow = window;
            // Optional argument: a game file or a font file to open
            string? path = args.FirstOrDefault();
            if (path != null)
                window.Opened += async (s, e) => await window.LoadPath(path);
        }
        base.OnFrameworkInitializationCompleted();
    }
}
