using AGS_TranslationEditor.Services;

namespace AGSTools.Tests;

/// <summary>Answers file dialogs and questions in order, and keeps the messages shown.</summary>
internal sealed class ScriptedDialogs : IDialogService
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
