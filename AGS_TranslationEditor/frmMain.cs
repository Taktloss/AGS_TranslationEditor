/*
    Copyright 2015 Bernd Keilmann

    This file is part of the AGS Translation Editor.

    AGS Translation Editor is free software: you can redistribute it and/or modify
    it under the terms of the GNU General Public License as published by
    the Free Software Foundation, either version 3 of the License, or
    (at your option) any later version.

    AGS Translation Editor is distributed in the hope that it will be useful,
    but WITHOUT ANY WARRANTY; without even the implied warranty of
    MERCHANTABILITY or FITNESS FOR A PARTICULAR PURPOSE.See the
    GNU General Public License for more details.

    You should have received a copy of the GNU General Public License
    along with AGS Translation Editor.  If not, see<http://www.gnu.org/licenses/>.

    Diese Datei ist Teil von AGS Translation Editor.

    AGS Translation Editor ist Freie Software: Sie können es unter den Bedingungen
    der GNU General Public License, wie von der Free Software Foundation,
    Version 3 der Lizenz oder (nach Ihrer Wahl) jeder späteren
    veröffentlichten Version, weiterverbreiten und/oder modifizieren.

    Fubar wird in der Hoffnung, dass es nützlich sein wird, aber
    OHNE JEDE GEWÄHRLEISTUNG, bereitgestellt; sogar ohne die implizite
    Gewährleistung der MARKTFÄHIGKEIT oder EIGNUNG FÜR EINEN BESTIMMTEN ZWECK.
    Siehe die GNU General Public License für weitere Details.

    Sie sollten eine Kopie der GNU General Public License zusammen mit diesem
    Programm erhalten haben.Wenn nicht, siehe <http://www.gnu.org/licenses/>.
*/

using AGSTools;
using System;
using System.Collections.Generic;
using System.Data;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Windows.Forms;
using TranslationApi;

namespace AGS_TranslationEditor
{
    public partial class frmMain : Form
    {
        bool _documentChanged;
        string _currentFilename = "";
        int _numEntries;
        Dictionary<string, string> _translationItems;
        //search
        static int _currentFindIndex;
        List<int> _foundEntries;

        public frmMain()
        {
            InitializeComponent();

            dgvTranslation.Columns["colSource"].DataPropertyName = "Key";
            dgvTranslation.Columns["colTranslation"].DataPropertyName = "Value";
        }

        private void frmMain_Load(object sender, EventArgs e)
        {
            tableLayoutPanel1.ColumnStyles[1].Width = 0;
            toolStripPadding.Width = 0;

            _documentChanged = false;
            saveToolStripMenuItem.Enabled = false;
            saveAsToolStripMenuItem.Enabled = false;
        }

        private void frmMain_FormClosed(object sender, FormClosedEventArgs e)
        {
            if (_documentChanged)
            {
                string question = string.Format(Properties.Resources.SaveTextMessageClose, Path.GetFileName(_currentFilename));
                if (MessageBox.Show(question, "AGS Translation Editor", MessageBoxButtons.YesNo, MessageBoxIcon.Question) == DialogResult.Yes)
                    if (dgvTranslation.Rows.Count > 0)
                        SaveFile(_currentFilename);
            }
        }

        private void openToolStripMenuItem_Click(object sender, EventArgs e)
        {
            OpenFileDialog openDialog = new OpenFileDialog()
            {
                Filter = "AGS Translation File(*.TRA,*.TRS)|*.tra;*.trs"
            };
            if (openDialog.ShowDialog() == DialogResult.OK)
            {
                _currentFilename = openDialog.FileName;

                if (openDialog.FileName.Contains(".tra"))
                    _translationItems = Translation.ParseTRA_Translation(openDialog.FileName);
                else if (openDialog.FileName.Contains(".trs"))
                    _translationItems = Translation.ParseTRS_Translation(openDialog.FileName);

                PopulateGridView(_translationItems);
                EnableButtons();
            }
        }

        private void beendenToolStripMenuItem_Click(object sender, EventArgs e)
        {
            Application.Exit();
        }

        private void saveToolStripMenuItem_Click(object sender, EventArgs e)
        {
            if (dgvTranslation.Rows.Count > 0)
            {
                SaveFile(_currentFilename);
                lblSeperator.Text = Properties.Resources.SaveMessage;
                MessageBox.Show(string.Format(Properties.Resources.SaveTextMessage, _currentFilename), Properties.Resources.SaveMessage);
            }
        }

