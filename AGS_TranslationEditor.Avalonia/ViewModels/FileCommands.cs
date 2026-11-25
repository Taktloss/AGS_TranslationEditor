using System.Linq;
using System.Threading.Tasks;
using AGS_TranslationEditor.Services;
using AGSFontEditor.Localization;
using AGSTools;
using CommunityToolkit.Mvvm.Input;

namespace AGS_TranslationEditor.ViewModels
{
    /// <summary>Opening, saving and exporting the translation.</summary>
    public sealed partial class FileCommands
    {
        private readonly MainWindowViewModel _main;

        public FileCommands(MainWindowViewModel main) => _main = main;

        private IDialogService Dialogs => _main.Dialogs;

        internal static readonly FileFilter TrsFilter = new(Loc.T("TRS Translation File"), "*.trs");

        /// <summary>Commands that need an open translation.</summary>
        private bool HasEntries() => _main.HasEntries;

        [RelayCommand]
        private async Task Open()
        {
            string? file = await Dialogs.OpenFileAsync(Loc.T("Open Translation File"),
                new FileFilter(Loc.T("AGS Translation Files"), "*.tra", "*.trs"),
                new FileFilter(Loc.T("CSV/TSV Files"), "*.csv", "*.tsv"),
                new FileFilter(Loc.T("PO Files"), "*.po"),
                new FileFilter(Loc.T("All Files"), "*"));
            if (file != null)
                await _main.LoadFile(file);
        }

        [RelayCommand(CanExecute = nameof(HasEntries))]
        private async Task Save()
        {
            if (string.IsNullOrEmpty(_main.CurrentFilename))
                await SaveAs();
            else
                SaveTo(_main.CurrentFilename);
        }

        [RelayCommand(CanExecute = nameof(HasEntries))]
        private async Task SaveAs()
        {
            string? file = await Dialogs.SaveFileAsync(Loc.T("Save Translation File"), "trs", TrsFilter);
            if (file != null)
                SaveTo(file);
        }

        /// <summary>Saves; line breaks typed into translations would break the TRS format, so they become spaces.</summary>
        private void SaveTo(string path)
        {
            var problematic = _main.Entries.Where(e => e.Value.Contains('\n')).ToList();
            if (problematic.Count > 0)
            {
                _main.BeginEditBatch(Loc.T("Line breaks removed"));
                foreach (var e in problematic)
                    e.Value = e.Value.Replace("\r\n", " ").Replace("\n", " ");
                _main.EndEditBatch();
            }
            _main.SaveFile(path);
            if (problematic.Count > 0)
                _main.FileStatusText = Loc.F("⚠ Warning: {0} translation(s) contain newlines — stripped on save.", problematic.Count);
        }

        [RelayCommand(CanExecute = nameof(HasEntries))]
        private async Task ExportCsv()
        {
            string? file = await Dialogs.SaveFileAsync(Loc.T("Export as CSV"), "csv", new FileFilter(Loc.T("CSV File"), "*.csv"));
            if (file != null)
                CSVFormat.CreateCSV(file, _main.Entries.ToDictionary(e => e.Key, e => e.Value));
        }

        [RelayCommand(CanExecute = nameof(HasEntries))]
        private async Task ExportPo()
        {
            string? file = await Dialogs.SaveFileAsync(Loc.T("Export as PO"), "po", new FileFilter(Loc.T("PO File"), "*.po"));
            if (file != null)
                POFormat.CreatePO(file, _main.Entries.ToDictionary(e => e.Key, e => e.Value));
        }
    }
}
