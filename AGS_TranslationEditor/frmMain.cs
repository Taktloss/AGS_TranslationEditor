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
using System.ComponentModel;
using System.Data;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Text;
using System.Windows.Forms;

namespace AGS_TranslationEditor
{
    public partial class frmMain : Form
    {
        int _selectedRow;
        string _currentfilename = "";
        int _numEntries;
        bool _documentChanged;
        static int _currentFindIndex;

        public frmMain()
        {
            InitializeComponent();
        }

        private void openToolStripMenuItem_Click(object sender, EventArgs e)
        {
            OpenFileDialog openDialog = new OpenFileDialog()
            {
                Filter = "AGS Translation File(*.TRA,*.TRS)|*.tra;*.trs"
            };
            if (openDialog.ShowDialog() == DialogResult.OK)
            {
                Dictionary<string, string> entryList = null;
                _currentfilename = openDialog.FileName;

                if (openDialog.FileName.Contains(".tra"))
                {
                    entryList = Translation.ParseTRA_Translation(openDialog.FileName);
                }
                    
                else if (openDialog.FileName.Contains(".trs"))
                {
                    entryList = Translation.ParseTRS_Translation(openDialog.FileName);
                }
                    

                PopulateGridView(entryList);
                EnableButtons();
            }
        }

        private void EnableButtons()
        {
            //Enable Buttons
            toolStripButtonStats.Enabled = true;
            saveToolStripMenuItem.Enabled = true;
            saveAsToolStripMenuItem.Enabled = true;
            toolStripButtonOpen.Enabled = true;
            toolStripButtonSave.Enabled = true;
            toolStripButtonFind.Enabled = true;
        }

        public static DataTable ToDataTable<T>(IList<T> data)
        {
            PropertyDescriptorCollection properties =
                TypeDescriptor.GetProperties(typeof(T));
            DataTable table = new DataTable();
            foreach (PropertyDescriptor prop in properties)
                table.Columns.Add(prop.Name, Nullable.GetUnderlyingType(prop.PropertyType) ?? prop.PropertyType);
            foreach (T item in data)
            {
                DataRow row = table.NewRow();
                foreach (PropertyDescriptor prop in properties)
                    row[prop.Name] = prop.GetValue(item) ?? DBNull.Value;
                table.Rows.Add(row);
            }
            return table;
        }

        private void PopulateGridView(Dictionary<string, string> entryList)
        {
            if (entryList != null)
            {
                //Clear the DataGrid
                dataGridView1.ClearSelection();

                //Create new DataTable 
                DataTable dataTable = ToDataTable(entryList.ToList());
                dataGridView1.Columns[0].DataPropertyName = "Key";
                dataGridView1.Columns[1].DataPropertyName = "Value";

                dataGridView1.DataSource = dataTable;

                _numEntries = dataTable.Rows.Count;
                lblFileStatus.Text = Properties.Resources.LoadMessage;
                lblEntriesCount.Text = string.Format(Properties.Resources.EntriesCount, _numEntries);

                //Set Form text to filename
                _documentChanged = false;
                Text = string.Format("{0} - AGS Translation Editor", _currentfilename);
            }
            else
            {
                MessageBox.Show("No Entrys found");
            }
        }

        private void beendenToolStripMenuItem_Click(object sender, EventArgs e)
        {
            Application.Exit();
        }

        private void saveToolStripMenuItem_Click(object sender, EventArgs e)
        {
            if (dataGridView1.Rows.Count > 0)
            {
                SaveFile(_currentfilename);
                lblFileStatus.Text = Properties.Resources.SaveMessage;
                MessageBox.Show(string.Format(Properties.Resources.SaveTextMessage, _currentfilename), Properties.Resources.SaveMessage);
            }
        }

        private void richTextBox2_KeyDown(object sender, KeyEventArgs e)
        {
            if (e.KeyCode == Keys.Enter)
            {
                dataGridView1.Rows[_selectedRow].Cells[1].Value = txtTranslationText.Text;
                dataGridView1.Focus();
                e.SuppressKeyPress = true;
            }
        }

