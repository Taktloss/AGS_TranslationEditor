using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using AGS_TranslationEditor.Models;
using AGS_TranslationEditor.Services;
using AGSFontEditor.Localization;
using AGSTools;
using Avalonia.Threading;
using CommunityToolkit.Mvvm.Input;
using TranslationApi;

namespace AGS_TranslationEditor.ViewModels
{
    /// <summary>
    /// Machine translation, AI review, shortening texts that do not fit, and the grammar check with LanguageTool.
    /// Every paid call is confirmed first; results that change texts are one undo step or wait in the check panel.
    /// </summary>
    public sealed partial class AiCommands
    {
        private readonly MainWindowViewModel _main;

        public AiCommands(MainWindowViewModel main) => _main = main;

        private IDialogService Dialogs => _main.Dialogs;
        private AppSettings Settings => _main.Settings;

        private string Status
        {
            set => _main.FileStatusText = value;
        }

        /// <summary>Runs on the UI thread: answers of the services arrive on other threads.</summary>
        private static void OnUi(Action action)
        {
            if (Dispatcher.UIThread.CheckAccess())
                action();
            else
                Dispatcher.UIThread.Post(action);
        }

        /// <summary>The toolbar button: the selected entry, or all empty entries if none is selected.</summary>
        [RelayCommand]
        private Task Translate() => TranslateEntries();

        [RelayCommand]
        private Task TranslateEmpty()
        {
            _main.SelectedEntry = null;
            return TranslateEntries();
        }

        [RelayCommand]
        private async Task TranslateSelected()
        {
            if (_main.SelectedEntry == null)
                Status = Loc.T("Select an entry first.");
            else
                await TranslateEntries();
        }

        private MachineTranslationOptions Options(string? glossaryPath, IReadOnlyList<string?>? lineNotes = null) => new()
        {
            SourceLanguage = Settings.MtSourceLanguage,
            TargetLanguage = Settings.MtTargetLanguage,
            Formality = AppSettings.ParseFormality(Settings.MtFormality),
            GameTitle = _main.GetDocument().Options.GameName,
            Glossary = glossaryPath != null ? Glossary.Read(glossaryPath) : null,
            LineNotes = lineNotes,
        };

        /// <summary>
        /// Machine-translates the selected entry, or all empty entries if none is selected, with the service
        /// chosen in the settings. A glossary next to the translation is passed on to the AI services.
        /// </summary>
        private async Task TranslateEntries()
        {
            IMachineTranslator translator;
            try
            {
                translator = MachineTranslators.Create(Settings.MtProvider, Settings.Services);
            }
            catch (MachineTranslationException ex)
            {
                Status = ex.Message;
                return;
            }

            var entries = _main.SelectedEntry != null
                ? new List<TranslationEntry> { _main.SelectedEntry }
                : _main.Entries.Where(e => string.IsNullOrEmpty(e.Value)).ToList();
            if (entries.Count == 0)
            {
                Status = Loc.T("No empty entries to translate.");
                return;
            }
            string direction = $"{(Settings.MtSourceLanguage.Length > 0 ? Settings.MtSourceLanguage : "auto")} → {Settings.MtTargetLanguage}";
            if (entries.Count > 1 && !await Dialogs.AskAsync(Loc.F("Translate {0} empty entries with {1} ({2})?", entries.Count, translator.Name, direction)
                + (translator is GoogleTranslator ? "" : "\n\n" + Loc.T("The service bills for the translated text."))))
                return;

            string? glossaryPath = _main.FindGlossary();
            // Who says a line and where it appears helps the AI with tone and form of address
            _main.EnsureContextLoaded();
            bool withNotes = translator is ILanguageModel && entries.Any(e => e.OriginInfo != null);
            var options = Options(glossaryPath, withNotes ? entries.Select(e => e.OriginInfo?.Note()).ToList() : null);

            if (_main.IsAiBusy)
                return;
            int count = 0;
            bool fitCheck = false;
            var cancellation = _main.StartAiWork();
            // Many entries are one undo step
            bool batch = entries.Count > 1;
            if (batch)
                _main.BeginEditBatch(Loc.F("Translated with {0}", translator.Name));
            Status = Loc.F("Translating with {0}...", translator.Name);
            try
            {
                await MachineTranslation.TranslateAsync(translator, entries.Select(e => e.Key).ToList(), options, (index, text) =>
                    OnUi(() =>
                    {
                        entries[index].Value = text;
                        _main.DocumentChanged = true;
                        count++;
                        Status = Loc.F("Translating with {0}... {1}/{2}", translator.Name, count, entries.Count);
                    }), cancellation);
                await Dispatcher.UIThread.InvokeAsync(() =>
                    Status = Loc.F(count == 1 ? "Translated 1 entry with {1}" : "Translated {0} entries with {1}", count, translator.Name)
                        + (glossaryPath != null && translator is ILanguageModel ? " " + Loc.F("(glossary: {0})", Path.GetFileName(glossaryPath)) : "")
                        + (withNotes ? " " + Loc.T("(with speaker and origin)") : ""));
                fitCheck = count > 0 && entries.Count > 1;
            }
            catch (OperationCanceledException)
            {
                await Dispatcher.UIThread.InvokeAsync(() => Status = Loc.F("Translation cancelled after {0} entries; they are kept.", count));
            }
            catch (Exception ex) when (ex is MachineTranslationException or System.Net.Http.HttpRequestException or IOException)
            {
                await Dispatcher.UIThread.InvokeAsync(() => Status = Loc.F("Translation stopped after {0} entries: {1}", count, ex.Message));
            }
            finally
            {
                if (batch)
                    _main.EndEditBatch();
                _main.EndAiWork();
            }
            if (fitCheck)
                await CheckFitAfterTranslation(entries.Where(e => !string.IsNullOrEmpty(e.Value)).Select(e => e.Key).ToHashSet());
        }

