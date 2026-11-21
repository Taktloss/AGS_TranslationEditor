using AGSFontEditor.Localization;
using System;
using System.Collections.ObjectModel;
using System.Linq;
using AGSTools;
using Avalonia.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace AGS_TranslationEditor.ViewModels
{
    /// <summary>One room, script or character in the progress panel.</summary>
    public sealed class ProgressRow
    {
        public ProgressRow(ProgressGroup group, string displayName)
        {
            Group = group;
            DisplayName = displayName;
        }

        public ProgressGroup Group { get; }
        public string DisplayName { get; }
        public double Percent => Group.Percent;
        public string Counts => Loc.F("{0} / {1} · {2:0} %", Group.Translated, Group.Total, Math.Floor(Group.Percent));
        public bool IsDone => Group.Open == 0;
    }

    /// <summary>
    /// Progress panel: how much of each room, script or character is translated, from the context file
    /// (origins of the texts). Selecting a group shows only its texts in the table.
    /// </summary>
    public partial class ProgressViewModel : ViewModelBase
    {
        private readonly MainWindowViewModel _main;
        private readonly DispatcherTimer _refreshTimer;

        [ObservableProperty]
        private bool _isVisible;

        /// <summary>0 = by place (room/script), 1 = by character.</summary>
        [ObservableProperty]
        private int _groupingIndex;

        [ObservableProperty]
        private ProgressRow? _selectedRow;

        [ObservableProperty]
        private string _summary = string.Empty;

        [ObservableProperty]
        private bool _hasContext;

        public ObservableCollection<ProgressRow> Rows { get; } = new();

        public ProgressViewModel(MainWindowViewModel main)
        {
            _main = main;
            // Edits come one by one (or thousands from auto-translate): recount once they settle
            _refreshTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(400) };
            _refreshTimer.Tick += (s, e) =>
            {
                _refreshTimer.Stop();
                Refresh();
            };
        }

        public ProgressGrouping Grouping => GroupingIndex == 1 ? ProgressGrouping.Speaker : ProgressGrouping.Place;

        partial void OnIsVisibleChanged(bool value)
        {
            if (value)
            {
                _main.EnsureContextLoaded();
                Refresh();
            }
            else
            {
                SelectedRow = null;
            }
        }

        partial void OnGroupingIndexChanged(int value)
        {
            SelectedRow = null;
            Refresh();
        }

        partial void OnSelectedRowChanged(ProgressRow? value) =>
            _main.SetGroupFilter(value == null ? null : (Grouping, value.Group.Name));

        [RelayCommand]
        private void Close() => IsVisible = false;

        [RelayCommand]
        private void ClearFilter() => SelectedRow = null;

        /// <summary>A translation changed: recount soon, if the panel is open.</summary>
        public void OnEntryEdited()
        {
            if (!IsVisible)
                return;
            _refreshTimer.Stop();
            _refreshTimer.Start();
        }

        /// <summary>Another file was loaded or the context changed.</summary>
        public void OnDocumentLoaded()
        {
            SelectedRow = null;
            if (IsVisible)
            {
                _main.EnsureContextLoaded();
                Refresh();
            }
        }

        public void Refresh()
        {
            HasContext = _main.HasContext;
            int total = _main.Entries.Count;
            int translated = _main.Entries.Count(e => !e.IsUntranslated);
            Summary = Loc.F("Total: {0} / {1} translated ({2:0} %)", translated, total, total == 0 ? 0 : Math.Floor(100.0 * translated / total));

            string? selected = SelectedRow?.Group.Name;
            Rows.Clear();
            if (!HasContext)
                return;
            var groups = TranslationProgress.Compute(_main.Entries.Select(e => (e.OriginInfo, !e.IsUntranslated)), Grouping);
            foreach (ProgressGroup group in groups)
                Rows.Add(new ProgressRow(group, DisplayName(group.Name)));
            // Keep the filter while counting again
            if (selected != null)
                SelectedRow = Rows.FirstOrDefault(r => r.Group.Name == selected);
        }

        private string DisplayName(string name)
        {
            if (name.Length == 0)
                return Grouping == ProgressGrouping.Speaker ? Loc.T("(no speaker)") : Loc.T("(unknown origin)");
            if (name == TextContext.Narration)
                return Loc.T("(narration, Display)");
            if (Grouping == ProgressGrouping.Speaker)
                return name;
            // Texts without a room or script are named by their kind
            return name switch
            {
                nameof(TextSource.Gui) => Loc.T("GUI (buttons, labels)"),
                nameof(TextSource.CharacterName) => Loc.T("Character names"),
                nameof(TextSource.InventoryItem) => Loc.T("Inventory items"),
                nameof(TextSource.GlobalMessage) => Loc.T("Global messages"),
                nameof(TextSource.DialogLine) => Loc.T("Dialog lines"),
                nameof(TextSource.Script) => Loc.T("Scripts"),
                _ => name,
            };
        }
    }
}