        private void saveAsToolStripMenuItem_Click(object sender, EventArgs e)
        {
            if (dataGridView1.Rows.Count > 0)
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

        private void frmMain_Load(object sender, EventArgs e)
        {
            _documentChanged = false;
            saveToolStripMenuItem.Enabled = false;
            saveAsToolStripMenuItem.Enabled = false;
        }

        private void dataGridView1_SelectionChanged(object sender, EventArgs e)
        {
            try
            {
                _selectedRow = dataGridView1.SelectedRows[0].Index;

                string originalText = (string)dataGridView1.Rows[_selectedRow].Cells[0].Value;
                txtSourceText.Text = originalText;
                string translatedText = (string)dataGridView1.Rows[_selectedRow].Cells[1].Value;
                txtTranslationText.Text = translatedText;

                //For Yandex translation API
                if (Properties.Settings.Default.UseYandex)
                {
                    if (translatedText.Length <= 0)
                        txtTranslationText.Text = YandexTranslationApi.Translate(
                            Properties.Settings.Default.YandexApiKey,
                            Properties.Settings.Default.Language,
                            originalText);
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine(ex.Message);
            }
        }

        private void getGameInfoToolStripMenuItem_Click(object sender, EventArgs e)
        {
            OpenFileDialog openDialog = new OpenFileDialog()
            {
                Filter = "Game EXE File (*.exe)|*.exe"
            };
            if (openDialog.ShowDialog() == DialogResult.OK)
            {
                GameInfo gameinfo = new GameInfo();
                gameinfo = Translation.GetGameInfo(openDialog.FileName);
                MessageBox.Show(string.Format(
                    "AGS Version: {0}\nGame Title: {1} \nGameUID: {2}",
                    gameinfo.Version,
                    gameinfo.GameTitle,
                    gameinfo.GameUID), "Game Information", MessageBoxButtons.OK, MessageBoxIcon.Information);
            }
        }

        private void CreateTRA_MenuItem_Click(object sender, EventArgs e)
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
                GameInfo info = Translation.GetGameInfo(openExeDialog.FileName);
                if (openDialog.ShowDialog() == DialogResult.OK)
                {
                    if (saveDialog.ShowDialog() == DialogResult.OK)
                        Translation.CreateTRA_File(info, saveDialog.FileName, Translation.ParseTRS_Translation(openDialog.FileName));
                }
            }
        }

        private void dataGridView1_CellValueChanged(object sender, DataGridViewCellEventArgs e)
        {
            _documentChanged = true;
            this.Text = string.Format("*{0} - AGS Translation Editor", _currentfilename);
        }

        private void frmMain_FormClosed(object sender, FormClosedEventArgs e)
        {
            if (_documentChanged)
            {
                string question = string.Format(Properties.Resources.SaveTextMessageClose, Path.GetFileName(_currentfilename)); 
                if (MessageBox.Show(question, "AGS Translation Editor", MessageBoxButtons.YesNo, MessageBoxIcon.Question) == DialogResult.Yes)
                {
                    if (dataGridView1.Rows.Count > 0)
                        SaveFile(_currentfilename);
                }
            }
        }

        private void aboutToolStripMenuItem_Click(object sender, EventArgs e)
        {
            frmAbout about = new frmAbout();
            about.ShowDialog();
        }

        private void ExportCSVMenuItem_Click(object sender, EventArgs e)
        {
            //Create CSV File
            if (dataGridView1.Rows.Count > 0)
            {
                SaveFileDialog saveDialog = new SaveFileDialog
                {
                    Filter = "CSV File|*.csv",
                    DefaultExt = "csv",
                    AddExtension = true
                };

                if (saveDialog.ShowDialog() == DialogResult.OK)
                {
                    using (StreamWriter sw = new StreamWriter(new FileStream(saveDialog.FileName, FileMode.Create)))
                    { 
                        foreach (DataGridViewRow row in dataGridView1.Rows)
                        {
                            //remove quotes btw. change them to ' because of format issues
                            string msgid = (string)row.Cells[0].Value;
                            msgid = msgid.Replace('\"', '\'');
                            string msgstr = (string)row.Cells[1].Value;
                            msgstr = msgstr.Replace('\"', '\'');

                            sw.Write("\"{0}\";\"{1}\"\n", msgid, msgstr);
                        }
                    }
                }
            }
        }

        private void ExportPOMenuItem_Click(object sender, EventArgs e)
        {
            //Create PO File
            if (dataGridView1.Rows.Count > 0)
            {
                SaveFileDialog saveDialog = new SaveFileDialog()
                {
                    Filter = "PO File|*.po",
                    DefaultExt = "po",
                    AddExtension = true
                };
                if (saveDialog.ShowDialog() == DialogResult.OK)
                {                 
                    Dictionary<string, string> gridData = new Dictionary<string, string>(dataGridView1.Rows.Count);
                    foreach (DataGridViewRow row in dataGridView1.Rows)
                    {
                        //remove quotes btw. change them to ' because of format issues
                        string msgid = (string)row.Cells[0].Value;
                        msgid = msgid.Replace('\"', '\'');
                        string msgstr = (string)row.Cells[1].Value;
                        msgstr = msgstr.Replace('\"', '\'');
                        gridData.Add(msgid, msgstr);
                    }

                    POFormat.CreatePO(saveDialog.FileName, gridData);
                }
            }
        }

