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
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text;
using System.Windows.Forms;

namespace AGS_TranslationEditor
{
    public partial class frmMain : Form
    {
        private int _selectedRow = 0;
        private string _currentfilename = "";
        private int _numEntries = 0;
        private bool _documentChanged;
        private static int _currentFindIndex = 0;

        public frmMain()
        {
            InitializeComponent();
        }

        private void openToolStripMenuItem_Click(object sender, EventArgs e)
        {
            OpenFileDialog fileDialog = new OpenFileDialog()
            {
                Filter = "AGS Translation File(*.TRA,*.TRS)|*.tra;*.trs"
            };
            if (fileDialog.ShowDialog() == DialogResult.OK)
            {
                Dictionary<string, string> entryList = null;
                _currentfilename = fileDialog.FileName;

                if (fileDialog.FileName.Contains(".tra"))
                    entryList = Translation.ParseTRA_Translation(fileDialog.FileName);
                else if (fileDialog.FileName.Contains(".trs"))
                    entryList = Translation.ParseTRS_Translation(fileDialog.FileName);

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

        private void PopulateGridView(Dictionary<string, string> entryList)
        {
            _numEntries = 0;
            //Clear the DataGrid
            dataGridView1.Rows.Clear();
            dataGridView1.Refresh();
            dataGridView1.ClearSelection();

            if (entryList != null)
            {
                foreach (KeyValuePair<string, string> pair in entryList)
                {
                    //Populate DataGridView
                    string[] newRow = { pair.Key, pair.Value };
                    dataGridView1.Rows.Add(newRow);
                    _numEntries++;
                }
            }

            lblFileStatus.Text = "File loaded";
            lblEntriesCount.Text = "Entries: " + _numEntries;

            //Set Form text to filename
            _documentChanged = false;
            Text = _currentfilename + " - AGS Translation Editor";
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
                lblFileStatus.Text = "File saved";
                MessageBox.Show(string.Format("File was saved as {0}.", _currentfilename), "File saved",
                    MessageBoxButtons.OK);
            }
        }

        private void richTextBox2_KeyDown(object sender, KeyEventArgs e)
        {
            if (e.KeyCode == Keys.Enter)
            {
                string newText = txtTranslationText.Text;
                dataGridView1.Rows[_selectedRow].Cells[1].Value = newText;
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
                    MessageBox.Show(string.Format("File was saved as {0}.", saveDialog.FileName), "File saved", MessageBoxButtons.OK);
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

                if (Properties.Settings.Default.UseYandex)
                {
                    //For Yandex translation API
                    if (translatedText.Length <= 0)
                        txtTranslationText.Text = YandexTranslationApi.Translate(Properties.Settings.Default.YandexApiKey, Properties.Settings.Default.Language, originalText);
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
                MessageBox.Show(string.Format("AGS Version: {0}\nGame Title: {1} \nGameUID: {2}",
                    gameinfo.Version, gameinfo.GameTitle, gameinfo.GameUID),"Game Information");
            }
        }

        private void CreateTRA_MenuItem_Click(object sender, EventArgs e)
        {
            OpenFileDialog openExeDialog = new OpenFileDialog()
            {
                DefaultExt = "exe",
                Title = "Game EXE for Translation",
                Filter = "AGS EXE File (*.exe)|*.exe"
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
            this.Text = string.Format("{0} • - AGS Translation Editor", _currentfilename);
        }

        private void frmMain_FormClosed(object sender, FormClosedEventArgs e)
        {
            //If data was changed ask if user wants to save
            if (_documentChanged)
            {
                string question = string.Format("Save changes to {0} ?",_currentfilename.Substring(_currentfilename.LastIndexOf('\\') + 1)); 
                if (MessageBox.Show(question, "AGS Translation Editor", MessageBoxButtons.YesNo) == DialogResult.Yes)
                {
                    //Save changes then exit
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
                SaveFileDialog saveDialog = new SaveFileDialog()
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

                            sw.Write("\"{0}\",\"{1}\"\n", msgid, msgstr);
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
                MessageBox.Show("Script extracted.","Status");
            }
        }

        private void toolStripButton1_Click(object sender, EventArgs e)
        {
            frmStats frmStats = new frmStats();
            int notTranslatedCount = CountNotTranslated();

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

        private void SelectDataGridRow(int index)
        {
            dataGridView1.ClearSelection();
            dataGridView1.Rows[foundEntries[index]].Selected = true;
            dataGridView1.FirstDisplayedScrollingRowIndex = foundEntries[index];
            dataGridView1.Focus();
        }

        private int CountNotTranslated()
        {
            int translatedCount = 0;
            foreach (DataGridViewRow row in dataGridView1.Rows)
            {
                string value = (string)row.Cells[1].Value;
                if (string.Equals(value, ""))
                    translatedCount++;
            }
            return translatedCount;
        }

        private List<int> foundEntries;
        private void findEntry(string searchValue)
        {
            try
            {
                int rowIndex = 0;
                dataGridView1.SelectionMode = DataGridViewSelectionMode.FullRowSelect;
                foundEntries = new List<int>();

                foreach (DataGridViewRow row in dataGridView1.Rows)
                {
                    if (row.Cells[rowIndex].Value.ToString().ToLower().Contains(searchValue.ToLower()))
                        foundEntries.Add(row.Index);
                }

                if (foundEntries.Count == 0)
                {
                    MessageBox.Show("No Entry found for: " + toolStriptxtSearch.Text, "Not Found");
                    return;
                }

                lblFoundEntries.Text = "Found " + foundEntries.Count + " entries";
                toolStripButtonBack.Enabled = true;
                toolStripButtonNext.Enabled = true;
                SelectDataGridRow(foundEntries[0]);
            }
            catch (Exception ex)
            {
                MessageBox.Show(ex.Message);
            }
        }

        #endregion
    }
}