        /// <summary>The AI service from the settings, or null (with a status text) if it is no language model.</summary>
        private ILanguageModel? CreateLanguageModel()
        {
            try
            {
                return MachineTranslators.CreateLanguageModel(Settings.MtProvider, Settings.Services);
            }
            catch (MachineTranslationException ex)
            {
                Status = ex.Message + " " + Loc.T("(Tools → Settings)");
                return null;
            }
        }

        [RelayCommand]
        private Task Review() => ReviewEntries(selectedOnly: false);

        [RelayCommand]
        private Task ReviewSelected() => ReviewEntries(selectedOnly: true);

        /// <summary>
        /// Lets the AI service review translated entries (all, or the selected one) and shows its corrections in the
        /// check panel, where each can be applied.
        /// </summary>
        private async Task ReviewEntries(bool selectedOnly)
        {
            List<TranslationEntry> entries = selectedOnly
                ? (_main.SelectedEntry is { } sel && !string.IsNullOrEmpty(sel.Value) ? new List<TranslationEntry> { sel } : new List<TranslationEntry>())
                : _main.Entries.Where(e => !string.IsNullOrEmpty(e.Value)).ToList();
            if (entries.Count == 0)
            {
                Status = selectedOnly ? Loc.T("Select a translated entry first.") : Loc.T("No translated entries to review.");
                return;
            }
            if (_main.IsAiBusy || CreateLanguageModel() is not { } model)
                return;
            if (entries.Count > 1 && !await Dialogs.AskAsync(Loc.F("Let {0} review {1} translated entries? The answers appear in the check panel, where you can apply them one by one.", model.Name, entries.Count)
                    + "\n\n" + Loc.T("The service bills for the sent and received text.")))
                return;

            MachineTranslationOptions options = Options(_main.FindGlossary());
            var items = entries.Select(e => new ReviewItem(e.Key, e.Value)).ToList();
            int found = 0;
            var cancellation = _main.StartAiWork();
            _main.StartSuggestions(Loc.F("AI review with {0}...", model.Name));
            var progress = new Progress<int>(done => Status = Loc.F("Reviewing with {0}... {1}/{2}", model.Name, done, entries.Count));
            void Finish(string summary, string status)
            {
                _main.CheckSummary = summary;
                Status = status;
            }
            try
            {
                await AiAssistant.ReviewAsync(model, items, options, suggestions =>
                    OnUi(() =>
                    {
                        found += suggestions.Count;
                        _main.AddSuggestions(suggestions.Select(s => (Loc.T("AI review"), entries[s.Index].Key, s.Problem, (string?)s.Suggestion)));
                    }), progress, cancellation);
                await Dispatcher.UIThread.InvokeAsync(() => Finish(Loc.F("{0} found {1} problems in {2} entries.", model.Name, found, entries.Count),
                    Loc.T("Review finished - check each suggestion before applying it.")));
            }
            catch (OperationCanceledException)
            {
                string text = Loc.F("Review cancelled; {0} problems found so far.", found);
                await Dispatcher.UIThread.InvokeAsync(() => Finish(text, text));
            }
            catch (Exception ex) when (ex is MachineTranslationException or System.Net.Http.HttpRequestException or IOException)
            {
                string text = Loc.F("Review stopped: {0}", ex.Message);
                await Dispatcher.UIThread.InvokeAsync(() => Finish(text, text));
            }
            finally
            {
                await Dispatcher.UIThread.InvokeAsync(_main.EndAiWork);
            }
        }

