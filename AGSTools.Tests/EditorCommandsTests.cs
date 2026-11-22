using AGS_TranslationEditor.Services;
using AGS_TranslationEditor.ViewModels;

namespace AGSTools.Tests;

/// <summary>The editor's commands, run without a window: the dialogs are answered by the test.</summary>
public class EditorCommandsTests
{
    /// <summary>Answers file dialogs and questions in order, and keeps the messages shown.</summary>
    private sealed class ScriptedDialogs : IDialogService
    {
        private readonly Queue<string?> _paths = new();
        private readonly Queue<bool> _answers = new();
        public List<string> Shown { get; } = new();
        public List<string> Asked { get; } = new();

        public ScriptedDialogs Paths(params string?[] paths)
        {
            foreach (string? p in paths) _paths.Enqueue(p);
            return this;
        }

        public ScriptedDialogs Answers(params bool[] answers)
        {
            foreach (bool a in answers) _answers.Enqueue(a);
            return this;
        }

        private Task<string?> NextPath() => Task.FromResult(_paths.Count > 0 ? _paths.Dequeue() : null);
        public Task<string?> OpenFileAsync(string title, params FileFilter[] filters) => NextPath();
        public Task<string?> SaveFileAsync(string title, string extension, FileFilter filter, string? suggestedName = null) => NextPath();
        public Task<string?> PickFolderAsync(string title) => NextPath();

        public Task<bool> AskAsync(string message)
        {
            Asked.Add(message);
            return Task.FromResult(_answers.Count > 0 && _answers.Dequeue());
        }

        public Task ShowInfoAsync(string message, string? title = null)
        {
            Shown.Add(message);
            return Task.CompletedTask;
        }
    }

    private static string BuildGame(TempDir tmp, params string[] buttons)
    {
        var builder = new GameDataBuilder { Title = "Command Test" };
        builder.Buttons.AddRange(buttons);
        string game = tmp.File("game28.dta");
        File.WriteAllBytes(game, builder.Build());
        return game;
    }

    private static MainWindowViewModel Editor(ScriptedDialogs dialogs) => new() { Dialogs = dialogs };

    [Fact]
    public async Task Extract_writes_the_texts_opens_them_and_reports()
    {
        using var tmp = new TempDir();
        string game = BuildGame(tmp, "New Game", "Quit");
        string trs = tmp.File("Deutsch.trs");
        var dialogs = new ScriptedDialogs().Paths(game, trs);
        var editor = Editor(dialogs);

        await editor.Game.ExtractCommand.ExecuteAsync(null);

        Assert.True(File.Exists(trs));
        Assert.Equal(new[] { "New Game", "Quit" }, editor.Entries.Select(e => e.Key));
        Assert.Equal(trs, editor.CurrentFilename);
        Assert.Equal(game, editor.LastGameFile); // the font and image editors open it
        Assert.Contains("2", Assert.Single(dialogs.Shown));
    }

    [Fact]
    public async Task Cancelling_a_dialog_does_nothing()
    {
        using var tmp = new TempDir();
        string game = BuildGame(tmp, "Quit");
        var dialogs = new ScriptedDialogs().Paths(game, null); // no place to save
        var editor = Editor(dialogs);

        await editor.Game.ExtractCommand.ExecuteAsync(null);

        Assert.Empty(editor.Entries);
        Assert.Empty(dialogs.Shown);
        Assert.Empty(Directory.EnumerateFiles(tmp.Path, "*.trs"));
    }

    [Fact]
    public async Task Create_tra_takes_the_game_id_and_writes_the_file()
    {
        using var tmp = new TempDir();
        string game = BuildGame(tmp, "Quit");
        string trs = tmp.File("Deutsch.trs");
        File.WriteAllText(trs, "Quit\nBeenden\n");
        string tra = tmp.File("Deutsch.tra");
        var editor = Editor(new ScriptedDialogs().Paths(game, trs, tra));

        await editor.Game.CreateTraCommand.ExecuteAsync(null);

        Assert.True(File.Exists(tra));
        TranslationDocument read = TraFile.Read(tra);
        Assert.Equal("Beenden", read.Entries["Quit"]);
        Assert.Equal("Command Test", read.Options.GameName);
        Assert.Contains("Deutsch.tra", editor.FileStatusText);
    }

