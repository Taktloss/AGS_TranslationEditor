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
                //Clear the DataGrid
                dataGridView1.Rows.Clear();
                dataGridView1.Refresh();
                dataGridView1.ClearSelection();

                _numEntries = 0;
                Dictionary<string, string> entryList = null;
                _currentfilename = fileDialog.FileName;

                if (fileDialog.FileName.Contains(".tra"))
                {
                    entryList = Translation.ParseTRA_Translation(fileDialog.FileName);
                }
                else if (fileDialog.FileName.Contains(".trs"))
                {
                    entryList = Translation.ParseTRS_Translation(fileDialog.FileName);
                }

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
                this.Text = _currentfilename + " - AGS Translation Editor";
                _documentChanged = false;

                //Enable Buttons
                toolStripButtonStats.Enabled = true;
                saveToolStripMenuItem.Enabled = true;
                saveAsToolStripMenuItem.Enabled = true;
                toolStripButtonOpen.Enabled = true;
                toolStripButtonSave.Enabled = true;
                toolStripButtonFind.Enabled = true;
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
                    DefaultExt = "trs",
                    //saveDialog.AddExtension = true;
                    Filter = "AGS Translation File(*.TRS)|*.trs"
                };
                if (saveDialog.ShowDialog() == DialogResult.OK)
                {
                    SaveFile(saveDialog.FileName);
                    MessageBox.Show($"File was saved as {saveDialog.FileName}.","File saved", MessageBoxButtons.OK);
                }
            }
        }

        private void StatsStripButton_Click(object sender, EventArgs e)
        {
            frmStats StatsWindow = new frmStats();
            int countNotTrans = CountNotTranslated();

            StatsWindow.LoadData(_numEntries, countNotTrans);
            StatsWindow.Show();
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
                    {
                        string tempText = originalText;

                        txtTranslationText.Text = YandexTranslationApi.Translate(Properties.Settings.Default.YandexApiKey, "de", tempText, null, null);
                    }
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
                        Translation.CreateTRA_File(info, saveDialog.FileName,
                         Translation.ParseTRS_Translation(openDialog.FileName));
                }
            }
        }

        private void dataGridView1_CellValueChanged(object sender, DataGridViewCellEventArgs e)
        {
            this.Text = _currentfilename + " • - AGS Translation Editor";
            _documentChanged = true;
        }

        private void frmMain_FormClosed(object sender, FormClosedEventArgs e)
        {
            if (_documentChanged)
            {
                string question = string.Format("Save changes to {0} ?",_currentfilename.Substring(_currentfilename.LastIndexOf('\\') + 1)); 
                //Ask if user wants to save if data was changed
                if (MessageBox.Show(question, "AGS Translation Editor", MessageBoxButtons.YesNo) ==
                    DialogResult.Yes)
                {
                    //Save changes then exit
                    if (dataGridView1.Rows.Count > 0)
                    {
                        SaveFile(_currentfilename);
                    }
                }
            }
        }

        private void aboutToolStripMenuItem_Click(object sender, EventArgs e)
        {
            frmAbout about = new frmAbout();
            about.ShowDialog();
        }

        private void SaveFile(string filename)
        {
            using (FileStream fs = new FileStream(filename, FileMode.Create))
            {
                using (StreamWriter fw = new StreamWriter(fs))
                {
                    foreach (DataGridViewRow row in dataGridView1.Rows)
                    {
                        fw.WriteLine(row.Cells[0].Value);
                        fw.WriteLine(row.Cells[1].Value);
                    }
                }
            }
        }

        private void ExportCSVMenuItem_Click(object sender, EventArgs e)
        {
            //Create CSV File
            if (dataGridView1.Rows.Count > 0)
            {
                SaveFileDialog saveDialog = new SaveFileDialog()
                {
                    DefaultExt = "csv",
                    AddExtension = true
                };
                if (saveDialog.ShowDialog() == DialogResult.OK)
                {
                    using (FileStream fs = new FileStream(saveDialog.FileName, FileMode.Create))
                    {
                        using (StreamWriter sw = new StreamWriter(fs))
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
        }

        private void AddPOHeader(StreamWriter sw)
        {
            sw.WriteLine("msgid \"\"");
            sw.WriteLine("msgstr \"\"");
            sw.WriteLine("\"Project-Id-Version: \\n\"");
            sw.WriteLine("\"POT-Creation-Date: \\n\"");
            sw.WriteLine("\"PO-Revision-Date: \\n\"");
            sw.WriteLine("\"Last-Translator: \\n\"");
            sw.WriteLine("\"Language-Team: \\n\"");
            sw.WriteLine("\"MIME-Version: 1.0\\n\"");
            sw.WriteLine("\"Content-Type: text/plain; UTF-8\\n\"");
            sw.WriteLine("\"Content-Transfer-Encoding: 8bit\\n\"");
            sw.WriteLine("\"Language: de\\n\"");
            sw.WriteLine("\"X-Generator: Poedit 1.7.6\\n\"");
            sw.WriteLine();
        }

        private void ExportPOMenuItem_Click(object sender, EventArgs e)
        {
            //Create PO File
            if (dataGridView1.Rows.Count > 0)
            {
                SaveFileDialog saveDialog = new SaveFileDialog()
                {
                    DefaultExt = "po",
                    AddExtension = true
                };
                if (saveDialog.ShowDialog() == DialogResult.OK)
                {
                    using (FileStream fs = new FileStream(saveDialog.FileName, FileMode.Create))
                    {
                        using (StreamWriter sw = new StreamWriter(fs))
                        {
                            //write standard PO Header
                            AddPOHeader(sw);

                            int i = 0;
                            foreach (DataGridViewRow row in dataGridView1.Rows)
                            {
                                //remove quotes btw. change them to ' because of format issues
                                string msgid = (string)row.Cells[0].Value;
                                msgid = msgid.Replace('\"', '\'');
                                string msgstr = (string)row.Cells[1].Value;
                                msgstr = msgstr.Replace('\"', '\'');

                                sw.WriteLine("msgid \"{0}\"", msgid);
                                sw.WriteLine("msgstr \"{0}\"\n", msgstr);
                                i++;
                            }
                        }
                    }
                }
            }
        }

        private void ImportPOMenuItem_Click(object sender, EventArgs e)
        {
            OpenFileDialog openDialog = new OpenFileDialog()
            {
                Filter = "PO File (*.po)|*.po",
                Title = "Select PO File to import."
            };
            if (openDialog.ShowDialog() == DialogResult.OK)
            {
                Dictionary<string, string> translatedLines = new Dictionary<string, string>();
                List<string> list = new List<string>(File.ReadAllLines(openDialog.FileName));
                //Look for comments and remove them
                list.RemoveAll(str => str.StartsWith("#"));
                //Remove Header
                list.RemoveRange(0, 13);
                //Remove empty lines
                list.RemoveAll(str => String.IsNullOrEmpty(str));

                // Go through all list data
                int length = list.Count();
                for (int i = 0; i < length;)
                {
                    string msgid = list[i];
                    i++;
                    string sTranslationText = "";
                    if (i < length)
                    {
                        sTranslationText = list[i];
                        i++;
                    }

                    if (!translatedLines.ContainsKey(msgid))
                    {
                        translatedLines.Add(msgid, sTranslationText);
                    }
                    else
                    {
                        MessageBox.Show("Entry already in Dictionary!", string.Format("Key already available: {0}", msgid));
                    }
                }
            }
        }

        private void ExtractTextMenuItem_Click(object sender, EventArgs e)
        {
            OpenFileDialog openDialog = new OpenFileDialog()
            {
                Filter = "EXE File (*.exe,*.bin)|*.exe;*.bin",
                Title = "Select Game EXE or bin file"
            };

            if(openDialog.ShowDialog() == DialogResult.OK)
                AGSTools.Extraction.ParseAGSFile(openDialog.FileName);
        }

        private void toolStripButton1_Click(object sender, EventArgs e)
        {
            int countEntries = dataGridView1.RowCount;
            int notTranslated = 0;

            foreach (DataGridViewRow row in dataGridView1.Rows)
            {
                if ((string)row.Cells[1].Value == string.Empty)
                    notTranslated++;
            }

            frmStats frmStats = new frmStats();
            frmStats.LoadData(countEntries, notTranslated);
            frmStats.Show();
        }

        private void toolStripButtonFind_Click(object sender, EventArgs e)
        {
            findEntry(toolStripTextBox1.Text);
        }

        private void toolStripTextBox1_KeyDown(object sender, KeyEventArgs e)
        {
            if (e.KeyCode == Keys.Enter)
            {
                findEntry(toolStripTextBox1.Text);
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
                    {
                        foundEntries.Add(row.Index);
                    }
                }

                if (foundEntries.Count == 0)
                {
                    MessageBox.Show("No Entry found for: " + toolStripTextBox1.Text, "Not Found");
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
    }
}
