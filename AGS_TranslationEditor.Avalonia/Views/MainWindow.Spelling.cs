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
    // Spell check while typing (Hunspell) and the grammar check with LanguageTool
    public partial class MainWindow
    {
        private readonly object _spellLock = new();
        private SpellChecker? _spelling;
        private string? _spellingKey;
        private string? _spellWordsFolder;
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

        /// <summary>Which dictionary the settings ask for: a name, or null for none.</summary>
        private string? WantedDictionary() => _settings.SpellDictionary switch
        {
            "off" => null,
            "" => SpellChecker.FindDictionary(_settings.MtTargetLanguage),
            string name => SpellChecker.FindDictionaries().TryGetValue(name, out string? path) ? path : null,
        };

        /// <summary>Folder of the open translation, whose words.txt adds own words; read on the UI thread.</summary>
        private string? TranslationFolder() =>
            ViewModel.CurrentFilename.Length > 0 ? Path.GetDirectoryName(Path.GetFullPath(ViewModel.CurrentFilename)) : null;

        /// <summary>
        /// The dictionary for the translation, loaded on first use (any thread), with the word list in
        /// <paramref name="folder"/>; null if switched off or none is installed.
        /// </summary>
        private SpellChecker? Spelling(string? folder)
        {
            string? path = WantedDictionary();
            lock (_spellLock)
            {
                if (path == null)
                    return null;
                if (_spelling == null || _spellingKey != path)
                {
                    try
                    {
                        _spelling = SpellChecker.Load(path);
                    }
                    catch (IOException)
                    {
                        _spelling = null;
                        return null;
                    }
                    _spellingKey = path;
                    _spellWordsFolder = null;
                }
                if (folder != null && folder != _spellWordsFolder)
                {
                    _spelling.AddWordsFrom(Path.Combine(folder, "words.txt"));
                    _spellWordsFolder = folder;
                }
                return _spelling;
            }
        }

        private void SpellingSettingsChanged()
        {
            lock (_spellLock)
                _spellingKey = null;
            _wantedCacheFor = null;
            UpdateSpellBar();
        }

        /// <summary>Lists the unknown words of the current translation under the text box.</summary>
        private void UpdateSpellBar()
        {
            var bar = this.FindControl<Border>("SpellBar")!;
            var words = this.FindControl<WrapPanel>("SpellWords")!;
            string text = ViewModel.TranslationText ?? "";
            string? key = ViewModel.SelectedEntry?.Key;
            if (ViewModel.SelectedEntry == null || string.IsNullOrWhiteSpace(text) || WantedDictionaryCached() == null)
            {
                bar.IsVisible = false;
                return;
            }
            SpellChecker? spelling;
            lock (_spellLock)
                spelling = _spellingKey != null && _spellingKey == WantedDictionaryCached() ? _spelling : null;
            if (spelling == null)
            {
                // The first load takes a moment; do it in the background and come back
                if (!_spellLoading)
                {
                    _spellLoading = true;
                    string? folder = TranslationFolder();
                    System.Threading.Tasks.Task.Run(() => Spelling(folder)).ContinueWith(t => Dispatcher.UIThread.Post(() =>
                    {
                        _spellLoading = false;
                        if (t.IsCompletedSuccessfully && t.Result != null)
                            UpdateSpellBar();
                    }));
                }
                return;
            }
            spelling = Spelling(TranslationFolder());
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

        private string? _wantedCache;
        private string? _wantedCacheFor;

        // FindDictionaries reads folders; remember the answer per settings value
        private string? WantedDictionaryCached()
        {
            string id = _settings.SpellDictionary + "|" + _settings.MtTargetLanguage;
            if (_wantedCacheFor != id)
            {
                _wantedCache = WantedDictionary();
                _wantedCacheFor = id;
            }
            return _wantedCache;
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

        /// <summary>LanguageTool's language code for the target language: de → de-DE, so its spelling rules are on.</summary>
        private string LanguageToolLanguage()
        {
            if (_settings.SpellDictionary is { Length: > 0 } name && name != "off")
                return name.Replace('_', '-');
            string lang = _settings.MtTargetLanguage.Trim();
            if (lang.Contains('-') || lang.Contains('_'))
                return lang.Replace('_', '-');
            return lang.ToLowerInvariant() switch
            {
                "de" => "de-DE",
                "en" => "en-US",
                "pt" => "pt-PT",
                "" => "auto",
                string other => other,
            };
        }

        private async System.Threading.Tasks.Task CheckWithLanguageTool()
        {
            if (ViewModel.IsAiBusy)
                return;
            if (string.IsNullOrWhiteSpace(_settings.LanguageToolUrl))
            {
                ViewModel.FileStatusText = Loc.T("Enter a LanguageTool server in the settings first (public or local).");
                return;
            }
            var entries = ViewModel.Entries.Where(e => !string.IsNullOrWhiteSpace(e.Value)).ToList();
            if (entries.Count == 0)
            {
                ViewModel.FileStatusText = Loc.T("There are no translated entries to check.");
                return;
            }
            LanguageToolClient client;
            try
            {
                client = new LanguageToolClient(_settings.LanguageToolUrl);
            }
            catch (MachineTranslationException ex)
            {
                ViewModel.FileStatusText = ex.Message;
                return;
            }
            string question = client.IsPublic
                ? Loc.F("Check {0} entries with the public LanguageTool server? The texts are sent to {1}; this takes a while because of its request limit.", entries.Count, client.Name)
                : Loc.F("Check {0} entries with LanguageTool at {1}?", entries.Count, client.Name);
            if (!await ShowYesNoDialog(question))
                return;

            CancellationToken token = ViewModel.StartAiWork();
            string language = LanguageToolLanguage();
            ViewModel.StartSuggestions(Loc.F("LanguageTool ({0}, {1})", client.Name, language));
            int found = 0;
            try
            {
                var texts = entries.Select(e => e.Value).ToList();
                await client.CheckAsync(texts, language, matches => Dispatcher.UIThread.Post(() =>
                {
                    ViewModel.AddCorrections(matches.Select(m =>
                    {
                        string original = texts[m.Index];
                        string snippet = original.Substring(m.Offset, m.Length);
                        string reason = $"{m.Message} \"{snippet}\"";
                        string? preview = m.Apply(original);
                        Func<string, string?>? fix = preview == null ? null : current => GrammarMatch.ApplyNear(current, snippet, m.Offset, m.Replacements[0]);
                        return (Loc.T("Grammar (LanguageTool)"), entries[m.Index].Key, reason, preview ?? "", fix);
                    }));
                    found += matches.Count;
                }), new Progress<int>(done => ViewModel.FileStatusText = Loc.F("LanguageTool: {0} of {1} entries checked...", done, entries.Count)), token);
                ViewModel.FileStatusText = Loc.F("LanguageTool found {0} possible problems.", found);
            }
            catch (OperationCanceledException)
            {
                ViewModel.FileStatusText = Loc.F("Cancelled; {0} possible problems found so far.", found);
            }
            catch (MachineTranslationException ex)
            {
                ViewModel.FileStatusText = ex.Message;
            }
            finally
            {
                ViewModel.EndAiWork();
            }
        }

    }
}
