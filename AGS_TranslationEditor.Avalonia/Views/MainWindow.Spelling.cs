using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using Avalonia.Controls;
using Avalonia.Media;
using Avalonia.Threading;
using AGS_TranslationEditor.ViewModels;
using AGSFontEditor.Localization;
using AGSFontEditor.Theming;
using AGSTools;
using TranslationApi;

namespace AGS_TranslationEditor.Views
{
    // Spell check while typing (Hunspell): the unknown words under the text box
    public partial class MainWindow
    {
        private bool _spellLoading;
        private DispatcherTimer? _spellTimer;

        private void AttachSpelling()
        {
            _spellTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(350) };
            _spellTimer.Tick += (s, e) =>
            {
                _spellTimer.Stop();
                UpdateSpellBar();
            };
            // The view model is set after the constructor
            MainWindowViewModel? watched = null;
            void Changed(object? s, System.ComponentModel.PropertyChangedEventArgs e)
            {
                if (e.PropertyName is nameof(MainWindowViewModel.TranslationText) or nameof(MainWindowViewModel.SelectedEntry))
                {
                    _spellTimer.Stop();
                    _spellTimer.Start();
                }
            }
            DataContextChanged += (s, e) =>
            {
                if (watched != null)
                    watched.PropertyChanged -= Changed;
                watched = DataContext as MainWindowViewModel;
                if (watched != null)
                    watched.PropertyChanged += Changed;
            };
        }

        private void SpellingSettingsChanged()
        {
            ViewModel.Spelling.Reset();
            UpdateSpellBar();
        }

        /// <summary>Lists the unknown words of the current translation under the text box.</summary>
        private void UpdateSpellBar()
        {
            var bar = this.FindControl<Border>("SpellBar")!;
            var words = this.FindControl<WrapPanel>("SpellWords")!;
            string text = ViewModel.TranslationText ?? "";
            string? key = ViewModel.SelectedEntry?.Key;
            if (ViewModel.SelectedEntry == null || string.IsNullOrWhiteSpace(text) || ViewModel.Spelling.WantedDictionaryCached() == null)
            {
                bar.IsVisible = false;
                return;
            }
            SpellChecker? spelling = ViewModel.Spelling.IfLoaded();
            if (spelling == null)
            {
                // The first load takes a moment; do it in the background and come back
                if (!_spellLoading)
                {
                    _spellLoading = true;
                    string? folder = ViewModel.TranslationFolder();
                    var service = ViewModel.Spelling; // DataContext is read on the UI thread only
                    System.Threading.Tasks.Task.Run(() => service.Get(folder)).ContinueWith(t => Dispatcher.UIThread.Post(() =>
                    {
                        _spellLoading = false;
                        if (t.IsCompletedSuccessfully && t.Result != null)
                            UpdateSpellBar();
                    }));
                }
                return;
            }
            spelling = ViewModel.Spelling.Get(ViewModel.TranslationFolder());
            if (spelling == null)
            {
                bar.IsVisible = false;
                return;
            }

            List<SpellingError> errors = spelling.Check(text, key);
            words.Children.Clear();
            if (errors.Count == 0)
            {
                bar.IsVisible = false;
                return;
            }
            words.Children.Add(new TextBlock
            {
                Text = Loc.F("Unknown to {0}:", spelling.Name),
                Foreground = AppTheme.Brush("TextMuted"),
                FontSize = 11,
                VerticalAlignment = Avalonia.Layout.VerticalAlignment.Center,
                Margin = new Avalonia.Thickness(0, 0, 6, 0),
            });
            foreach (SpellingError error in errors.GroupBy(e => e.Word).Select(g => g.First()))
                words.Children.Add(WordButton(spelling, error));
            bar.IsVisible = true;
        }

        private Button WordButton(SpellChecker spelling, SpellingError error)
        {
            var flyout = new MenuFlyout();
            var button = new Button
            {
                Content = new TextBlock { Text = error.Word, TextDecorations = TextDecorations.Underline },
                Foreground = AppTheme.Brush("Error"),
                Background = Brushes.Transparent,
                Padding = new Avalonia.Thickness(5, 1),
                Margin = new Avalonia.Thickness(0, 0, 4, 0),
                FontSize = 12,
                Flyout = flyout,
            };
            ToolTip.SetTip(button, Loc.T("Click for suggestions"));
            button.Click += (s, e) =>
            {
                // Suggestions are slow for long words; look them up only when asked
                flyout.Items.Clear();
                IReadOnlyList<string> suggestions = spelling.Suggest(error.Word);
                foreach (string suggestion in suggestions)
                {
                    var item = new MenuItem { Header = suggestion, FontWeight = FontWeight.SemiBold };
                    item.Click += (_, _) => ReplaceWord(error, suggestion);
                    flyout.Items.Add(item);
                }
                if (suggestions.Count == 0)
                    flyout.Items.Add(new MenuItem { Header = Loc.T("No suggestions"), IsEnabled = false });
                flyout.Items.Add(new Separator());
                var add = new MenuItem { Header = Loc.F("Add \"{0}\" to my word list", error.Word) };
                add.Click += (_, _) =>
                {
                    try
                    {
                        spelling.AddToUserWords(error.Word);
                        ViewModel.FileStatusText = Loc.F("\"{0}\" added to {1}", error.Word, SpellChecker.UserWordsFile);
                    }
                    catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
                    {
                        ViewModel.FileStatusText = Loc.F("Could not save the word list: {0}", ex.Message);
                    }
                    UpdateSpellBar();
                };
                flyout.Items.Add(add);
                var ignore = new MenuItem { Header = Loc.T("Ignore until the program closes") };
                ignore.Click += (_, _) =>
                {
                    spelling.AddWord(error.Word);
                    UpdateSpellBar();
                };
                flyout.Items.Add(ignore);
            };
            return button;
        }

        private void ReplaceWord(SpellingError error, string replacement)
        {
            string text = ViewModel.TranslationText ?? "";
            int at = error.Start + error.Length <= text.Length && text.Substring(error.Start, error.Length) == error.Word
                ? error.Start
                : text.IndexOf(error.Word, StringComparison.Ordinal);
            if (at < 0)
                return;
            ViewModel.TranslationText = text[..at] + replacement + text[(at + error.Word.Length)..];
            UpdateSpellBar();
        }
    }
}