    [Fact]
    public async Task Install_asks_first_and_uninstall_puts_everything_back()
    {
        using var tmp = new TempDir();
        string game = BuildGame(tmp, "Quit");
        string trs = tmp.File("Deutsch.trs");
        File.WriteAllText(trs, "Quit\nBeenden\n");
        var dialogs = new ScriptedDialogs();
        var editor = Editor(dialogs);
        await editor.LoadFile(trs);

        // "No" to installing: nothing happens
        dialogs.Paths(game).Answers(false);
        await editor.Game.InstallCommand.ExecuteAsync(null);
        Assert.False(File.Exists(tmp.File("Deutsch.tra")));

        // "Yes", without extra fonts
        dialogs.Paths(game).Answers(true, false);
        await editor.Game.InstallCommand.ExecuteAsync(null);
        Assert.True(File.Exists(tmp.File("Deutsch.tra")));
        Assert.Contains("Deutsch", dialogs.Shown[^1]);

        dialogs.Paths(game).Answers(true);
        await editor.Game.UninstallCommand.ExecuteAsync(null);
        Assert.False(File.Exists(tmp.File("Deutsch.tra")));
    }

    [Fact]
    public async Task Save_turns_line_breaks_into_spaces_as_one_undo_step()
    {
        using var tmp = new TempDir();
        string trs = tmp.File("t.trs");
        File.WriteAllText(trs, "One\n\nTwo\n\n");
        var editor = Editor(new ScriptedDialogs());
        await editor.LoadFile(trs);
        editor.Entries[0].Value = "Eins\nzwei";
        editor.Entries[1].Value = "Drei\r\nvier";

        await editor.Files.SaveCommand.ExecuteAsync(null);

        TranslationDocument saved = TrsFile.Read(trs);
        Assert.Equal(("Eins zwei", "Drei vier"), (saved.Entries["One"], saved.Entries["Two"]));
        editor.UndoCommand.Execute(null);
        Assert.Equal(("Eins\nzwei", "Drei\r\nvier"), (editor.Entries[0].Value, editor.Entries[1].Value));
    }

    [Fact]
    public void Find_matches_source_and_translation_ignoring_case()
    {
        static AGS_TranslationEditor.Models.TranslationEntry E(string key, string value) => new() { Key = key, Value = value };
        var entries = new[] { E("Open the door", ""), E("Quit", "Beenden"), E("Door bell", "Türklingel") };
        Assert.Equal(new[] { 0, 2 }, MainWindowViewModel.FindMatches(entries, "DOOR"));
        Assert.Equal(new[] { 1 }, MainWindowViewModel.FindMatches(entries, "beend"));
        Assert.Empty(MainWindowViewModel.FindMatches(entries, ""));
    }

    [Theory]
    [InlineData("", "de", "de-DE")]
    [InlineData("", "EN", "en-US")]
    [InlineData("", "pt_BR", "pt-BR")]
    [InlineData("", "", "auto")]
    [InlineData("de_AT", "de", "de-AT")]
    [InlineData("off", "fr", "fr")]
    public void LanguageTool_gets_a_full_language_code(string dictionary, string target, string expected) =>
        Assert.Equal(expected, AiCommands.LanguageToolLanguage(dictionary, target));

    [Fact]
    public void Long_warning_lists_are_cut_with_a_count()
    {
        var text = new System.Text.StringBuilder();
        Reports.AppendWarnings(text, Enumerable.Range(1, 12).Select(i => $"w{i}").ToList());
        string s = text.ToString();
        Assert.Contains("• w10", s);
        Assert.DoesNotContain("• w11", s);
        Assert.Contains("2", s.Split('\n').Last(l => l.Length > 0));
    }
}