        private void ImportPOMenuItem_Click(object sender, EventArgs e)
        {
            OpenFileDialog openDialog = new OpenFileDialog()
            {
                Filter = "PO File (*.po)|*.po",
                Title = "Select PO File."
            };
            if (openDialog.ShowDialog() == DialogResult.OK)
            {
                PopulateGridView(POFormat.OpenPO(openDialog.FileName));
            }
        }

        private void ExtractTextMenuItem_Click(object sender, EventArgs e)
        {
            OpenFileDialog openDialog = new OpenFileDialog()
            {
                Filter = "AGS File (*.exe,*.bin)|*.exe;*.bin",
                Title = "Select Game EXE or bin file"
            };

            if (openDialog.ShowDialog() == DialogResult.OK)
            {
                Extraction.ParseAGSFile(openDialog.FileName);

                NotifyIcon notifyIcon = new NotifyIcon();

                notifyIcon.Icon = Properties.Resources.editor;
                notifyIcon.BalloonTipIcon = ToolTipIcon.Info;
                notifyIcon.Visible = true;
                notifyIcon.BalloonTipTitle = "AGS Translation Editor";
                notifyIcon.BalloonTipText = string.Format(Properties.Resources.ScriptExtractedMessage, Path.GetDirectoryName(openDialog.FileName));
                notifyIcon.ShowBalloonTip(3000);
            }
        }

        private void toolStripButton1_Click(object sender, EventArgs e)
        {
            frmStats frmStats = new frmStats();
            int notTranslatedCount = GetNotTranslatedCount();

            frmStats.LoadData(_numEntries, notTranslatedCount);
            frmStats.Show();
        }

        private void toolStripButtonFind_Click(object sender, EventArgs e)
        {
            findEntry(toolStriptxtSearch.Text);
        }

        private void toolStripTextBox1_KeyDown(object sender, KeyEventArgs e)
        {
            if (e.KeyCode == Keys.Enter)
            {
                findEntry(toolStriptxtSearch.Text);
                e.SuppressKeyPress = true;
            }
        }
        
        private void toolStripButtonNext_Click(object sender, EventArgs e)
        {
            if (_currentFindIndex < foundEntries.Count)
            {
                _currentFindIndex++;
                SelectDataGridRow(_currentFindIndex);
            }
        }     

        private void toolStripButtonBack_Click(object sender, EventArgs e)
        {
            if (_currentFindIndex > 0)
            {
                _currentFindIndex--;
                SelectDataGridRow(_currentFindIndex);
            }
        }

        private void toolStripButtonSettings_Click(object sender, EventArgs e)
        {
            frmSettings frmSettings = new frmSettings();
            frmSettings.ShowDialog();
        }

        #region UtilityMethods

        private void SaveFile(string filename)
        {
            using (StreamWriter fw = new StreamWriter(filename,false))
            {
                foreach (DataGridViewRow row in dataGridView1.Rows)
                {
                    fw.WriteLine(row.Cells[0].Value);
                    fw.WriteLine(row.Cells[1].Value);
                }
            }
        }

        private int GetNotTranslatedCount()
        {
            DataTable dt = (DataTable)dataGridView1.DataSource;
            var queryResults = from queryResult in dt.AsEnumerable() where string.Equals((string)queryResult.ItemArray[1], "") select queryResult;

            return queryResults.Count(); 
        }

        private void SelectDataGridRow(int index)
        {
            if (foundEntries.Count > index)
            {
                dataGridView1.ClearSelection();
                dataGridView1.Rows[foundEntries[index]].Selected = true;
                dataGridView1.FirstDisplayedScrollingRowIndex = foundEntries[index];
            }
        }

        private List<int> foundEntries;
        private void findEntry(string searchValue)
        {
            try
            {
                foundEntries = new List<int>();
                DataTable dt = (DataTable)dataGridView1.DataSource;

                var queryResults2 = from queryResult in dataGridView1.Rows.Cast<DataGridViewRow>() where ((string)queryResult.Cells[0].Value).ToLower().Contains(searchValue.ToLower()) select queryResult.Index;
                foundEntries = queryResults2.ToList();

                if (foundEntries.Count == 0)
                {
                    MessageBox.Show(string.Format(Properties.Resources.NotFound, toolStriptxtSearch.Text), "AGS Translation Editor", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                    return;
                }

                lblFoundEntries.Text = string.Format(Properties.Resources.FoundCountEntries,foundEntries.Count);
                toolStripButtonBack.Enabled = true;
                toolStripButtonNext.Enabled = true;
                SelectDataGridRow(0);
            }
            catch (Exception ex)
            {
                MessageBox.Show(ex.Message);
            }
        }

        #endregion
    }
}