        private void saveAsToolStripMenuItem_Click(object sender, EventArgs e)
        {
            if (dgvTranslation.Rows.Count > 0)
            {
                SaveFileDialog saveDialog = new SaveFileDialog()
                {
                    AddExtension = true,
                    DefaultExt = "trs",
                    Filter = "AGS Translation File(*.TRS)|*.trs"
                };
                if (saveDialog.ShowDialog() == DialogResult.OK)
                {
                    SaveFile(saveDialog.FileName);
                    MessageBox.Show(string.Format(Properties.Resources.SaveTextMessage, saveDialog.FileName), Properties.Resources.SaveMessage);
                }
            }
        }

        private void CreateTRAMenuItem_Click(object sender, EventArgs e)
        {
            OpenFileDialog openExeDialog = new OpenFileDialog()
            {
                DefaultExt = "exe",
                Filter = "AGS EXE File (*.exe)|*.exe",
                Title = "Game EXE for Translation"
            };
            OpenFileDialog openDialog = new OpenFileDialog()
            {
                DefaultExt = "trs",
                Filter = "TRS Translation File (*.trs)|*.trs",
                Title = "Open TRS Translation you want to use."
            };
            SaveFileDialog saveDialog = new SaveFileDialog()
            {
                DefaultExt = "tra",
                Filter = "TRA Translation File (*.tra)|*.tra",
                Title = "Save TRA Translation as..."
            };

            if (openExeDialog.ShowDialog() == DialogResult.OK)
            {
                if (openDialog.ShowDialog() == DialogResult.OK)
                    if (saveDialog.ShowDialog() == DialogResult.OK)
                    {
                        GameInfo info = GameInfo.GetGameInfo(openExeDialog.FileName);
                        Translation.CreateTRA_File(info, saveDialog.FileName, Translation.ParseTRS_Translation(openDialog.FileName));
                    }
            }
        }

        private void ExportCSVMenuItem_Click(object sender, EventArgs e)
        {
            //Create CSV File
            if (dgvTranslation.Rows.Count > 0)
            {
                SaveFileDialog saveDialog = new SaveFileDialog
                {
                    Filter = "CSV File|*.csv",
                    DefaultExt = "csv",
                    AddExtension = true
                };

                if (saveDialog.ShowDialog() == DialogResult.OK)
                {
                    Dictionary<string, string> gridData = new Dictionary<string, string>(dgvTranslation.Rows.Count);
                    foreach (DataGridViewRow row in dgvTranslation.Rows)
                    {
                        string msgid = (string)row.Cells[0].Value;
                        string msgstr = (string)row.Cells[1].Value;

                        gridData.Add(msgid, msgstr);
                    }
                    CSVFormat.CreateCSV(saveDialog.FileName, gridData);
                }
            }
        }

        private void ExportPOMenuItem_Click(object sender, EventArgs e)
        {
            //Create PO File
            if (dgvTranslation.Rows.Count > 0)
            {
                using (SaveFileDialog saveDialog = new SaveFileDialog()
                {
                    Filter = "PO File|*.po",
                    DefaultExt = "po",
                    AddExtension = true
                })
                {
                    if (saveDialog.ShowDialog() == DialogResult.OK)
                    {
                        Dictionary<string, string> gridData = new Dictionary<string, string>(dgvTranslation.Rows.Count);
                        foreach (DataGridViewRow row in dgvTranslation.Rows)
                        {
                            if (row.Cells[0].Value != null)
                            {
                                string msgid = (string)row.Cells[0].Value;
                                string msgstr = (string)row.Cells[1].Value;

                                gridData.Add(msgid, msgstr);
                            }
                        }
                        POFormat.CreatePO(saveDialog.FileName, gridData);
                    }
                }
            }
        }

        private void ImportPOMenuItem_Click(object sender, EventArgs e)
        {
            using (OpenFileDialog openDialog = new OpenFileDialog()
            {
                Filter = "PO File (*.po)|*.po",
                Title = "Select PO File."
            })
            {
                if (openDialog.ShowDialog() == DialogResult.OK)
                {
                    _currentFilename = openDialog.FileName;
                    PopulateGridView(POFormat.OpenPO(openDialog.FileName));
                    EnableButtons();
                }
            }
        }

