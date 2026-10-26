using System;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Styling;
using AGS_TranslationEditor.Models;
using AGSFontEditor.Localization;
using TranslationApi;

namespace AGS_TranslationEditor.Views
{
    public partial class SettingsWindow : Window
    {
        private readonly AppSettings _settings;

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
            cbTheme.SelectedIndex = _settings.Theme == "Light" ? 1 : 0;

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
            _settings.Theme = cbTheme.SelectedIndex == 1 ? "Light" : "Dark";

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

            // Apply theme
            var theme = _settings.Theme == "Light" ? ThemeVariant.Light : ThemeVariant.Dark;
            if (Application.Current != null)
                Application.Current.RequestedThemeVariant = theme;

            _settings.Save();
            Close();
        }
    }
}
