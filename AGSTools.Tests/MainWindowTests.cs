using System.Runtime.CompilerServices;
using AGS_TranslationEditor.ViewModels;
using AGS_TranslationEditor.Views;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Avalonia.LogicalTree;
using Avalonia.Threading;

[assembly: AvaloniaTestApplication(typeof(AGSTools.Tests.HeadlessApp))]

namespace AGSTools.Tests;

/// <summary>The editor app without a screen, for the window tests.</summary>
public static class HeadlessApp
{
    public static AppBuilder BuildAvaloniaApp() =>
        AppBuilder.Configure<AGS_TranslationEditor.App>().UseHeadless(new AvaloniaHeadlessPlatformOptions());
}

internal static class TestSettings
{
    /// <summary>
    /// The window loads and saves its settings: keep them in a folder of the test run, never the user's
    /// (.NET reads XDG_CONFIG_HOME for ApplicationData on every call).
    /// </summary>
    [ModuleInitializer]
    internal static void UseOwnConfigFolder()
    {
        string folder = Path.Combine(Path.GetTempPath(), "agstools-tests-config");
        Directory.CreateDirectory(folder);
        Environment.SetEnvironmentVariable("XDG_CONFIG_HOME", folder);
    }
}

/// <summary>
/// The real main window, without a screen: bindings, keyboard shortcuts and what the code-behind does.
/// File dialogs are answered by <see cref="ScriptedDialogs"/>, so none ever opens.
/// </summary>
public class MainWindowTests
{
    // Click handlers in the code-behind instead of commands: other windows and closing
    private static readonly string[] ClickHandled = { "MenuExit", "MenuFontEditor", "MenuSpriteEditor", "MenuSettings", "MenuAbout" };

    private sealed class Editor : IDisposable
    {
        public readonly TempDir Tmp = new();
        public readonly MainWindow Window;
        public readonly MainWindowViewModel Vm = new();
        public readonly ScriptedDialogs Dialogs = new();
        public readonly string Trs;

        public Editor(params (string Source, string Translation)[] entries)
        {
            Trs = Tmp.File("Deutsch.trs");
            var doc = new TranslationDocument();
            foreach (var (s, t) in entries)
                doc.Entries.Add(s, t);
            TrsFile.Write(Trs, doc);

            Window = new MainWindow { DataContext = Vm };
            Vm.Dialogs = Dialogs; // after the window has set its own
            Window.Show();
            Dispatcher.UIThread.RunJobs();
        }

        public async Task Open()
        {
            await Vm.LoadFile(Trs);
            Dispatcher.UIThread.RunJobs();
        }

        public T Control<T>(string name) where T : Control => Window.FindControl<T>(name)!;

        public void Press(PhysicalKey key, RawInputModifiers modifiers = RawInputModifiers.None)
        {
            Window.KeyPressQwerty(key, modifiers);
            Window.KeyReleaseQwerty(key, modifiers);
            Dispatcher.UIThread.RunJobs();
        }

        public void Dispose()
        {
            Vm.ResetDocumentChanged();
            Window.Close();
            Dispatcher.UIThread.RunJobs();
            Tmp.Dispose();
        }
    }

    private static IEnumerable<MenuItem> AllMenuItems(MainWindow window) =>
        window.GetLogicalDescendants().OfType<Menu>().SelectMany(m => m.Items.OfType<MenuItem>()).SelectMany(Flatten);

    private static IEnumerable<MenuItem> Flatten(MenuItem item) =>
        new[] { item }.Concat(item.Items.OfType<MenuItem>().SelectMany(Flatten));

    [AvaloniaFact]
    public void Every_menu_entry_does_something()
    {
        using var editor = new Editor();

        List<MenuItem> items = AllMenuItems(editor.Window).ToList();
        var leaves = items.Where(i => !i.Items.OfType<MenuItem>().Any()).ToList();
        var dead = leaves.Where(i => i.Command == null && !ClickHandled.Contains(i.Name))
            .Select(i => i.Name ?? i.Header?.ToString()).ToList();

        Assert.True(leaves.Count > 20, $"only {leaves.Count} menu entries found");
        Assert.Empty(dead); // a broken binding leaves Command empty
        Assert.All(ClickHandled, name => Assert.Contains(items, i => i.Name == name));
    }

