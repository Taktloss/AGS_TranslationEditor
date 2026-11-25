using System;
using System.Collections.Generic;
using System.Linq;
using Avalonia;
using Avalonia.Controls;
using AGS_TranslationEditor.Models;
using AGSFontEditor.Localization;
using AGSFontEditor.Theming;
using TranslationApi;

namespace AGS_TranslationEditor.Views
{
    public partial class SettingsWindow : Window
    {
        private readonly AppSettings _settings;
        private bool _applied;
        private List<string> _dictionaries = new();

        public SettingsWindow(AppSettings settings)
        {
            _settings = settings;
            InitializeComponent();
            LoadSettings();

            this.FindControl<Button>("BtnOK")!.Click += (s, e) => ApplyAndClose();
            this.FindControl<Button>("BtnCancel")!.Click += (s, e) => Close();
        }

        private void LoadSettings()
        {
            this.FindControl<NumericUpDown>("NumFontSize")!.Value = _settings.FontSize;
            this.FindControl<CheckBox>("ChkMonospace")!.IsChecked = _settings.MonospaceFont;
            this.FindControl<CheckBox>("ChkContext")!.IsChecked = _settings.ShowContext;

            var cbEncoding = this.FindControl<ComboBox>("CmbEncoding")!;
            cbEncoding.SelectedIndex = _settings.Encoding == "UTF-8" ? 1 : 0;

            var cbTheme = this.FindControl<ComboBox>("CmbTheme")!;
            cbTheme.ItemsSource = AppTheme.All.Select(p => p.Name switch
            {
                AppTheme.Dark => Loc.T("Dark (default)"),
                AppTheme.Light => Loc.T("Light"),
                string name => name,
            }).ToList();
            cbTheme.SelectedIndex = Math.Max(0, AppTheme.All.ToList().FindIndex(p => p.Name == _settings.Theme));
            // Preview: the theme changes as soon as it is picked; Cancel goes back
            cbTheme.SelectionChanged += (s, e) =>
            {
                if (cbTheme.SelectedIndex >= 0 && Application.Current != null)
                    AppTheme.Apply(Application.Current, AppTheme.All[cbTheme.SelectedIndex].Name);
            };
            Closed += (s, e) =>
            {
                if (!_applied && Application.Current != null)
                    AppTheme.Apply(Application.Current, _settings.Theme);
            };

            this.FindControl<ComboBox>("CmbLanguage")!.SelectedIndex = Math.Max(0, Array.IndexOf(Languages, _settings.UiLanguage));
            var provider = this.FindControl<ComboBox>("CmbProvider")!;
            provider.SelectedIndex = Math.Max(0, Array.IndexOf(MachineTranslators.Names, _settings.MtProvider));
            provider.SelectionChanged += (s, e) => ShowProviderFields();
            ShowProviderFields();
            this.FindControl<TextBox>("TxtSourceLanguage")!.Text = _settings.MtSourceLanguage;
            this.FindControl<TextBox>("TxtTargetLanguage")!.Text = _settings.MtTargetLanguage;
            this.FindControl<ComboBox>("CmbFormality")!.SelectedIndex = Math.Max(0, (int)ParseFormality(_settings.MtFormality));
            this.FindControl<TextBox>("TxtDeepLKey")!.Text = _settings.DeepLApiKey;
            this.FindControl<TextBox>("TxtClaudeKey")!.Text = _settings.ClaudeApiKey;
            var model = this.FindControl<TextBox>("TxtClaudeModel")!;
            model.Text = _settings.ClaudeModel;
            model.Watermark = ClaudeTranslator.DefaultModel;
            var spell = this.FindControl<ComboBox>("CmbSpell")!;
            _dictionaries = AGSTools.SpellChecker.FindDictionaries().Keys.ToList();
            spell.ItemsSource = new[] { Loc.T("Automatic (target language)"), Loc.T("Off") }.Concat(_dictionaries).ToList();
            spell.SelectedIndex = _settings.SpellDictionary switch
            {
                "" => 0,
                "off" => 1,
                string name => _dictionaries.IndexOf(name) is int i and >= 0 ? i + 2 : 0,
            };
            this.FindControl<Button>("BtnSpellFolder")!.Click += async (s, e) =>
            {
                System.IO.Directory.CreateDirectory(AGSTools.SpellChecker.UserFolder);
                await Launcher.LaunchUriAsync(new Uri(AGSTools.SpellChecker.UserFolder + System.IO.Path.DirectorySeparatorChar));
            };
            var languageTool = this.FindControl<TextBox>("TxtLanguageTool")!;
            languageTool.Text = _settings.LanguageToolUrl;
            var ltPresets = this.FindControl<ComboBox>("CmbLanguageToolPreset")!;
            ltPresets.ItemsSource = new[] { Loc.T("Public (languagetool.org)"), Loc.T("Local server (port 8081)"), Loc.T("Off") };
            ltPresets.SelectionChanged += (s, e) =>
            {
                languageTool.Text = ltPresets.SelectedIndex switch
                {
                    0 => LanguageToolClient.PublicUrl,
                    1 => LanguageToolClient.LocalUrl,
                    _ => "",
                };
            };
            var url = this.FindControl<TextBox>("TxtOpenAiUrl")!;
            url.Text = _settings.OpenAiUrl;
            this.FindControl<TextBox>("TxtOpenAiKey")!.Text = _settings.OpenAiApiKey;
            this.FindControl<TextBox>("TxtOpenAiModel")!.Text = _settings.OpenAiModel;
            var presets = this.FindControl<ComboBox>("CmbOpenAiPreset")!;
            presets.ItemsSource = OpenAiCompatibleTranslator.Presets.Select(p => p.Name).ToList();
            presets.SelectionChanged += (s, e) =>
            {
                if (presets.SelectedIndex >= 0)
                    url.Text = OpenAiCompatibleTranslator.Presets[presets.SelectedIndex].Url;
            };
        }

