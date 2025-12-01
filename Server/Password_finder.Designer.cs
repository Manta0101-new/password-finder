
namespace PasswordFileFinderGUI
{
    partial class Password_finder
    {
        /// <summary>
        ///  Required designer variable.
        /// </summary>
        private System.ComponentModel.IContainer components = null;

        /// <summary>
        ///  Clean up any resources being used.
        /// </summary>
        /// <param name="disposing">true if managed resources should be disposed; otherwise, false.</param>
        protected override void Dispose(bool disposing)
        {
            if (disposing && (components != null))
            {
                components.Dispose();
            }
            base.Dispose(disposing);
        }

        #region Windows Form Designer generated code

        /// <summary>
        ///  Required method for Designer support - do not modify
        ///  the contents of this method with the code editor.
        /// </summary>
        private void InitializeComponent()
        {
            chkDefaultExtensions = new CheckBox();
            label1 = new Label();
            txtUserExtensions = new TextBox();
            label2 = new Label();
            txtDirectories = new TextBox();
            btnSearch = new Button();
            label3 = new Label();
            txtResults = new TextBox();
            btnSaveResults_Click = new Button();
            menuStrip1 = new MenuStrip();
            fileToolStripMenuItem = new ToolStripMenuItem();
            exitToolStripMenuItem = new ToolStripMenuItem();
            txtContentKeywords = new TextBox();
            label4 = new Label();
            btnStopSearch = new Button();
            remoteToolStripMenuItem = new ToolStripMenuItem();
            menuStrip1.SuspendLayout();
            SuspendLayout();
            // 
            // chkDefaultExtensions
            // 
            chkDefaultExtensions.AutoSize = true;
            chkDefaultExtensions.Location = new Point(3, 57);
            chkDefaultExtensions.Name = "chkDefaultExtensions";
            chkDefaultExtensions.Size = new Size(217, 19);
            chkDefaultExtensions.TabIndex = 0;
            chkDefaultExtensions.Text = "Include Default Password Extensions";
            chkDefaultExtensions.UseVisualStyleBackColor = true;
            // 
            // label1
            // 
            label1.AutoSize = true;
            label1.Location = new Point(6, 84);
            label1.Name = "label1";
            label1.Size = new Size(231, 15);
            label1.TabIndex = 1;
            label1.Text = "Additional Extensions (comma-separated):";
            // 
            // txtUserExtensions
            // 
            txtUserExtensions.Location = new Point(9, 108);
            txtUserExtensions.Name = "txtUserExtensions";
            txtUserExtensions.Size = new Size(295, 23);
            txtUserExtensions.TabIndex = 2;
            // 
            // label2
            // 
            label2.AutoSize = true;
            label2.Location = new Point(6, 196);
            label2.Name = "label2";
            label2.Size = new Size(226, 15);
            label2.TabIndex = 3;
            label2.Text = "Directories to Search (comma-separated):";
            // 
            // txtDirectories
            // 
            txtDirectories.Location = new Point(9, 214);
            txtDirectories.Name = "txtDirectories";
            txtDirectories.Size = new Size(363, 23);
            txtDirectories.TabIndex = 4;
            // 
            // btnSearch
            // 
            btnSearch.Location = new Point(12, 258);
            btnSearch.Name = "btnSearch";
            btnSearch.Size = new Size(112, 23);
            btnSearch.TabIndex = 5;
            btnSearch.Text = "Start Search";
            btnSearch.UseVisualStyleBackColor = true;
            btnSearch.Click += btnSearch_Click;
            // 
            // label3
            // 
            label3.AutoSize = true;
            label3.Location = new Point(313, 24);
            label3.Name = "label3";
            label3.Size = new Size(47, 15);
            label3.TabIndex = 6;
            label3.Text = "Results:";
            // 
            // txtResults
            // 
            txtResults.Anchor = AnchorStyles.Top | AnchorStyles.Bottom | AnchorStyles.Left | AnchorStyles.Right;
            txtResults.Location = new Point(375, 24);
            txtResults.Multiline = true;
            txtResults.Name = "txtResults";
            txtResults.ReadOnly = true;
            txtResults.ScrollBars = ScrollBars.Both;
            txtResults.Size = new Size(470, 405);
            txtResults.TabIndex = 7;
            // 
            // btnSaveResults_Click
            // 
            btnSaveResults_Click.Location = new Point(201, 345);
            btnSaveResults_Click.Name = "btnSaveResults_Click";
            btnSaveResults_Click.Size = new Size(159, 23);
            btnSaveResults_Click.TabIndex = 8;
            btnSaveResults_Click.Text = "Save Results to File";
            btnSaveResults_Click.UseVisualStyleBackColor = true;
            btnSaveResults_Click.Click += btnSaveResults_Click_Click;
            // 
            // menuStrip1
            // 
            menuStrip1.Items.AddRange(new ToolStripItem[] { fileToolStripMenuItem, remoteToolStripMenuItem });
            menuStrip1.Location = new Point(0, 0);
            menuStrip1.Name = "menuStrip1";
            menuStrip1.Size = new Size(879, 24);
            menuStrip1.TabIndex = 9;
            menuStrip1.Text = "menuStrip1";
            // 
            // fileToolStripMenuItem
            // 
            fileToolStripMenuItem.DropDownItems.AddRange(new ToolStripItem[] { exitToolStripMenuItem });
            fileToolStripMenuItem.Name = "fileToolStripMenuItem";
            fileToolStripMenuItem.Size = new Size(37, 20);
            fileToolStripMenuItem.Text = "&File";
            // 
            // exitToolStripMenuItem
            // 
            exitToolStripMenuItem.Name = "exitToolStripMenuItem";
            exitToolStripMenuItem.Size = new Size(92, 22);
            exitToolStripMenuItem.Text = "&Exit";
            exitToolStripMenuItem.Click += exitToolStripMenuItem_Click;
            // 
            // txtContentKeywords
            // 
            txtContentKeywords.Location = new Point(9, 156);
            txtContentKeywords.Name = "txtContentKeywords";
            txtContentKeywords.Size = new Size(366, 23);
            txtContentKeywords.TabIndex = 10;
            // 
            // label4
            // 
            label4.AutoSize = true;
            label4.Location = new Point(9, 138);
            label4.Name = "label4";
            label4.Size = new Size(168, 15);
            label4.TabIndex = 11;
            label4.Text = "Additional Keywords to Search";
            // 
            // btnStopSearch
            // 
            btnStopSearch.Location = new Point(12, 296);
            btnStopSearch.Name = "btnStopSearch";
            btnStopSearch.Size = new Size(112, 23);
            btnStopSearch.TabIndex = 12;
            btnStopSearch.Text = "Stop Search";
            btnStopSearch.UseVisualStyleBackColor = true;
            btnStopSearch.Click += btnStopSearch_Click;
            // 
            // remoteToolStripMenuItem
            // 
            remoteToolStripMenuItem.Name = "remoteToolStripMenuItem";
            remoteToolStripMenuItem.Size = new Size(60, 20);
            remoteToolStripMenuItem.Text = "Remote";
            remoteToolStripMenuItem.Click += remoteToolStripMenuItem_Click;
            // 
            // Password_finder
            // 
            AutoScaleDimensions = new SizeF(7F, 15F);
            AutoScaleMode = AutoScaleMode.Font;
            ClientSize = new Size(879, 450);
            Controls.Add(btnStopSearch);
            Controls.Add(label4);
            Controls.Add(txtContentKeywords);
            Controls.Add(btnSaveResults_Click);
            Controls.Add(txtResults);
            Controls.Add(label3);
            Controls.Add(btnSearch);
            Controls.Add(txtDirectories);
            Controls.Add(label2);
            Controls.Add(txtUserExtensions);
            Controls.Add(label1);
            Controls.Add(chkDefaultExtensions);
            Controls.Add(menuStrip1);
            MainMenuStrip = menuStrip1;
            Name = "Password_finder";
            Text = "Form1";
            menuStrip1.ResumeLayout(false);
            menuStrip1.PerformLayout();
            ResumeLayout(false);
            PerformLayout();
        }

        private void remoteToolStripMenuItem_Click(object sender, EventArgs e)
        {
            // Ensure the new form is disposed of correctly after closing
            using (RemoteClientForm remoteForm = new RemoteClientForm())
            {
                remoteForm.ShowDialog();
            }
            
        }

        #endregion

        private CheckBox chkDefaultExtensions;
        private Label label1;
        private TextBox txtUserExtensions;
        private Label label2;
        private TextBox txtDirectories;
        private Button btnSearch;
        private Label label3;
        private TextBox txtResults;
        private Button btnSaveResults_Click;
        private MenuStrip menuStrip1;
        private ToolStripMenuItem fileToolStripMenuItem;
        private ToolStripMenuItem exitToolStripMenuItem;
        private TextBox txtContentKeywords;
        private Label label4;
        private Button btnStopSearch;
        private ToolStripMenuItem remoteToolStripMenuItem;
    }
}