        /// <summary>
        /// Checks the translation against the game, then asks the AI service for shorter versions of the texts that
        /// are too wide or too high for their GUI control. Every answer is measured with the game fonts and checked for
        /// placeholders and line breaks; only answers that fit can be applied.
        /// </summary>
        [RelayCommand]
        private async Task Shorten()
        {
            if (_main.Entries.Count == 0)
            {
                Status = Loc.T("Open a translation to check first.");
                return;
            }
            if (_main.IsAiBusy || CreateLanguageModel() is not { } model)
                return;
            string? gameFile = await _main.Game.PickGameFile(Loc.T("Select the AGS Game File of this Translation"));
            if (gameFile == null || await FindTextsThatDoNotFit(gameFile, null) is not { } items)
                return;
            if (items.Count == 0)
            {
                Status = Loc.T("All texts fit into their GUI controls; nothing to shorten.");
                return;
            }
            if (!await Dialogs.AskAsync(Loc.F("{0} texts do not fit into their buttons or labels. Let {1} suggest shorter versions?", items.Count, model.Name)
                    + "\n\n" + Loc.T("The service bills for the sent and received text.")))
                return;
            await ShortenTexts(model, items);
        }

        /// <summary>
        /// After auto-translating: checks whether the new texts fit their buttons and labels, if the game file
        /// is known (chosen before in this session, or the live preview game), and offers to shorten those
        /// that do not.
        /// </summary>
        private async Task CheckFitAfterTranslation(HashSet<string> keys)
        {
            string? gameFile = _main.LastGameFile ?? (File.Exists(Settings.LivePreviewGame) ? Settings.LivePreviewGame : null);
            string translated = _main.FileStatusText;
            if (gameFile == null)
            {
                Status = translated + " " + Loc.T("Check the width with AI → Shorten Texts That Do Not Fit.");
                return;
            }
            if (await FindTextsThatDoNotFit(gameFile, keys) is not { } items)
                return;
            if (items.Count == 0)
            {
                Status = translated + " " + Loc.T("All new texts fit into their GUI controls.");
                return;
            }
            Status = translated;
            if (!await Dialogs.AskAsync(Loc.F("{0} of the new texts do not fit into their buttons or labels. Let the AI suggest shorter versions?", items.Count)
                    + "\n\n" + Loc.T("The service bills for the sent and received text.")))
                return;
            if (CreateLanguageModel() is { } model)
                await ShortenTexts(model, items);
        }

        /// <summary>Translated texts that are too wide or high for their GUI controls; only <paramref name="keys"/> if given; null on error.</summary>
        private async Task<List<FitRequest>?> FindTextsThatDoNotFit(string gameFile, HashSet<string>? keys)
        {
            _main.IsLoading = true;
            Status = Loc.F("Checking against {0}...", Path.GetFileName(gameFile));
            try
            {
                var doc = _main.GetDocument();
                var progressText = new Progress<string>(message => Status = message);
                var issues = await Task.Run(() =>
                    TranslationCheck.Run(doc, TextExtractor.Extract(gameFile, null, progressText), GameFonts.Load(gameFile)));
                return TextsThatDoNotFit(doc, issues, keys);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or InvalidDataException)
            {
                Status = Loc.F("Check failed: {0}", ex.Message);
                return null;
            }
            finally
            {
                _main.IsLoading = false;
            }
        }

        /// <summary>One request per translated text with fit findings (only <paramref name="keys"/> if given).</summary>
        public static List<FitRequest> TextsThatDoNotFit(TranslationDocument doc, IEnumerable<CheckIssue> issues, HashSet<string>? keys) =>
            issues.Where(i => i.Fit != null && (keys == null || keys.Contains(i.Key))
                    && doc.Entries.TryGetValue(i.Key, out string? t) && !string.IsNullOrEmpty(t))
                .GroupBy(i => i.Key)
                .Select(g => new FitRequest(g.Key, doc.Entries[g.Key], g.Select(i => i.Fit!).ToList()))
                .ToList();

