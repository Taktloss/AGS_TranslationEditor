using System;
using System.Windows.Forms;

namespace AGS_TranslationEditor
{
    public partial class frmSettings : Form
    {
        public frmSettings()
        {
            InitializeComponent();
        }

        private void checkBox1_CheckedChanged(object sender, EventArgs e)
        {
            txtApiKey.Enabled = !txtApiKey.Enabled;
        }

        private void btnSave_Click(object sender, EventArgs e)
        {
            Properties.Settings.Default.UseYandex = checkBoxYandex.Checked;
            Properties.Settings.Default.YandexApiKey = txtApiKey.Text;
            Properties.Settings.Default.Save();
            this.Close();
        }

        private void Settings_Load(object sender, EventArgs e)
        {
            checkBoxYandex.Checked = Properties.Settings.Default.UseYandex;
            txtApiKey.Text = Properties.Settings.Default.YandexApiKey;
        }
    }
}