        /// <summary>Only the key, model and address fields of the chosen service are shown.</summary>
        private void ShowProviderFields()
        {
            string service = MachineTranslators.Names[Math.Max(0, this.FindControl<ComboBox>("CmbProvider")!.SelectedIndex)];
            void Show(string name, bool visible) => this.FindControl<Control>(name)!.IsVisible = visible;
            Show("RowFormality", service != MachineTranslators.Google);
            Show("RowDeepLKey", service == MachineTranslators.DeepL);
            Show("RowClaudeKey", service == MachineTranslators.Claude);
            Show("RowClaudeModel", service == MachineTranslators.Claude);
            Show("RowOpenAiUrl", service == MachineTranslators.OpenAi);
            Show("RowOpenAiKey", service == MachineTranslators.OpenAi);
            Show("RowOpenAiModel", service == MachineTranslators.OpenAi);
            Show("NoteKeys", service != MachineTranslators.Google);
        }

        // Order of the language box
        private static readonly string[] Languages = { Loc.System, Loc.English, Loc.German };

        private static Formality ParseFormality(string value) => AppSettings.ParseFormality(value);

        private void ApplyAndClose()
        {
            var numFont = this.FindControl<NumericUpDown>("NumFontSize")!;
            var chkMono = this.FindControl<CheckBox>("ChkMonospace")!;
            var cbEncoding = this.FindControl<ComboBox>("CmbEncoding")!;
            var cbTheme = this.FindControl<ComboBox>("CmbTheme")!;

            _settings.FontSize = (int)(numFont.Value ?? 13);
            _settings.MonospaceFont = chkMono.IsChecked == true;
            _settings.ShowContext = this.FindControl<CheckBox>("ChkContext")!.IsChecked == true;
            _settings.Encoding = cbEncoding.SelectedIndex == 1 ? "UTF-8" : "Latin-1";
            _settings.Theme = AppTheme.All[Math.Max(0, cbTheme.SelectedIndex)].Name;

            _settings.UiLanguage = Languages[Math.Max(0, this.FindControl<ComboBox>("CmbLanguage")!.SelectedIndex)];
            _settings.MtProvider = MachineTranslators.Names[Math.Max(0, this.FindControl<ComboBox>("CmbProvider")!.SelectedIndex)];
            string source = (this.FindControl<TextBox>("TxtSourceLanguage")!.Text ?? "").Trim();
            _settings.MtSourceLanguage = source.Equals("auto", StringComparison.OrdinalIgnoreCase) ? "" : source;
            string target = (this.FindControl<TextBox>("TxtTargetLanguage")!.Text ?? "").Trim();
            _settings.MtTargetLanguage = target.Length > 0 ? target : "de";
            _settings.MtFormality = ((Formality)Math.Max(0, this.FindControl<ComboBox>("CmbFormality")!.SelectedIndex)).ToString();
            _settings.DeepLApiKey = (this.FindControl<TextBox>("TxtDeepLKey")!.Text ?? "").Trim();
            _settings.ClaudeApiKey = (this.FindControl<TextBox>("TxtClaudeKey")!.Text ?? "").Trim();
            _settings.ClaudeModel = (this.FindControl<TextBox>("TxtClaudeModel")!.Text ?? "").Trim();
            int spellIndex = this.FindControl<ComboBox>("CmbSpell")!.SelectedIndex;
            _settings.SpellDictionary = spellIndex switch { <= 0 => "", 1 => "off", int i => _dictionaries[i - 2] };
            _settings.LanguageToolUrl = (this.FindControl<TextBox>("TxtLanguageTool")!.Text ?? "").Trim();
            _settings.OpenAiUrl = (this.FindControl<TextBox>("TxtOpenAiUrl")!.Text ?? "").Trim();
            _settings.OpenAiApiKey = (this.FindControl<TextBox>("TxtOpenAiKey")!.Text ?? "").Trim();
            _settings.OpenAiModel = (this.FindControl<TextBox>("TxtOpenAiModel")!.Text ?? "").Trim();

            if (Application.Current != null)
                AppTheme.Apply(Application.Current, _settings.Theme);
            _applied = true;

            _settings.Save();
            Close();
        }
    }
}
