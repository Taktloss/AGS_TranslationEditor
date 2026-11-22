using System.Linq;
using System.Threading.Tasks;
using AGS_TranslationEditor.Services;
using AGSFontEditor.Localization;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Platform.Storage;

namespace AGS_TranslationEditor.Views
{
    /// <summary>File pickers and message boxes owned by a window.</summary>
    public sealed class WindowDialogs : IDialogService
    {
        private readonly Window _owner;

        public WindowDialogs(Window owner) => _owner = owner;

        private static FilePickerFileType ToType(FileFilter f) => new(f.Name) { Patterns = f.Patterns };

        public async Task<string?> OpenFileAsync(string title, params FileFilter[] filters)
        {
            var files = await _owner.StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions
            {
                Title = title,
                AllowMultiple = false,
                FileTypeFilter = filters.Select(ToType).ToList(),
            });
            return files.Count > 0 ? files[0].Path.LocalPath : null;
        }

        public async Task<string?> SaveFileAsync(string title, string extension, FileFilter filter, string? suggestedName = null)
        {
            var file = await _owner.StorageProvider.SaveFilePickerAsync(new FilePickerSaveOptions
            {
                Title = title,
                DefaultExtension = extension,
                SuggestedFileName = suggestedName,
                FileTypeChoices = new[] { ToType(filter) },
            });
            return file?.Path.LocalPath;
        }

        public async Task<string?> PickFolderAsync(string title)
        {
            var folders = await _owner.StorageProvider.OpenFolderPickerAsync(new FolderPickerOpenOptions { Title = title });
            return folders.Count > 0 ? folders[0].Path.LocalPath : null;
        }

        public async Task<bool> AskAsync(string message)
        {
            var dialog = new Window
            {
                Title = Loc.T("AGS Translation Editor"),
                Width = 460,
                SizeToContent = SizeToContent.Height,
                WindowStartupLocation = WindowStartupLocation.CenterOwner,
                CanResize = false
            };
            bool result = false;
            var panel = new StackPanel { Margin = new Avalonia.Thickness(16), Spacing = 12 };
            panel.Children.Add(new TextBlock { Text = message, TextWrapping = TextWrapping.Wrap });
            var buttons = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right, Spacing = 8 };
            var yes = new Button { Content = Loc.T("Yes") };
            var no = new Button { Content = Loc.T("No") };
            yes.Click += (s, e) => { result = true; dialog.Close(); };
            no.Click += (s, e) => { result = false; dialog.Close(); };
            buttons.Children.Add(yes);
            buttons.Children.Add(no);
            panel.Children.Add(buttons);
            dialog.Content = panel;
            await dialog.ShowDialog(_owner);
            return result;
        }

        public async Task ShowInfoAsync(string message, string? title = null)
        {
            var dialog = new Window
            {
                Title = title ?? Loc.T("Game Info"),
                Width = 460,
                SizeToContent = SizeToContent.Height,
                MaxHeight = 600,
                WindowStartupLocation = WindowStartupLocation.CenterOwner,
                CanResize = false
            };
            var panel = new StackPanel { Margin = new Avalonia.Thickness(16), Spacing = 12 };
            panel.Children.Add(new ScrollViewer
            {
                MaxHeight = 480,
                Content = new SelectableTextBlock { Text = message, TextWrapping = TextWrapping.Wrap }
            });
            var ok = new Button { Content = Loc.T("OK"), HorizontalAlignment = HorizontalAlignment.Right };
            ok.Click += (s, e) => dialog.Close();
            panel.Children.Add(ok);
            dialog.Content = panel;
            await dialog.ShowDialog(_owner);
        }
    }
}
