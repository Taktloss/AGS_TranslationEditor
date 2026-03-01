using Avalonia;
using Avalonia.Controls;
using Avalonia.Styling;
using AGS_TranslationEditor.Models;

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

            var cbEncoding = this.FindControl<ComboBox>("CmbEncoding")!;
            cbEncoding.SelectedIndex = _settings.Encoding == "UTF-8" ? 1 : 0;

            var cbTheme = this.FindControl<ComboBox>("CmbTheme")!;
            cbTheme.SelectedIndex = _settings.Theme == "Light" ? 1 : 0;
        }

        private void ApplyAndClose()
        {
            var numFont = this.FindControl<NumericUpDown>("NumFontSize")!;
            var chkMono = this.FindControl<CheckBox>("ChkMonospace")!;
            var cbEncoding = this.FindControl<ComboBox>("CmbEncoding")!;
            var cbTheme = this.FindControl<ComboBox>("CmbTheme")!;

            _settings.FontSize = (int)(numFont.Value ?? 13);
            _settings.MonospaceFont = chkMono.IsChecked == true;
            _settings.Encoding = cbEncoding.SelectedIndex == 1 ? "UTF-8" : "Latin-1";
            _settings.Theme = cbTheme.SelectedIndex == 1 ? "Light" : "Dark";

            // Apply theme
            var theme = _settings.Theme == "Light" ? ThemeVariant.Light : ThemeVariant.Dark;
            if (Application.Current != null)
                Application.Current.RequestedThemeVariant = theme;

            _settings.Save();
            Close();
        }
    }
}