    [AvaloniaFact]
    public async Task Enter_in_the_search_box_selects_the_next_match()
    {
        using var editor = new Editor(("Hello there", ""), ("Good bye", ""), ("Bye bye", "Tschüss"));
        await editor.Open();

        var search = editor.Control<TextBox>("TxtSearch");
        search.Focus();
        editor.Window.KeyTextInput("bye");
        editor.Press(PhysicalKey.Enter);

        Assert.Equal("bye", editor.Vm.SearchText);
        Assert.Equal("Good bye", editor.Vm.SelectedEntry?.Key);
        Assert.Same(editor.Vm.SelectedEntry, editor.Control<DataGrid>("DgvTranslation").SelectedItem);

        // Enter again goes on to the next match, and after the last back to the first
        search.Focus();
        editor.Press(PhysicalKey.Enter);
        Assert.Equal("Bye bye", editor.Vm.SelectedEntry?.Key);
        search.Focus();
        editor.Press(PhysicalKey.Enter);
        Assert.Equal("Good bye", editor.Vm.SelectedEntry?.Key);
    }

    [AvaloniaFact]
    public async Task Typing_Ctrl_S_and_Ctrl_Z_work_in_the_window()
    {
        using var editor = new Editor(("Hello", ""), ("Bye", ""));
        await editor.Open();
        editor.Vm.SelectedEntry = editor.Vm.Entries[0];
        Dispatcher.UIThread.RunJobs();

        var translation = editor.Control<TextBox>("TxtTranslation");
        translation.Focus();
        editor.Window.KeyTextInput("Hallo");
        Dispatcher.UIThread.RunJobs();
        Assert.Equal("Hallo", editor.Vm.Entries[0].Value);

        editor.Press(PhysicalKey.S, RawInputModifiers.Control);
        Assert.Equal("Hallo", TrsFile.Read(editor.Trs).Entries["Hello"]);
        Assert.False(editor.Vm.DocumentChanged);

        // Outside a text box Ctrl+Z is the editor's undo
        editor.Control<DataGrid>("DgvTranslation").Focus();
        editor.Press(PhysicalKey.Z, RawInputModifiers.Control);
        Assert.Equal("", editor.Vm.Entries[0].Value);
    }

    [AvaloniaFact]
    public async Task The_filter_button_shows_only_untranslated_texts()
    {
        using var editor = new Editor(("One", "Eins"), ("Two", ""), ("Three", ""));
        await editor.Open();
        var grid = editor.Control<DataGrid>("DgvTranslation");
        Assert.Equal(3, grid.ItemsSource!.Cast<object>().Count());

        editor.Control<Avalonia.Controls.Primitives.ToggleButton>("BtnFilterUntranslated").IsChecked = true;
        Dispatcher.UIThread.RunJobs();

        Assert.True(editor.Vm.FilterUntranslated);
        Assert.Equal(new[] { "Two", "Three" }, grid.ItemsSource!.Cast<AGS_TranslationEditor.Models.TranslationEntry>().Select(e => e.Key));
    }

    [AvaloniaFact]
    public async Task F3_jumps_to_the_next_untranslated_text()
    {
        using var editor = new Editor(("One", "Eins"), ("Two", "Zwei"), ("Three", ""));
        await editor.Open();
        editor.Vm.SelectedEntry = editor.Vm.Entries[0];
        editor.Control<DataGrid>("DgvTranslation").Focus();

        editor.Press(PhysicalKey.F3);

        Assert.Equal("Three", editor.Vm.SelectedEntry?.Key);
    }

    [AvaloniaFact]
    public async Task Closing_with_changes_asks_and_saves_on_yes()
    {
        var editor = new Editor(("Hello", ""));
        try
        {
            await editor.Open();
            editor.Vm.SelectedEntry = editor.Vm.Entries[0];
            Dispatcher.UIThread.RunJobs();
            editor.Control<TextBox>("TxtTranslation").Focus();
            editor.Window.KeyTextInput("Hallo");
            Dispatcher.UIThread.RunJobs();
            Assert.True(editor.Vm.DocumentChanged);
            editor.Dialogs.Answers(true);

            editor.Window.Close();
            Dispatcher.UIThread.RunJobs();

            Assert.Contains("Deutsch.trs", Assert.Single(editor.Dialogs.Asked));
            Assert.Equal("Hallo", TrsFile.Read(editor.Trs).Entries["Hello"]);
            Assert.False(editor.Window.IsVisible);
            // The window keeps its layout in the test's own settings file
            Assert.StartsWith(Path.GetTempPath(), AGS_TranslationEditor.Models.AppSettings.ConfigPath);
            Assert.True(File.Exists(AGS_TranslationEditor.Models.AppSettings.ConfigPath));
        }
        finally
        {
            editor.Tmp.Dispose();
        }
    }
}