        private void ImportCSVToolStripMenuItem_Click(object sender, EventArgs e)
        {
            using (OpenFileDialog openDialog = new OpenFileDialog()
            {
                Filter = "CSV File (*.csv)|*.csv",
                Title = "Select CSV File."
            })
            {
                if (openDialog.ShowDialog() == DialogResult.OK)
                {
                    _currentFilename = openDialog.FileName;
                    PopulateGridView(CSVFormat.OpenCSV(openDialog.FileName));
                    EnableButtons();
                }
            }
        }

        private void ExtractTextMenuItem_Click(object sender, EventArgs e)
        {
            using (OpenFileDialog openDialog = new OpenFileDialog()
            {
                Filter = "AGS File (*.exe,*.bin)|*.exe;*.bin",
                Title = "Select Game EXE or bin file"
            })
            {
                if (openDialog.ShowDialog() == DialogResult.OK)
                {
                    Extraction.ParseAGSFile(openDialog.FileName);

                    NotifyIcon notifyIcon = new NotifyIcon
                    {
                        Icon = Properties.Resources.editor,
                        BalloonTipIcon = ToolTipIcon.Info,
                        Visible = true,
                        BalloonTipTitle = "AGS Translation Editor",
                        BalloonTipText = string.Format(Properties.Resources.ScriptExtractedMessage, Path.GetDirectoryName(openDialog.FileName))
                    };
                    notifyIcon.ShowBalloonTip(3000);
                    notifyIcon.Dispose();
                }
            }
        }

        private void toolStripButtonFind_Click(object sender, EventArgs e)
        {
            if (toolStriptxtSearch.Text.Length != 0)
                FindValue(toolStriptxtSearch.Text);
        }

        private void toolStriptxtSearch_KeyDown(object sender, KeyEventArgs e)
        {
            if (e.KeyCode == Keys.Enter)
            {
                if (toolStriptxtSearch.Text.Length != 0)
                    FindValue(toolStriptxtSearch.Text);

                e.SuppressKeyPress = true;
            }
        }

        private void toolStripButtonNext_Click(object sender, EventArgs e)
        {
            if (_currentFindIndex < _foundEntries.Count)
            {
                _currentFindIndex++;
                SelectRow(_currentFindIndex);
            }
        }

        private void toolStripButtonBack_Click(object sender, EventArgs e)
        {
            if (_currentFindIndex > 0)
            {
                _currentFindIndex--;
                SelectRow(_currentFindIndex);
            }
        }

        private void toolStripButtonSettings_Click(object sender, EventArgs e)
        {
            using (frmSettings frmSettings = new frmSettings())
            {
                frmSettings.ShowDialog();
            }
        }

        private void toolStripButton1_Click(object sender, EventArgs e)
        {
            if (tableLayoutPanel1.ColumnStyles[1].Width == 0)
            {
                tableLayoutPanel1.ColumnStyles[1].Width = 280;
                toolStripPadding.Width = 280;
            }
            else
            {
                tableLayoutPanel1.ColumnStyles[1].Width = 0;
                toolStripPadding.Width = 0;
            }
        }

        protected void dgvTranslation_SelectionChanged(object sender, EventArgs e)
        {
            try
            {
                string originalText = Convert.ToString(dgvTranslation.Rows[dgvTranslation.CurrentRow.Index].Cells[0].Value);
                txtSourceText.Text = originalText;
                txtTranslationText.Text = Convert.ToString(dgvTranslation.Rows[dgvTranslation.CurrentRow.Index].Cells[1].Value);

                if (Properties.Settings.Default.UseGoogle || Properties.Settings.Default.UseBing || Properties.Settings.Default.UseYandex)
                    if (txtTranslationText.Text.Length <= 0 && tableLayoutPanel1.ColumnStyles[1].Width != 0)
                    {
                        ITranslateAPI translateAPI = null;
                        if (Properties.Settings.Default.UseGoogle)
                            translateAPI = new GoogleTranslator();

                        if (Properties.Settings.Default.UseBing)
                            translateAPI = new BingTranslator();
                            
                        lblSuggestion.Text = translateAPI.Translate(originalText, "en", "de");

                        if (Properties.Settings.Default.UseYandex)
                            lblSuggestion.Text = YandexTranslator.
                                Translate(Properties.Settings.Default.YandexApiKey, "en-de", originalText).Result;
                    }
            }
            catch (Exception ex)
            {
                MessageBox.Show(ex.Message);
            }
        }

