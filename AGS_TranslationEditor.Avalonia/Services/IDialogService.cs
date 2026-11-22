using System.Threading.Tasks;

namespace AGS_TranslationEditor.Services
{
    /// <summary>A file type for the file dialogs, e.g. ("TRS Translation File", "*.trs").</summary>
    public sealed record FileFilter(string Name, params string[] Patterns);

    /// <summary>
    /// The dialogs the editor's commands need. The window shows real dialogs; tests answer them directly,
    /// so the commands can run without a window.
    /// </summary>
    public interface IDialogService
    {
        /// <summary>Path of the chosen file, or null if cancelled.</summary>
        Task<string?> OpenFileAsync(string title, params FileFilter[] filters);

        /// <summary>Path to save to, or null if cancelled.</summary>
        Task<string?> SaveFileAsync(string title, string extension, FileFilter filter, string? suggestedName = null);

        /// <summary>Path of the chosen folder, or null if cancelled.</summary>
        Task<string?> PickFolderAsync(string title);

        /// <summary>Yes or no.</summary>
        Task<bool> AskAsync(string message);

        Task ShowInfoAsync(string message, string? title = null);
    }

    /// <summary>Answers every dialog with "cancel" and "no", until the window provides real dialogs.</summary>
    public sealed class NoDialogs : IDialogService
    {
        public static readonly NoDialogs Instance = new();
        public Task<string?> OpenFileAsync(string title, params FileFilter[] filters) => Task.FromResult<string?>(null);
        public Task<string?> SaveFileAsync(string title, string extension, FileFilter filter, string? suggestedName = null) => Task.FromResult<string?>(null);
        public Task<string?> PickFolderAsync(string title) => Task.FromResult<string?>(null);
        public Task<bool> AskAsync(string message) => Task.FromResult(false);
        public Task ShowInfoAsync(string message, string? title = null) => Task.CompletedTask;
    }
}
