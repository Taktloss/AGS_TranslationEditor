using Avalonia.Controls;

namespace AGS_TranslationEditor.Views
{
    public partial class AboutDialog : Window
    {
        public AboutDialog()
        {
            InitializeComponent();
            this.FindControl<Button>("BtnOK")!.Click += (s, e) => Close();
        }
    }
}