        private void dgvTranslation_CellValueChanged(object sender, DataGridViewCellEventArgs e)
        {
            _documentChanged = true;
            Text = string.Format("*{0} - AGS Translation Editor", _currentFilename);

            if (_translationItems != null)
                UpdateStatus();
        }

        private void txtTranslationText_KeyDown(object sender, KeyEventArgs e)
        {
            if (e.KeyCode == Keys.Enter)
            {
                dgvTranslation[1, dgvTranslation.CurrentRow.Index].Value = txtTranslationText.Text;
                dgvTranslation.Focus();

                e.SuppressKeyPress = true;
            }
        }

        private void lblSuggestion_Click(object sender, EventArgs e)
        {
            txtTranslationText.Text = ((Label)sender).Text;
        }

        private void aboutMenuItem_Click(object sender, EventArgs e)
        {
            frmAbout about = new frmAbout();
            about.ShowDialog();
        }

        #region UtilityMethods

        private void PopulateGridView(Dictionary<string, string> entryList)
        {
            if (entryList != null)
            {
                //Clear the DataGrid
                dgvTranslation.ClearSelection();

                DataTable dataTable = TableUtils.ToDataTable(entryList.ToList());
                dgvTranslation.DataSource = dataTable;

                _numEntries = entryList.Count;
                lblSeperator.Text = Properties.Resources.LoadMessage;

                //Set Form text to filename
                _documentChanged = false;
                Text = $"{_currentFilename} - AGS Translation Editor";
                UpdateStatus();
            }
            else
                MessageBox.Show($"No entries in {_currentFilename} found.");
        }

        private void UpdateStatus()
        {
            int translatedCount = _numEntries - UnTranslatedCount();
            float progressValue = (translatedCount * 100) / _numEntries;

            //lblEntriesCount.Text = $"Translated: {UnTranslatedCount()}/{_numEntries} ({progressValue} %)";
            lblEntriesCount.Text = $"{Properties.Resources.EntriesCount} {UnTranslatedCount()}/{_numEntries} ({progressValue} %)";
            toolStripProgressBar1.Value = Convert.ToInt32(progressValue);
        }

        private void EnableButtons()
        {
            //Enable Buttons
            saveToolStripMenuItem.Enabled = true;
            saveAsToolStripMenuItem.Enabled = true;
            toolStripButtonOpen.Enabled = true;
            toolStripButtonSave.Enabled = true;
            toolStripButtonFind.Enabled = true;
            ExportMenuItem.Enabled = true;
        }

        private void SaveFile(string filename)
        {
            using (StreamWriter fw = new StreamWriter(filename, false))
            {
                foreach (DataGridViewRow row in dgvTranslation.Rows)
                {
                    fw.WriteLine(row.Cells[0].Value);
                    fw.WriteLine(row.Cells[1].Value);
                }
            }
        }

        private int UnTranslatedCount()
        {
            var queryResults = from DataGridViewRow rows in dgvTranslation.Rows
                               where Convert.ToString(rows.Cells[1].Value) == string.Empty
                               select rows;
            return queryResults.Count();
        }

        private void SelectRow(int index)
        {
            if (_foundEntries.Count > index)
            {
                dgvTranslation.ClearSelection();
                dgvTranslation.Rows[_foundEntries[index]].Selected = true;
                dgvTranslation.FirstDisplayedScrollingRowIndex = _foundEntries[index];
            }
        }

        private void FindValue(string searchValue)
        {
            try
            {
                _foundEntries = new List<int>();

                var result = from queryResult in dgvTranslation.Rows.Cast<DataGridViewRow>()
                             where ((string)queryResult.Cells[0].Value).ToLower().
                             Contains(searchValue.ToLower())
                             select queryResult.Index;
                _foundEntries = result.ToList();

                if (_foundEntries.Count == 0)
                {
                    MessageBox.Show(string.Format(Properties.Resources.NotFound, toolStriptxtSearch.Text), "AGS Translation Editor", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                    return;
                }

                lblFoundEntries.Text = string.Format(Properties.Resources.FoundCountEntries, _foundEntries.Count);
                toolStripButtonBack.Enabled = true;
                toolStripButtonNext.Enabled = true;
                SelectRow(0);
            }
            catch (Exception ex)
            {
                MessageBox.Show(ex.Message);
            }
        }

        #endregion UtilityMethods
    }
}