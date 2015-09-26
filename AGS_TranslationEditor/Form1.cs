﻿/*
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
using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Data;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Windows.Forms;
using System.Xml;
using AGS_TranslationEditor.Properties;
using static System.Windows.Forms.Application;

namespace AGS_TranslationEditor
{
    public partial class frmMain : Form
    {
        private int _selectedRow = 0;
        private string _currentfilename = "";
        private int _numEntries = 0;
        private bool _documentChanged = false;

        public frmMain()
        {
            InitializeComponent();
        }

        private void öffnenToolStripMenuItem_Click(object sender, EventArgs e)
        {
            OpenFileDialog fileDialog = new OpenFileDialog();
            fileDialog.Filter = "AGS Translation File(*.TRA,*.TRS)|*.tra;*.trs";

            if (fileDialog.ShowDialog() == DialogResult.OK)
            {
                //Enable Buttons
                SaveStripButton.Enabled = true;
                StatsStripButton.Enabled = true;
                saveToolStripMenuItem.Enabled = true;
                saveAsToolStripMenuItem.Enabled = true;
                addToolStripMenuItem.Enabled = true;
                removeToolStripMenuItem.Enabled = true;

                //Clear the DataGrid
                dataGridView1.Rows.Clear();
                dataGridView1.Refresh();

                _numEntries = 0;
                Dictionary<string, string> entryList = null;
                _currentfilename = fileDialog.FileName;

                if (fileDialog.FileName.Contains(".tra"))
                {
                    entryList = AGS_Translation.ParseTRA_Translation(fileDialog.FileName);
                }
                else if (fileDialog.FileName.Contains(".trs"))
                {
                    entryList = AGS_Translation.ParseTRS_Translation(fileDialog.FileName);
                }

                if (entryList != null)
                {
                    foreach (KeyValuePair<string, string> pair in entryList)
                    {
                        //Populate DataGridView
                        string[] newRow = {pair.Key, pair.Value};
                        dataGridView1.Rows.Add(newRow);
                        _numEntries++;
                    }
                }

                lblFileStatus.Text = "File loaded";
                lblEntriesCount.Text = "Entries: " + _numEntries;

                //Set Form text to filename
                this.Text = _currentfilename + " - AGS Translation Editor";
                _documentChanged = false;
            }
        }

        private void beendenToolStripMenuItem_Click(object sender, EventArgs e)
        {
            if (_documentChanged)
            {
                string question = "Save changes to " +
                                  _currentfilename.Substring(_currentfilename.LastIndexOf("\\") + 1);

                //Ask if user wants to save if data was changed
                if (MessageBox.Show(question, "AGS Translation Editor", MessageBoxButtons.YesNo) ==
                    DialogResult.Yes)
                {
                    //Save changes then exit
                    if (dataGridView1.Rows.Count > 0)
                    {
                        SaveFile(_currentfilename);
                    }
                    Exit();
                }
                else
                {
                    Exit();
                }
            }
            Exit();
        }

        private void saveToolStripMenuItem_Click(object sender, EventArgs e)
        {
            if (dataGridView1.Rows.Count > 0)
            {
                SaveFile(_currentfilename);

                lblFileStatus.Text = Resources.frmMain_saveToolStripMenuItem_Click_File_saved;
                MessageBox.Show(string.Format("File was saved as {0}.", _currentfilename), "File saved",
                    MessageBoxButtons.OK);
            }
        }

        private void richTextBox2_KeyDown(object sender, KeyEventArgs e)
        {
            if (e.KeyCode == Keys.Enter)
            {
                string newText = richTextBox2.Text;
                dataGridView1.Rows[_selectedRow].Cells[1].Value = newText;
                dataGridView1.Focus();
            }
        }

        private void saveAsToolStripMenuItem_Click(object sender, EventArgs e)
        {
            if (dataGridView1.Rows.Count > 0)
            {
                SaveFileDialog saveDialog = new SaveFileDialog();
                saveDialog.DefaultExt = "trs";
                //saveDialog.AddExtension = true;
                saveDialog.Filter = "AGS Translation File(*.TRS)|*.trs";

                if (saveDialog.ShowDialog() == DialogResult.OK)
                {
                    SaveFile(saveDialog.FileName);

                    MessageBox.Show(
                        $"File was saved as {saveDialog.FileName}.",
                        "File saved",
                        MessageBoxButtons.OK);
                }
            }
        }

        private int CountNotTranslated()
        {
            int translatedCount = 0;
            foreach (DataGridViewRow row in dataGridView1.Rows)
            {
                string value = (string) row.Cells[1].Value;

                if (string.Equals(value, ""))
                    translatedCount++;
            }
            return translatedCount;
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

            addToolStripMenuItem.Enabled = false;
            removeToolStripMenuItem.Enabled = false;

            SaveStripButton.Enabled = false;
            StatsStripButton.Enabled = false;

        }

        private void dataGridView1_SelectionChanged(object sender, EventArgs e)
        {
            try
            {
                _selectedRow = dataGridView1.SelectedRows[0].Index;

                string test = (string) dataGridView1.Rows[_selectedRow].Cells[0].Value;
                richTextBox1.Text = test;

                string test2 = (string) dataGridView1.Rows[_selectedRow].Cells[1].Value;
                /*if(test2.Length <= 0)
                {
                    string tempText = test.Substring(test.IndexOf(" "));

                    richTextBox2.Text = YandexTranslationApi.translate(null, "de", tempText, null, null);
                }
                else*/
                richTextBox2.Text = test2;
            }
            catch (Exception)
            {

            }
        }

        private void addToolStripMenuItem_Click(object sender, EventArgs e)
        {
            dataGridView1.Rows.Add();
            dataGridView1.Rows[dataGridView1.RowCount - 1].Selected = true;
            dataGridView1.FirstDisplayedScrollingRowIndex = dataGridView1.RowCount - 1;
        }

        private void removeToolStripMenuItem_Click(object sender, EventArgs e)
        {
            dataGridView1.Rows.RemoveAt(_selectedRow);
        }

        private void getGameInfoToolStripMenuItem_Click(object sender, EventArgs e)
        {
            OpenFileDialog openDialog = new OpenFileDialog();
            openDialog.Filter = "Game EXE File (*.exe)|*.exe";

            if (openDialog.ShowDialog() == DialogResult.OK)
            {
                AGS_Translation.Gameinfo gameinfo = new AGS_Translation.Gameinfo();

                gameinfo = AGS_Translation.GetGameInfo(openDialog.FileName);
                MessageBox.Show(
                    "AGS Version: " + gameinfo.Version +
                    "\nGame Title: " + gameinfo.GameTitle +
                    "\nGameUID: " + gameinfo.GameUID,
                    "Game Information");
            }
        }

        private void createTRAToolStripMenuItem_Click(object sender, EventArgs e)
        {
            OpenFileDialog openExeDialog = new OpenFileDialog();
            openExeDialog.Title = "Game EXE for Translation";
            openExeDialog.Filter = "AGS EXE File (*.exe)|*.exe";

            OpenFileDialog openDialog = new OpenFileDialog();
            openDialog.Filter = "TRS Translation File (*.trs)|*.trs";
            openDialog.Title = "Open TRS Translation you want to use.";

            SaveFileDialog saveDialog = new SaveFileDialog();
            saveDialog.DefaultExt = "tra";
            saveDialog.Filter = "TRA Translation File (*.tra)|*.tra";
            saveDialog.Title = "Save TRA Translation as...";

            if (openExeDialog.ShowDialog() == DialogResult.OK)
            {
                AGS_Translation.Gameinfo info = AGS_Translation.GetGameInfo(openExeDialog.FileName);

                if (openDialog.ShowDialog() == DialogResult.OK)
                {
                    if (saveDialog.ShowDialog() == DialogResult.OK)
                        AGS_Translation.CreateTRA_File(info, saveDialog.FileName,
                            AGS_Translation.ParseTRS_Translation(openDialog.FileName));
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
                string question = "Save changes to " +
                                  _currentfilename.Substring(_currentfilename.LastIndexOf("\\") + 1);
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

        private void neuToolStripMenuItem_Click(object sender, EventArgs e)
        {

        }


        private void SaveFile(string filename)
        {
            FileStream fs = new FileStream(filename, FileMode.Create);
            StreamWriter fw = new StreamWriter(fs);

            foreach (DataGridViewRow row in dataGridView1.Rows)
            {
                fw.WriteLine(row.Cells[0].Value);
                fw.WriteLine(row.Cells[1].Value);
            }
            fw.Close();
            fs.Close();
        }

        private void toolStripButton1_Click(object sender, EventArgs e)
        {


        }

        private void xMLToolStripMenuItem_Click(object sender, EventArgs e)
        {
            //Save changes then exit
            if (dataGridView1.Rows.Count > 0)
            {
                SaveFileDialog saveDialog = new SaveFileDialog();
                saveDialog.DefaultExt = "xml";

                if (saveDialog.ShowDialog() == DialogResult.OK)
                {
                    int counter = 0;

                    XmlWriterSettings settings = new XmlWriterSettings();
                    settings.Encoding = Encoding.ASCII;
                    settings.Indent = true;

                    XmlWriter writer = XmlWriter.Create(saveDialog.FileName, settings);

                    writer.WriteStartDocument();
                    writer.WriteStartElement("AGSTranslationScript");

                    foreach (DataGridViewRow row in dataGridView1.Rows)
                    {
                        writer.WriteStartElement("Entry" + counter);
                        writer.WriteElementString("SourceText", (string) row.Cells[0].Value);
                        writer.WriteElementString("TranslatedText", (string) row.Cells[1].Value);
                        writer.WriteEndElement();
                        counter++;
                    }

                    writer.WriteEndElement();
                    writer.WriteEndDocument();
                    writer.Close();

                }
            }
        }

        private void cSVToolStripMenuItem_Click(object sender, EventArgs e)
        {
            if (dataGridView1.Rows.Count > 0)
            {
                SaveFileDialog saveDialog = new SaveFileDialog();
                saveDialog.DefaultExt = "csv";
                saveDialog.AddExtension = true;

                if (saveDialog.ShowDialog() == DialogResult.OK)
                {
                    FileStream fs = new FileStream(saveDialog.FileName,FileMode.Create);
                    StreamWriter sw = new StreamWriter(fs);
                    foreach (DataGridViewRow row in dataGridView1.Rows)
                    {
                        //remove quotes btw. change them to ' because of format issues
                        string msgid = (string)row.Cells[0].Value;
                        msgid = msgid.Replace('\"', '\'');
                        string msgstr = (string)row.Cells[1].Value;
                        msgstr = msgstr.Replace('\"', '\'');

                        sw.Write("\"{0}\",\"{1}\"\n", msgid, msgstr);
                    }


                    sw.Close();
                    fs.Close();
                }
            }
        }

        private void pOToolStripMenuItem_Click(object sender, EventArgs e)
        {
            if (dataGridView1.Rows.Count > 0)
            {
                SaveFileDialog saveDialog = new SaveFileDialog();
                saveDialog.DefaultExt = "po";
                saveDialog.AddExtension = true;

                if (saveDialog.ShowDialog() == DialogResult.OK)
                {
                    FileStream fs = new FileStream(saveDialog.FileName, FileMode.Create);
                    StreamWriter sw = new StreamWriter(fs);
                    //Standard PO Header
                    AddPOHeader(sw);

                    foreach (DataGridViewRow row in dataGridView1.Rows)
                    {
                        //remove quotes btw. change them to ' because of format issues
                        string msgid = (string)row.Cells[0].Value;
                        msgid = msgid.Replace('\"', '\'');
                        string msgstr = (string)row.Cells[1].Value;
                        msgstr = msgstr.Replace('\"', '\'');

                        sw.Write("msgid \"{0}\"\nmsgstr \"{1}\"\n", msgid, msgstr);
                    }


                    sw.Close();
                    fs.Close();
                }
            }
        }

        void AddPOHeader(StreamWriter sw)
        {
            sw.WriteLine("msgid \"\"");
            sw.WriteLine("msgstr \"\"");
            sw.WriteLine("\"Project-Id-Version: \\n\"");
            sw.WriteLine("\"POT-Creation-Date: \\n\"");
            sw.WriteLine("\"PO-Revision-Date: \\n\"");
            sw.WriteLine("\"Last-Translator: \\n\"");
            sw.WriteLine("\"Language-Team: \\n\"");
            sw.WriteLine("\"MIME-Version: 1.0\\n\"");
            sw.WriteLine("\"Content-Type: text/plain; charset=iso-8859-1\\n\"");
            sw.WriteLine("\"Content-Transfer-Encoding: 8bit\\n\"");
            sw.WriteLine("\"Language: de\\n\"");
            sw.WriteLine("\"X-Generator: Poedit 1.7.6\\n\"");
            sw.WriteLine();
        }
    }
}
