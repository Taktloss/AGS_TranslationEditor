using System;
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
            this.FindControl<ComboBox>("CmbProvider")!.SelectedIndex = Math.Max(0, Array.IndexOf(MachineTranslators.Names, _settings.MtProvider));
            this.FindControl<TextBox>("TxtSourceLanguage")!.Text = _settings.MtSourceLanguage;
            this.FindControl<TextBox>("TxtTargetLanguage")!.Text = _settings.MtTargetLanguage;
            this.FindControl<ComboBox>("CmbFormality")!.SelectedIndex = Math.Max(0, (int)ParseFormality(_settings.MtFormality));
            this.FindControl<TextBox>("TxtDeepLKey")!.Text = _settings.DeepLApiKey;
            this.FindControl<TextBox>("TxtClaudeKey")!.Text = _settings.ClaudeApiKey;
            var model = this.FindControl<TextBox>("TxtClaudeModel")!;
            model.Text = _settings.ClaudeModel;
            model.Watermark = ClaudeTranslator.DefaultModel;
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

        // Order of the language box
        private static readonly string[] Languages = { Loc.System, Loc.English, Loc.German };

        public static Formality ParseFormality(string value) =>
            Enum.TryParse(value, out Formality formality) ? formality : Formality.Default;

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