        private async Task ShortenTexts(ILanguageModel model, List<FitRequest> items)
        {
            if (_main.IsAiBusy)
                return;
            var cancellation = _main.StartAiWork();
            _main.StartSuggestions(Loc.F("Shorter texts from {0}...", model.Name));
            Status = Loc.F("Shortening with {0}...", model.Name);
            var progress = new Progress<int>(done => Status = Loc.F("Shortening with {0}... {1}/{2}", model.Name, done, items.Count));
            try
            {
                List<FitSuggestion> results = await AiAssistant.ShortenToFitAsync(model, items, Options(_main.FindGlossary()), progress, cancellation);
                _main.AddSuggestions(results.OrderBy(r => r.Suggestion == null).Select(r =>
                    (Loc.T(r.Suggestion != null ? "Shorter text" : "Not shortened"), items[r.Index].Original, Reports.FitNote(r), r.Suggestion)));
                int fits = results.Count(r => r.Suggestion != null);
                _main.CheckSummary = Loc.F("{0} of {1} texts shortened so that they fit (measured with the game fonts).", fits, items.Count);
                Status = Loc.T("Check each suggestion before applying it.");
            }
            catch (OperationCanceledException)
            {
                _main.CheckSummary = Loc.T("Shortening cancelled.");
                Status = Loc.T("Shortening cancelled.");
            }
            catch (Exception ex) when (ex is MachineTranslationException or System.Net.Http.HttpRequestException or IOException)
            {
                _main.CheckSummary = Loc.F("Stopped: {0}", ex.Message);
                Status = Loc.F("Stopped: {0}", ex.Message);
            }
            finally
            {
                _main.EndAiWork();
            }
        }

        /// <summary>LanguageTool's language code for the target language: de → de-DE, so its spelling rules are on.</summary>
        public static string LanguageToolLanguage(string spellDictionary, string targetLanguage)
        {
            if (spellDictionary is { Length: > 0 } name && name != "off")
                return name.Replace('_', '-');
            string lang = targetLanguage.Trim();
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

        [RelayCommand]
        private async Task CheckGrammar()
        {
            if (_main.IsAiBusy)
                return;
            if (string.IsNullOrWhiteSpace(Settings.LanguageToolUrl))
            {
                Status = Loc.T("Enter a LanguageTool server in the settings first (public or local).");
                return;
            }
            var entries = _main.Entries.Where(e => !string.IsNullOrWhiteSpace(e.Value)).ToList();
            if (entries.Count == 0)
            {
                Status = Loc.T("There are no translated entries to check.");
                return;
            }
            LanguageToolClient client;
            try
            {
                client = new LanguageToolClient(Settings.LanguageToolUrl);
            }
            catch (MachineTranslationException ex)
            {
                Status = ex.Message;
                return;
            }
            string question = client.IsPublic
                ? Loc.F("Check {0} entries with the public LanguageTool server? The texts are sent to {1}; this takes a while because of its request limit.", entries.Count, client.Name)
                : Loc.F("Check {0} entries with LanguageTool at {1}?", entries.Count, client.Name);
            if (!await Dialogs.AskAsync(question))
                return;

            CancellationToken token = _main.StartAiWork();
            string language = LanguageToolLanguage(Settings.SpellDictionary, Settings.MtTargetLanguage);
            _main.StartSuggestions(Loc.F("LanguageTool ({0}, {1})", client.Name, language));
            int found = 0;
            try
            {
                var texts = entries.Select(e => e.Value).ToList();
                await client.CheckAsync(texts, language, matches => OnUi(() =>
                {
                    _main.AddCorrections(matches.Select(m =>
                    {
                        string original = texts[m.Index];
                        string snippet = original.Substring(m.Offset, m.Length);
                        string reason = $"{m.Message} \"{snippet}\"";
                        string? preview = m.Apply(original);
                        Func<string, string?>? fix = preview == null ? null : current => GrammarMatch.ApplyNear(current, snippet, m.Offset, m.Replacements[0]);
                        return (Loc.T("Grammar (LanguageTool)"), entries[m.Index].Key, reason, preview ?? "", fix);
                    }));
                    found += matches.Count;
                }), new Progress<int>(done => Status = Loc.F("LanguageTool: {0} of {1} entries checked...", done, entries.Count)), token);
                Status = Loc.F("LanguageTool found {0} possible problems.", found);
            }
            catch (OperationCanceledException)
            {
                Status = Loc.F("Cancelled; {0} possible problems found so far.", found);
            }
            catch (MachineTranslationException ex)
            {
                Status = ex.Message;
            }
            finally
            {
                _main.EndAiWork();
            }
        }
    }
}
