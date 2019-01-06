namespace AGS_TranslationEditor
{
    partial class frmMain
    {
        /// <summary>
        /// Erforderliche Designervariable.
        /// </summary>
        private System.ComponentModel.IContainer components = null;

        /// <summary>
        /// Verwendete Ressourcen bereinigen.
        /// </summary>
        /// <param name="disposing">True, wenn verwaltete Ressourcen gelöscht werden sollen; andernfalls False.</param>
        protected override void Dispose(bool disposing)
        {
            if (disposing && (components != null))
            {
                components.Dispose();
            }
            base.Dispose(disposing);
        }

        #region Vom Windows Form-Designer generierter Code

        /// <summary>
        /// Erforderliche Methode für die Designerunterstützung.
        /// Der Inhalt der Methode darf nicht mit dem Code-Editor geändert werden.
        /// </summary>
        private void InitializeComponent()
        {
            System.ComponentModel.ComponentResourceManager resources = new System.ComponentModel.ComponentResourceManager(typeof(frmMain));
            System.Windows.Forms.DataGridViewCellStyle dataGridViewCellStyle1 = new System.Windows.Forms.DataGridViewCellStyle();
            this.menuStrip1 = new System.Windows.Forms.MenuStrip();
            this.dateiToolStripMenuItem = new System.Windows.Forms.ToolStripMenuItem();
            this.openToolStripMenuItem = new System.Windows.Forms.ToolStripMenuItem();
            this.toolStripMenuItem3 = new System.Windows.Forms.ToolStripSeparator();
            this.saveToolStripMenuItem = new System.Windows.Forms.ToolStripMenuItem();
            this.saveAsToolStripMenuItem = new System.Windows.Forms.ToolStripMenuItem();
            this.toolStripMenuItem1 = new System.Windows.Forms.ToolStripSeparator();
            this.beendenToolStripMenuItem = new System.Windows.Forms.ToolStripMenuItem();
            this.toolsToolStripMenuItem = new System.Windows.Forms.ToolStripMenuItem();
            this.CreateTRA_MenuItem = new System.Windows.Forms.ToolStripMenuItem();
            this.ExtractTextMenuItem = new System.Windows.Forms.ToolStripMenuItem();
            this.toolStripMenuItem5 = new System.Windows.Forms.ToolStripSeparator();
            this.ExportMenuItem = new System.Windows.Forms.ToolStripMenuItem();
            this.ExportPOMenuItem = new System.Windows.Forms.ToolStripMenuItem();
            this.ExportCSVMenuItem = new System.Windows.Forms.ToolStripMenuItem();
            this.importToolStripMenuItem = new System.Windows.Forms.ToolStripMenuItem();
            this.ImportPOMenuItem = new System.Windows.Forms.ToolStripMenuItem();
            this.ImportCSVToolStripMenuItem = new System.Windows.Forms.ToolStripMenuItem();
            this.hilfeToolStripMenuItem = new System.Windows.Forms.ToolStripMenuItem();
            this.aboutToolStripMenuItem = new System.Windows.Forms.ToolStripMenuItem();
            this.xMLToolStripMenuItem = new System.Windows.Forms.ToolStripMenuItem();
            this.statusStrip1 = new System.Windows.Forms.StatusStrip();
            this.lblEntriesCount = new System.Windows.Forms.ToolStripStatusLabel();
            this.toolStripProgressBar1 = new System.Windows.Forms.ToolStripProgressBar();
            this.lblSeperator = new System.Windows.Forms.ToolStripStatusLabel();
            this.lblFoundEntries = new System.Windows.Forms.ToolStripStatusLabel();
            this.dgvTranslation = new System.Windows.Forms.DataGridView();
            this.colSource = new System.Windows.Forms.DataGridViewTextBoxColumn();
            this.colTranslation = new System.Windows.Forms.DataGridViewTextBoxColumn();
            this.toolStrip1 = new System.Windows.Forms.ToolStrip();
            this.toolStripButtonOpen = new System.Windows.Forms.ToolStripButton();
            this.toolStripButtonSave = new System.Windows.Forms.ToolStripButton();
            this.toolStripSeparator3 = new System.Windows.Forms.ToolStripSeparator();
            this.toolStripPadding = new System.Windows.Forms.ToolStripLabel();
            this.toolStripButtonSidebar = new System.Windows.Forms.ToolStripButton();
            this.toolStripButtonNext = new System.Windows.Forms.ToolStripButton();
            this.toolStripButtonBack = new System.Windows.Forms.ToolStripButton();
            this.toolStripButtonFind = new System.Windows.Forms.ToolStripButton();
            this.toolStriptxtSearch = new System.Windows.Forms.ToolStripTextBox();
            this.toolStripButtonSettings = new System.Windows.Forms.ToolStripButton();
            this.OpenStripButton = new System.Windows.Forms.ToolStripButton();
            this.txtSourceText = new System.Windows.Forms.RichTextBox();
            this.lblSource = new System.Windows.Forms.Label();
            this.lblTranslation = new System.Windows.Forms.Label();
            this.txtTranslationText = new System.Windows.Forms.RichTextBox();
            this.tableLayoutPanel1 = new System.Windows.Forms.TableLayoutPanel();
            this.flowLayoutPanel1 = new System.Windows.Forms.FlowLayoutPanel();
            this.lblTranslationSuggestion = new System.Windows.Forms.Label();
            this.panel1 = new System.Windows.Forms.Panel();
            this.lblSuggestion = new System.Windows.Forms.Label();
            this.menuStrip1.SuspendLayout();
            this.statusStrip1.SuspendLayout();
            ((System.ComponentModel.ISupportInitialize)(this.dgvTranslation)).BeginInit();
            this.toolStrip1.SuspendLayout();
            this.tableLayoutPanel1.SuspendLayout();
            this.flowLayoutPanel1.SuspendLayout();
            this.panel1.SuspendLayout();
            this.SuspendLayout();
            // 
            // menuStrip1
            // 
            this.menuStrip1.Items.AddRange(new System.Windows.Forms.ToolStripItem[] {
            this.dateiToolStripMenuItem,
            this.toolsToolStripMenuItem,
            this.hilfeToolStripMenuItem});
            resources.ApplyResources(this.menuStrip1, "menuStrip1");
            this.menuStrip1.Name = "menuStrip1";
            // 
            // dateiToolStripMenuItem
            // 
            this.dateiToolStripMenuItem.DropDownItems.AddRange(new System.Windows.Forms.ToolStripItem[] {
            this.openToolStripMenuItem,
            this.toolStripMenuItem3,
            this.saveToolStripMenuItem,
            this.saveAsToolStripMenuItem,
            this.toolStripMenuItem1,
            this.beendenToolStripMenuItem});
            this.dateiToolStripMenuItem.Name = "dateiToolStripMenuItem";
            resources.ApplyResources(this.dateiToolStripMenuItem, "dateiToolStripMenuItem");
            // 
            // openToolStripMenuItem
            // 
            this.openToolStripMenuItem.Image = global::AGS_TranslationEditor.Properties.Resources.document_open;
            this.openToolStripMenuItem.Name = "openToolStripMenuItem";
            resources.ApplyResources(this.openToolStripMenuItem, "openToolStripMenuItem");
            this.openToolStripMenuItem.Click += new System.EventHandler(this.openToolStripMenuItem_Click);
            // 
            // toolStripMenuItem3
            // 
            this.toolStripMenuItem3.Name = "toolStripMenuItem3";
            resources.ApplyResources(this.toolStripMenuItem3, "toolStripMenuItem3");
            // 
            // saveToolStripMenuItem
            // 
            resources.ApplyResources(this.saveToolStripMenuItem, "saveToolStripMenuItem");
            this.saveToolStripMenuItem.Image = global::AGS_TranslationEditor.Properties.Resources.document_save;
            this.saveToolStripMenuItem.Name = "saveToolStripMenuItem";
            this.saveToolStripMenuItem.Click += new System.EventHandler(this.saveToolStripMenuItem_Click);
            // 
            // saveAsToolStripMenuItem
            // 
            resources.ApplyResources(this.saveAsToolStripMenuItem, "saveAsToolStripMenuItem");
            this.saveAsToolStripMenuItem.Image = global::AGS_TranslationEditor.Properties.Resources.document_save;
            this.saveAsToolStripMenuItem.Name = "saveAsToolStripMenuItem";
            this.saveAsToolStripMenuItem.Click += new System.EventHandler(this.saveAsToolStripMenuItem_Click);
            // 
            // toolStripMenuItem1
            // 
            this.toolStripMenuItem1.Name = "toolStripMenuItem1";
            resources.ApplyResources(this.toolStripMenuItem1, "toolStripMenuItem1");
            // 
            // beendenToolStripMenuItem
            // 
            this.beendenToolStripMenuItem.Name = "beendenToolStripMenuItem";
            resources.ApplyResources(this.beendenToolStripMenuItem, "beendenToolStripMenuItem");
            this.beendenToolStripMenuItem.Click += new System.EventHandler(this.beendenToolStripMenuItem_Click);
            // 
            // toolsToolStripMenuItem
            // 
            this.toolsToolStripMenuItem.DropDownItems.AddRange(new System.Windows.Forms.ToolStripItem[] {
            this.CreateTRA_MenuItem,
            this.ExtractTextMenuItem,
            this.toolStripMenuItem5,
            this.ExportMenuItem,
            this.importToolStripMenuItem});
            this.toolsToolStripMenuItem.Name = "toolsToolStripMenuItem";
            resources.ApplyResources(this.toolsToolStripMenuItem, "toolsToolStripMenuItem");
            // 
            // CreateTRA_MenuItem
            // 
            this.CreateTRA_MenuItem.Name = "CreateTRA_MenuItem";
            resources.ApplyResources(this.CreateTRA_MenuItem, "CreateTRA_MenuItem");
            this.CreateTRA_MenuItem.Click += new System.EventHandler(this.CreateTRAMenuItem_Click);
            // 
            // ExtractTextMenuItem
            // 
            this.ExtractTextMenuItem.Name = "ExtractTextMenuItem";
            resources.ApplyResources(this.ExtractTextMenuItem, "ExtractTextMenuItem");
            this.ExtractTextMenuItem.Click += new System.EventHandler(this.ExtractTextMenuItem_Click);
            // 
            // toolStripMenuItem5
            // 
            this.toolStripMenuItem5.Name = "toolStripMenuItem5";
            resources.ApplyResources(this.toolStripMenuItem5, "toolStripMenuItem5");
            // 
            // ExportMenuItem
            // 
            this.ExportMenuItem.DropDownItems.AddRange(new System.Windows.Forms.ToolStripItem[] {
            this.ExportPOMenuItem,
            this.ExportCSVMenuItem});
            resources.ApplyResources(this.ExportMenuItem, "ExportMenuItem");
            this.ExportMenuItem.Name = "ExportMenuItem";
            // 
            // ExportPOMenuItem
            // 
            this.ExportPOMenuItem.Name = "ExportPOMenuItem";
            resources.ApplyResources(this.ExportPOMenuItem, "ExportPOMenuItem");
            this.ExportPOMenuItem.Click += new System.EventHandler(this.ExportPOMenuItem_Click);
            // 
            // ExportCSVMenuItem
            // 
            this.ExportCSVMenuItem.Name = "ExportCSVMenuItem";
            resources.ApplyResources(this.ExportCSVMenuItem, "ExportCSVMenuItem");
            this.ExportCSVMenuItem.Click += new System.EventHandler(this.ExportCSVMenuItem_Click);
            // 
            // importToolStripMenuItem
            // 
            this.importToolStripMenuItem.DropDownItems.AddRange(new System.Windows.Forms.ToolStripItem[] {
            this.ImportPOMenuItem,
            this.ImportCSVToolStripMenuItem});
            this.importToolStripMenuItem.Name = "importToolStripMenuItem";
            resources.ApplyResources(this.importToolStripMenuItem, "importToolStripMenuItem");
            // 
            // ImportPOMenuItem
            // 
            this.ImportPOMenuItem.Name = "ImportPOMenuItem";
            resources.ApplyResources(this.ImportPOMenuItem, "ImportPOMenuItem");
            this.ImportPOMenuItem.Click += new System.EventHandler(this.ImportPOMenuItem_Click);
            // 
            // ImportCSVToolStripMenuItem
            // 
            this.ImportCSVToolStripMenuItem.Name = "ImportCSVToolStripMenuItem";
            resources.ApplyResources(this.ImportCSVToolStripMenuItem, "ImportCSVToolStripMenuItem");
            this.ImportCSVToolStripMenuItem.Click += new System.EventHandler(this.ImportCSVToolStripMenuItem_Click);
            // 
            // hilfeToolStripMenuItem
            // 
            this.hilfeToolStripMenuItem.DropDownItems.AddRange(new System.Windows.Forms.ToolStripItem[] {
            this.aboutToolStripMenuItem});
            this.hilfeToolStripMenuItem.Name = "hilfeToolStripMenuItem";
            resources.ApplyResources(this.hilfeToolStripMenuItem, "hilfeToolStripMenuItem");
            // 
            // aboutToolStripMenuItem
            // 
            this.aboutToolStripMenuItem.Name = "aboutToolStripMenuItem";
            resources.ApplyResources(this.aboutToolStripMenuItem, "aboutToolStripMenuItem");
            this.aboutToolStripMenuItem.Click += new System.EventHandler(this.aboutMenuItem_Click);
            // 
            // xMLToolStripMenuItem
            // 
            this.xMLToolStripMenuItem.Name = "xMLToolStripMenuItem";
            resources.ApplyResources(this.xMLToolStripMenuItem, "xMLToolStripMenuItem");
            // 
            // statusStrip1
            // 
            this.statusStrip1.Items.AddRange(new System.Windows.Forms.ToolStripItem[] {
            this.lblEntriesCount,
            this.toolStripProgressBar1,
            this.lblSeperator,
            this.lblFoundEntries});
            resources.ApplyResources(this.statusStrip1, "statusStrip1");
            this.statusStrip1.Name = "statusStrip1";
            // 
            // lblEntriesCount
            // 
            resources.ApplyResources(this.lblEntriesCount, "lblEntriesCount");
            this.lblEntriesCount.Name = "lblEntriesCount";
            // 
            // toolStripProgressBar1
            // 
            this.toolStripProgressBar1.Name = "toolStripProgressBar1";
            resources.ApplyResources(this.toolStripProgressBar1, "toolStripProgressBar1");
            // 
            // lblSeperator
            // 
            this.lblSeperator.Name = "lblSeperator";
            resources.ApplyResources(this.lblSeperator, "lblSeperator");
            this.lblSeperator.Spring = true;
            // 
            // lblFoundEntries
            // 
            resources.ApplyResources(this.lblFoundEntries, "lblFoundEntries");
            this.lblFoundEntries.Name = "lblFoundEntries";
            // 
            // dgvTranslation
            // 
            this.dgvTranslation.AllowUserToResizeRows = false;
            dataGridViewCellStyle1.BackColor = System.Drawing.Color.LightGray;
            this.dgvTranslation.AlternatingRowsDefaultCellStyle = dataGridViewCellStyle1;
            this.dgvTranslation.BorderStyle = System.Windows.Forms.BorderStyle.Fixed3D;
            this.dgvTranslation.ColumnHeadersHeightSizeMode = System.Windows.Forms.DataGridViewColumnHeadersHeightSizeMode.AutoSize;
            this.dgvTranslation.Columns.AddRange(new System.Windows.Forms.DataGridViewColumn[] {
            this.colSource,
            this.colTranslation});
            resources.ApplyResources(this.dgvTranslation, "dgvTranslation");
            this.dgvTranslation.MultiSelect = false;
            this.dgvTranslation.Name = "dgvTranslation";
            this.dgvTranslation.ReadOnly = true;
            this.dgvTranslation.RowHeadersVisible = false;
            this.dgvTranslation.RowHeadersWidthSizeMode = System.Windows.Forms.DataGridViewRowHeadersWidthSizeMode.DisableResizing;
            this.dgvTranslation.RowTemplate.Resizable = System.Windows.Forms.DataGridViewTriState.True;
            this.dgvTranslation.SelectionMode = System.Windows.Forms.DataGridViewSelectionMode.FullRowSelect;
            this.dgvTranslation.ShowEditingIcon = false;
            this.dgvTranslation.ShowRowErrors = false;
            this.dgvTranslation.CellValueChanged += new System.Windows.Forms.DataGridViewCellEventHandler(this.dgvTranslation_CellValueChanged);
            this.dgvTranslation.SelectionChanged += new System.EventHandler(this.dgvTranslation_SelectionChanged);
            // 
            // colSource
            // 
            this.colSource.AutoSizeMode = System.Windows.Forms.DataGridViewAutoSizeColumnMode.Fill;
            this.colSource.DataPropertyName = "colSource";
            resources.ApplyResources(this.colSource, "colSource");
            this.colSource.Name = "colSource";
            this.colSource.ReadOnly = true;
            this.colSource.Resizable = System.Windows.Forms.DataGridViewTriState.False;
            // 
            // colTranslation
            // 
            this.colTranslation.AutoSizeMode = System.Windows.Forms.DataGridViewAutoSizeColumnMode.Fill;
            this.colTranslation.DataPropertyName = "colTranslation";
            resources.ApplyResources(this.colTranslation, "colTranslation");
            this.colTranslation.Name = "colTranslation";
            this.colTranslation.ReadOnly = true;
            this.colTranslation.Resizable = System.Windows.Forms.DataGridViewTriState.False;
            // 
            // toolStrip1
            // 
            resources.ApplyResources(this.toolStrip1, "toolStrip1");
            this.toolStrip1.Items.AddRange(new System.Windows.Forms.ToolStripItem[] {
            this.toolStripButtonOpen,
            this.toolStripButtonSave,
            this.toolStripSeparator3,
            this.toolStripPadding,
            this.toolStripButtonSidebar,
            this.toolStripButtonNext,
            this.toolStripButtonBack,
            this.toolStripButtonFind,
            this.toolStriptxtSearch,
            this.toolStripButtonSettings});
            this.toolStrip1.Name = "toolStrip1";
            // 
            // toolStripButtonOpen
            // 
            resources.ApplyResources(this.toolStripButtonOpen, "toolStripButtonOpen");
            this.toolStripButtonOpen.DisplayStyle = System.Windows.Forms.ToolStripItemDisplayStyle.Image;
            this.toolStripButtonOpen.Image = global::AGS_TranslationEditor.Properties.Resources.document_open;
            this.toolStripButtonOpen.Name = "toolStripButtonOpen";
            this.toolStripButtonOpen.Click += new System.EventHandler(this.openToolStripMenuItem_Click);
            // 
            // toolStripButtonSave
            // 
            resources.ApplyResources(this.toolStripButtonSave, "toolStripButtonSave");
            this.toolStripButtonSave.DisplayStyle = System.Windows.Forms.ToolStripItemDisplayStyle.Image;
            this.toolStripButtonSave.Image = global::AGS_TranslationEditor.Properties.Resources.document_save;
            this.toolStripButtonSave.Name = "toolStripButtonSave";
            this.toolStripButtonSave.Click += new System.EventHandler(this.saveToolStripMenuItem_Click);
            // 
            // toolStripSeparator3
            // 
            this.toolStripSeparator3.Name = "toolStripSeparator3";
            resources.ApplyResources(this.toolStripSeparator3, "toolStripSeparator3");
            // 
            // toolStripPadding
            // 
            this.toolStripPadding.Alignment = System.Windows.Forms.ToolStripItemAlignment.Right;
            resources.ApplyResources(this.toolStripPadding, "toolStripPadding");
            this.toolStripPadding.Name = "toolStripPadding";
            // 
            // toolStripButtonSidebar
            // 
            this.toolStripButtonSidebar.Alignment = System.Windows.Forms.ToolStripItemAlignment.Right;
            this.toolStripButtonSidebar.DisplayStyle = System.Windows.Forms.ToolStripItemDisplayStyle.Image;
            this.toolStripButtonSidebar.Image = global::AGS_TranslationEditor.Properties.Resources.RightColumnOfTwoColumns_16x;
            resources.ApplyResources(this.toolStripButtonSidebar, "toolStripButtonSidebar");
            this.toolStripButtonSidebar.Name = "toolStripButtonSidebar";
            this.toolStripButtonSidebar.Click += new System.EventHandler(this.toolStripButton1_Click);
            // 
            // toolStripButtonNext
            // 
            this.toolStripButtonNext.Alignment = System.Windows.Forms.ToolStripItemAlignment.Right;
            resources.ApplyResources(this.toolStripButtonNext, "toolStripButtonNext");
            this.toolStripButtonNext.DisplayStyle = System.Windows.Forms.ToolStripItemDisplayStyle.Image;
            this.toolStripButtonNext.Image = global::AGS_TranslationEditor.Properties.Resources.Next_grey_16x;
            this.toolStripButtonNext.Name = "toolStripButtonNext";
            this.toolStripButtonNext.Click += new System.EventHandler(this.toolStripButtonNext_Click);
            // 
            // toolStripButtonBack
            // 
            this.toolStripButtonBack.Alignment = System.Windows.Forms.ToolStripItemAlignment.Right;
            resources.ApplyResources(this.toolStripButtonBack, "toolStripButtonBack");
            this.toolStripButtonBack.DisplayStyle = System.Windows.Forms.ToolStripItemDisplayStyle.Image;
            this.toolStripButtonBack.Image = global::AGS_TranslationEditor.Properties.Resources.Previous_grey_16x;
            this.toolStripButtonBack.Name = "toolStripButtonBack";
            this.toolStripButtonBack.Click += new System.EventHandler(this.toolStripButtonBack_Click);
            // 
            // toolStripButtonFind
            // 
            this.toolStripButtonFind.Alignment = System.Windows.Forms.ToolStripItemAlignment.Right;
            resources.ApplyResources(this.toolStripButtonFind, "toolStripButtonFind");
            this.toolStripButtonFind.DisplayStyle = System.Windows.Forms.ToolStripItemDisplayStyle.Image;
            this.toolStripButtonFind.Image = global::AGS_TranslationEditor.Properties.Resources.Search_16x;
            this.toolStripButtonFind.Name = "toolStripButtonFind";
            this.toolStripButtonFind.Click += new System.EventHandler(this.toolStripButtonFind_Click);
            // 
            // toolStriptxtSearch
            // 
            this.toolStriptxtSearch.Alignment = System.Windows.Forms.ToolStripItemAlignment.Right;
            resources.ApplyResources(this.toolStriptxtSearch, "toolStriptxtSearch");
            this.toolStriptxtSearch.BorderStyle = System.Windows.Forms.BorderStyle.FixedSingle;
            this.toolStriptxtSearch.Name = "toolStriptxtSearch";
            this.toolStriptxtSearch.KeyDown += new System.Windows.Forms.KeyEventHandler(this.toolStriptxtSearch_KeyDown);
            // 
            // toolStripButtonSettings
            // 
            resources.ApplyResources(this.toolStripButtonSettings, "toolStripButtonSettings");
            this.toolStripButtonSettings.DisplayStyle = System.Windows.Forms.ToolStripItemDisplayStyle.Image;
            this.toolStripButtonSettings.Image = global::AGS_TranslationEditor.Properties.Resources.Settings_16x;
            this.toolStripButtonSettings.Name = "toolStripButtonSettings";
            this.toolStripButtonSettings.Click += new System.EventHandler(this.toolStripButtonSettings_Click);
            // 
            // OpenStripButton
            // 
            this.OpenStripButton.Image = global::AGS_TranslationEditor.Properties.Resources.document_open;
            resources.ApplyResources(this.OpenStripButton, "OpenStripButton");
            this.OpenStripButton.Name = "OpenStripButton";
            this.OpenStripButton.Click += new System.EventHandler(this.openToolStripMenuItem_Click);
            // 
            // txtSourceText
            // 
            resources.ApplyResources(this.txtSourceText, "txtSourceText");
            this.txtSourceText.Name = "txtSourceText";
            this.txtSourceText.ReadOnly = true;
            this.txtSourceText.TabStop = false;
            // 
            // lblSource
            // 
            resources.ApplyResources(this.lblSource, "lblSource");
            this.lblSource.Name = "lblSource";
            // 
            // lblTranslation
            // 
            resources.ApplyResources(this.lblTranslation, "lblTranslation");
            this.lblTranslation.Name = "lblTranslation";
            // 
            // txtTranslationText
            // 
            resources.ApplyResources(this.txtTranslationText, "txtTranslationText");
            this.txtTranslationText.Name = "txtTranslationText";
            this.txtTranslationText.KeyDown += new System.Windows.Forms.KeyEventHandler(this.txtTranslationText_KeyDown);
            // 
            // tableLayoutPanel1
            // 
            resources.ApplyResources(this.tableLayoutPanel1, "tableLayoutPanel1");
            this.tableLayoutPanel1.Controls.Add(this.lblSource, 0, 1);
            this.tableLayoutPanel1.Controls.Add(this.txtTranslationText, 0, 4);
            this.tableLayoutPanel1.Controls.Add(this.lblTranslation, 0, 3);
            this.tableLayoutPanel1.Controls.Add(this.txtSourceText, 0, 2);
            this.tableLayoutPanel1.Controls.Add(this.dgvTranslation, 0, 0);
            this.tableLayoutPanel1.Controls.Add(this.flowLayoutPanel1, 1, 0);
            this.tableLayoutPanel1.Name = "tableLayoutPanel1";
            // 
            // flowLayoutPanel1
            // 
            this.flowLayoutPanel1.Controls.Add(this.lblTranslationSuggestion);
            this.flowLayoutPanel1.Controls.Add(this.panel1);
            resources.ApplyResources(this.flowLayoutPanel1, "flowLayoutPanel1");
            this.flowLayoutPanel1.Name = "flowLayoutPanel1";
            // 
            // lblTranslationSuggestion
            // 
            resources.ApplyResources(this.lblTranslationSuggestion, "lblTranslationSuggestion");
            this.lblTranslationSuggestion.Name = "lblTranslationSuggestion";
            // 
            // panel1
            // 
            this.panel1.Controls.Add(this.lblSuggestion);
            resources.ApplyResources(this.panel1, "panel1");
            this.panel1.Name = "panel1";
            // 
            // lblSuggestion
            // 
            resources.ApplyResources(this.lblSuggestion, "lblSuggestion");
            this.lblSuggestion.Name = "lblSuggestion";
            this.lblSuggestion.UseMnemonic = false;
            this.lblSuggestion.Click += new System.EventHandler(this.lblSuggestion_Click);
            // 
            // frmMain
            // 
            resources.ApplyResources(this, "$this");
            this.AutoScaleMode = System.Windows.Forms.AutoScaleMode.Font;
            this.Controls.Add(this.tableLayoutPanel1);
            this.Controls.Add(this.toolStrip1);
            this.Controls.Add(this.statusStrip1);
            this.Controls.Add(this.menuStrip1);
            this.MainMenuStrip = this.menuStrip1;
            this.Name = "frmMain";
            this.FormClosed += new System.Windows.Forms.FormClosedEventHandler(this.frmMain_FormClosed);
            this.Load += new System.EventHandler(this.frmMain_Load);
            this.menuStrip1.ResumeLayout(false);
            this.menuStrip1.PerformLayout();
            this.statusStrip1.ResumeLayout(false);
            this.statusStrip1.PerformLayout();
            ((System.ComponentModel.ISupportInitialize)(this.dgvTranslation)).EndInit();
            this.toolStrip1.ResumeLayout(false);
            this.toolStrip1.PerformLayout();
            this.tableLayoutPanel1.ResumeLayout(false);
            this.tableLayoutPanel1.PerformLayout();
            this.flowLayoutPanel1.ResumeLayout(false);
            this.panel1.ResumeLayout(false);
            this.ResumeLayout(false);
            this.PerformLayout();

        }

        #endregion

        private System.Windows.Forms.MenuStrip menuStrip1;
        private System.Windows.Forms.ToolStripMenuItem dateiToolStripMenuItem;
        private System.Windows.Forms.ToolStripMenuItem openToolStripMenuItem;
        private System.Windows.Forms.ToolStripSeparator toolStripMenuItem1;
        private System.Windows.Forms.ToolStripMenuItem beendenToolStripMenuItem;
        private System.Windows.Forms.ToolStripMenuItem hilfeToolStripMenuItem;
        private System.Windows.Forms.StatusStrip statusStrip1;
        private System.Windows.Forms.DataGridView dgvTranslation;
        private System.Windows.Forms.ToolStrip toolStrip1;
        private System.Windows.Forms.ToolStripButton OpenStripButton;
        private System.Windows.Forms.RichTextBox txtSourceText;
        private System.Windows.Forms.Label lblSource;
        private System.Windows.Forms.Label lblTranslation;
        private System.Windows.Forms.RichTextBox txtTranslationText;
        private System.Windows.Forms.ToolStripStatusLabel lblEntriesCount;
        private System.Windows.Forms.ToolStripSeparator toolStripMenuItem3;
        private System.Windows.Forms.ToolStripMenuItem saveToolStripMenuItem;
        private System.Windows.Forms.ToolStripMenuItem saveAsToolStripMenuItem;
        private System.Windows.Forms.ToolStripMenuItem aboutToolStripMenuItem;
        private System.Windows.Forms.ToolStripMenuItem toolsToolStripMenuItem;
        private System.Windows.Forms.ToolStripMenuItem CreateTRA_MenuItem;
        private System.Windows.Forms.ToolStripMenuItem ExportMenuItem;
        private System.Windows.Forms.ToolStripMenuItem xMLToolStripMenuItem;
        private System.Windows.Forms.ToolStripMenuItem importToolStripMenuItem;
        private System.Windows.Forms.ToolStripSeparator toolStripMenuItem5;
        private System.Windows.Forms.ToolStripMenuItem ExtractTextMenuItem;
        private System.Windows.Forms.ToolStripButton toolStripButtonOpen;
        private System.Windows.Forms.ToolStripButton toolStripButtonSave;
        private System.Windows.Forms.ToolStripSeparator toolStripSeparator3;
        private System.Windows.Forms.ToolStripTextBox toolStriptxtSearch;
        private System.Windows.Forms.ToolStripButton toolStripButtonFind;
        private System.Windows.Forms.ToolStripMenuItem ExportPOMenuItem;
        private System.Windows.Forms.ToolStripMenuItem ExportCSVMenuItem;
        private System.Windows.Forms.ToolStripMenuItem ImportPOMenuItem;
        private System.Windows.Forms.ToolStripButton toolStripButtonBack;
        private System.Windows.Forms.ToolStripButton toolStripButtonNext;
        private System.Windows.Forms.ToolStripStatusLabel lblSeperator;
        private System.Windows.Forms.ToolStripStatusLabel lblFoundEntries;
        private System.Windows.Forms.ToolStripButton toolStripButtonSettings;
        private System.Windows.Forms.ToolStripLabel toolStripPadding;
        private System.Windows.Forms.ToolStripMenuItem ImportCSVToolStripMenuItem;
        private System.Windows.Forms.DataGridViewTextBoxColumn colSource;
        private System.Windows.Forms.DataGridViewTextBoxColumn colTranslation;
        private System.Windows.Forms.TableLayoutPanel tableLayoutPanel1;
        private System.Windows.Forms.ToolStripButton toolStripButtonSidebar;
        private System.Windows.Forms.FlowLayoutPanel flowLayoutPanel1;
        private System.Windows.Forms.Label lblTranslationSuggestion;
        private System.Windows.Forms.Panel panel1;
        private System.Windows.Forms.Label lblSuggestion;
        private System.Windows.Forms.ToolStripProgressBar toolStripProgressBar1;
    }
}

