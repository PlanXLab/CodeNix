namespace VSCodePortableInstaller
{
    partial class InstallForm
    {
        private System.ComponentModel.IContainer components = null;

        protected override void Dispose(bool disposing)
        {
            if (disposing && (components != null))
            {
                components.Dispose();
            }

            if (elapsedTimer != null)
            {
                elapsedTimer.Stop();
                elapsedTimer.Dispose();
                elapsedTimer = null;
            }

            base.Dispose(disposing);
        }

        #region Windows Form Designer generated code

        private void InitializeComponent()
        {
            this.headerPanel = new System.Windows.Forms.Panel();
            this.headerTitleLabel = new System.Windows.Forms.Label();
            this.headerSubLabel = new System.Windows.Forms.Label();
            this.settingsPanel = new System.Windows.Forms.Panel();
            this.pathLabel = new System.Windows.Forms.Label();
            this.installPathHint = new System.Windows.Forms.Label();
            this.installPathTextBox = new System.Windows.Forms.TextBox();
            this.browseButton = new System.Windows.Forms.Button();
            this.pythonLabel = new System.Windows.Forms.Label();
            this.pythonVersionTextBox = new System.Windows.Forms.TextBox();
            this.pythonHint = new System.Windows.Forms.Label();
            this.installButton = new System.Windows.Forms.Button();
            this.cancelButton = new System.Windows.Forms.Button();
            this.label1 = new System.Windows.Forms.Label();
            this.separatorPanel = new System.Windows.Forms.Panel();
            this.progressPanel = new System.Windows.Forms.Panel();
            this.componentTable = new System.Windows.Forms.TableLayoutPanel();
            this.pwshNameLabel = new System.Windows.Forms.Label();
            this.pwshProgressBar = new VSCodePortableCommon.CodeNixProgressBar();
            this.pwshStatusLabel = new System.Windows.Forms.Label();
            this.vscodeNameLabel = new System.Windows.Forms.Label();
            this.vscodeProgressBar = new VSCodePortableCommon.CodeNixProgressBar();
            this.vscodeStatusLabel = new System.Windows.Forms.Label();
            this.pythonNameLabel = new System.Windows.Forms.Label();
            this.pythonProgressBar = new VSCodePortableCommon.CodeNixProgressBar();
            this.pythonStatusLabel = new System.Windows.Forms.Label();
            this.overallProgress = new VSCodePortableCommon.CodeNixProgressBar();
            this.completedLabel = new System.Windows.Forms.Label();
            this.elapsedTimeLabel = new System.Windows.Forms.Label();
            this.currentTaskLabel = new System.Windows.Forms.Label();
            this.logHeaderLabel = new System.Windows.Forms.Label();
            this.activityLog = new System.Windows.Forms.TextBox();
            this.headerPanel.SuspendLayout();
            this.settingsPanel.SuspendLayout();
            this.progressPanel.SuspendLayout();
            this.componentTable.SuspendLayout();
            this.SuspendLayout();
            //
            // headerPanel
            //
            this.headerPanel.BackColor = System.Drawing.Color.FromArgb(0, 88, 160);
            this.headerPanel.Controls.Add(this.label1);
            this.headerPanel.Controls.Add(this.headerSubLabel);
            this.headerPanel.Controls.Add(this.headerTitleLabel);
            this.headerPanel.Location = new System.Drawing.Point(0, 0);
            this.headerPanel.Name = "headerPanel";
            this.headerPanel.Size = new System.Drawing.Size(720, 64);
            this.headerPanel.TabIndex = 0;
            //
            // headerTitleLabel
            //
            this.headerTitleLabel.AutoSize = true;
            this.headerTitleLabel.Font = new System.Drawing.Font("Segoe UI", 14F, System.Drawing.FontStyle.Bold);
            this.headerTitleLabel.ForeColor = System.Drawing.Color.White;
            this.headerTitleLabel.Location = new System.Drawing.Point(20, 10);
            this.headerTitleLabel.Name = "headerTitleLabel";
            this.headerTitleLabel.Text = "CodeNix";
            //
            // headerSubLabel
            //
            this.headerSubLabel.AutoSize = true;
            this.headerSubLabel.Font = new System.Drawing.Font("Segoe UI", 8.5F);
            this.headerSubLabel.ForeColor = System.Drawing.Color.FromArgb(176, 210, 240);
            this.headerSubLabel.Location = new System.Drawing.Point(22, 38);
            this.headerSubLabel.Name = "headerSubLabel";
            this.headerSubLabel.Text = "Sets up a portable VSCode, pwsh, and Python (uv) environment.";
            //
            // settingsPanel
            //
            this.settingsPanel.BackColor = System.Drawing.Color.FromArgb(249, 249, 249);
            this.settingsPanel.Controls.Add(this.cancelButton);
            this.settingsPanel.Controls.Add(this.installButton);
            this.settingsPanel.Controls.Add(this.installPathHint);
            this.settingsPanel.Controls.Add(this.pythonHint);
            this.settingsPanel.Controls.Add(this.pythonVersionTextBox);
            this.settingsPanel.Controls.Add(this.pythonLabel);
            this.settingsPanel.Controls.Add(this.browseButton);
            this.settingsPanel.Controls.Add(this.installPathTextBox);
            this.settingsPanel.Controls.Add(this.pathLabel);
            this.settingsPanel.Location = new System.Drawing.Point(0, 64);
            this.settingsPanel.Name = "settingsPanel";
            this.settingsPanel.Size = new System.Drawing.Size(720, 120);
            this.settingsPanel.TabIndex = 1;
            //
            // pathLabel
            //
            this.pathLabel.Font = new System.Drawing.Font("Segoe UI", 9F);
            this.pathLabel.ForeColor = System.Drawing.Color.FromArgb(50, 50, 50);
            this.pathLabel.Location = new System.Drawing.Point(24, 14);
            this.pathLabel.Name = "pathLabel";
            this.pathLabel.Size = new System.Drawing.Size(100, 22);
            this.pathLabel.Text = "Installation Path";
            this.pathLabel.TextAlign = System.Drawing.ContentAlignment.MiddleLeft;
            //
            // installPathTextBox
            //
            this.installPathTextBox.BorderStyle = System.Windows.Forms.BorderStyle.FixedSingle;
            this.installPathTextBox.Font = new System.Drawing.Font("Segoe UI", 9F);
            this.installPathTextBox.Location = new System.Drawing.Point(128, 12);
            this.installPathTextBox.Name = "installPathTextBox";
            this.installPathTextBox.Size = new System.Drawing.Size(460, 23);
            this.installPathTextBox.Text = "C:\\VSCode";
            this.installPathTextBox.TabIndex = 0;
            //
            // installPathHint
            //
            this.installPathHint.AutoEllipsis = true;
            this.installPathHint.Font = new System.Drawing.Font("Segoe UI", 8F);
            this.installPathHint.ForeColor = System.Drawing.Color.FromArgb(150, 150, 150);
            this.installPathHint.Location = new System.Drawing.Point(24, 88);
            this.installPathHint.Name = "installPathHint";
            this.installPathHint.Size = new System.Drawing.Size(440, 18);
            this.installPathHint.Text = "Target folder must not already exist.";
            this.installPathHint.TextAlign = System.Drawing.ContentAlignment.MiddleLeft;
            //
            // browseButton
            //
            this.browseButton.BackColor = System.Drawing.Color.White;
            this.browseButton.FlatStyle = System.Windows.Forms.FlatStyle.Flat;
            this.browseButton.FlatAppearance.BorderColor = System.Drawing.Color.FromArgb(200, 200, 200);
            this.browseButton.FlatAppearance.MouseOverBackColor = System.Drawing.Color.FromArgb(240, 240, 240);
            this.browseButton.FlatAppearance.MouseDownBackColor = System.Drawing.Color.FromArgb(225, 225, 225);
            this.browseButton.Font = new System.Drawing.Font("Segoe UI", 8.5F);
            this.browseButton.ForeColor = System.Drawing.Color.FromArgb(50, 50, 50);
            this.browseButton.Location = new System.Drawing.Point(594, 10);
            this.browseButton.Name = "browseButton";
            this.browseButton.Size = new System.Drawing.Size(100, 27);
            this.browseButton.Text = "Browse...";
            this.browseButton.UseVisualStyleBackColor = false;
            this.browseButton.Cursor = System.Windows.Forms.Cursors.Hand;
            this.browseButton.TabIndex = 1;
            this.browseButton.Click += new System.EventHandler(this.BrowseButton_Click);
            //
            // pythonLabel
            //
            this.pythonLabel.Font = new System.Drawing.Font("Segoe UI", 9F);
            this.pythonLabel.ForeColor = System.Drawing.Color.FromArgb(50, 50, 50);
            this.pythonLabel.Location = new System.Drawing.Point(24, 48);
            this.pythonLabel.Name = "pythonLabel";
            this.pythonLabel.Size = new System.Drawing.Size(100, 22);
            this.pythonLabel.Text = "Python Version";
            this.pythonLabel.TextAlign = System.Drawing.ContentAlignment.MiddleLeft;
            //
            // pythonVersionTextBox
            //
            this.pythonVersionTextBox.BorderStyle = System.Windows.Forms.BorderStyle.FixedSingle;
            this.pythonVersionTextBox.Font = new System.Drawing.Font("Segoe UI", 9F);
            this.pythonVersionTextBox.Location = new System.Drawing.Point(128, 46);
            this.pythonVersionTextBox.Name = "pythonVersionTextBox";
            this.pythonVersionTextBox.Size = new System.Drawing.Size(150, 23);
            this.pythonVersionTextBox.Text = "";
            this.pythonVersionTextBox.TabIndex = 2;
            //
            // pythonHint
            //
            this.pythonHint.Font = new System.Drawing.Font("Segoe UI", 8F);
            this.pythonHint.ForeColor = System.Drawing.Color.FromArgb(150, 150, 150);
            this.pythonHint.Location = new System.Drawing.Point(284, 49);
            this.pythonHint.Name = "pythonHint";
            this.pythonHint.Size = new System.Drawing.Size(200, 18);
            this.pythonHint.Text = "Blank = latest; enter x.y or x.y.z";
            this.pythonHint.TextAlign = System.Drawing.ContentAlignment.MiddleLeft;
            //
            // label1
            //
            this.label1.Font = new System.Drawing.Font("Segoe UI", 8F);
            this.label1.ForeColor = System.Drawing.Color.FromArgb(214, 232, 247);
            this.label1.Location = new System.Drawing.Point(414, 22);
            this.label1.Name = "label1";
            this.label1.Size = new System.Drawing.Size(286, 20);
            this.label1.Text = "Created by Atticle at PlanXLab | devcamp@planxlab.com";
            this.label1.TextAlign = System.Drawing.ContentAlignment.MiddleRight;
            //
            // installButton
            //
            this.installButton.BackColor = System.Drawing.Color.FromArgb(0, 120, 212);
            this.installButton.FlatStyle = System.Windows.Forms.FlatStyle.Flat;
            this.installButton.FlatAppearance.BorderSize = 0;
            this.installButton.FlatAppearance.MouseOverBackColor = System.Drawing.Color.FromArgb(16, 110, 190);
            this.installButton.FlatAppearance.MouseDownBackColor = System.Drawing.Color.FromArgb(0, 90, 158);
            this.installButton.Font = new System.Drawing.Font("Segoe UI", 9.5F, System.Drawing.FontStyle.Bold);
            this.installButton.ForeColor = System.Drawing.Color.White;
            this.installButton.Location = new System.Drawing.Point(478, 80);
            this.installButton.Name = "installButton";
            this.installButton.Size = new System.Drawing.Size(110, 34);
            this.installButton.Text = "Install";
            this.installButton.Enabled = false;
            this.installButton.UseVisualStyleBackColor = false;
            this.installButton.Cursor = System.Windows.Forms.Cursors.Hand;
            this.installButton.TabIndex = 3;
            this.installButton.Click += new System.EventHandler(this.InstallButton_Click);
            //
            // cancelButton
            //
            this.cancelButton.BackColor = System.Drawing.Color.White;
            this.cancelButton.FlatStyle = System.Windows.Forms.FlatStyle.Flat;
            this.cancelButton.FlatAppearance.BorderColor = System.Drawing.Color.FromArgb(210, 210, 210);
            this.cancelButton.FlatAppearance.MouseOverBackColor = System.Drawing.Color.FromArgb(240, 240, 240);
            this.cancelButton.FlatAppearance.MouseDownBackColor = System.Drawing.Color.FromArgb(225, 225, 225);
            this.cancelButton.Font = new System.Drawing.Font("Segoe UI", 9F);
            this.cancelButton.ForeColor = System.Drawing.Color.FromArgb(50, 50, 50);
            this.cancelButton.Location = new System.Drawing.Point(594, 80);
            this.cancelButton.Name = "cancelButton";
            this.cancelButton.Size = new System.Drawing.Size(100, 34);
            this.cancelButton.Text = "Cancel";
            this.cancelButton.Enabled = true;
            this.cancelButton.UseVisualStyleBackColor = false;
            this.cancelButton.Cursor = System.Windows.Forms.Cursors.Hand;
            this.cancelButton.TabIndex = 4;
            this.cancelButton.Click += new System.EventHandler(this.CancelButton_Click);
            //
            // separatorPanel
            //
            this.separatorPanel.BackColor = System.Drawing.Color.FromArgb(228, 228, 228);
            this.separatorPanel.Location = new System.Drawing.Point(0, 184);
            this.separatorPanel.Name = "separatorPanel";
            this.separatorPanel.Size = new System.Drawing.Size(720, 1);
            this.separatorPanel.TabIndex = 2;
            //
            // progressPanel
            //
            this.progressPanel.BackColor = System.Drawing.Color.White;
            this.progressPanel.Controls.Add(this.componentTable);
            this.progressPanel.Controls.Add(this.overallProgress);
            this.progressPanel.Controls.Add(this.completedLabel);
            this.progressPanel.Controls.Add(this.elapsedTimeLabel);
            this.progressPanel.Controls.Add(this.currentTaskLabel);
            this.progressPanel.Controls.Add(this.logHeaderLabel);
            this.progressPanel.Controls.Add(this.activityLog);
            this.progressPanel.Location = new System.Drawing.Point(0, 185);
            this.progressPanel.Name = "progressPanel";
            this.progressPanel.Size = new System.Drawing.Size(720, 345);
            this.progressPanel.TabIndex = 3;
            //
            // componentTable
            //
            this.componentTable.ColumnCount = 3;
            this.componentTable.ColumnStyles.Add(new System.Windows.Forms.ColumnStyle(System.Windows.Forms.SizeType.Absolute, 96F));
            this.componentTable.ColumnStyles.Add(new System.Windows.Forms.ColumnStyle(System.Windows.Forms.SizeType.Percent, 100F));
            this.componentTable.ColumnStyles.Add(new System.Windows.Forms.ColumnStyle(System.Windows.Forms.SizeType.Absolute, 110F));
            this.componentTable.Controls.Add(this.pwshNameLabel, 0, 0);
            this.componentTable.Controls.Add(this.pwshProgressBar, 1, 0);
            this.componentTable.Controls.Add(this.pwshStatusLabel, 2, 0);
            this.componentTable.Controls.Add(this.vscodeNameLabel, 0, 1);
            this.componentTable.Controls.Add(this.vscodeProgressBar, 1, 1);
            this.componentTable.Controls.Add(this.vscodeStatusLabel, 2, 1);
            this.componentTable.Controls.Add(this.pythonNameLabel, 0, 2);
            this.componentTable.Controls.Add(this.pythonProgressBar, 1, 2);
            this.componentTable.Controls.Add(this.pythonStatusLabel, 2, 2);
            this.componentTable.Location = new System.Drawing.Point(20, 14);
            this.componentTable.Name = "componentTable";
            this.componentTable.RowCount = 3;
            this.componentTable.RowStyles.Add(new System.Windows.Forms.RowStyle(System.Windows.Forms.SizeType.Percent, 33.333F));
            this.componentTable.RowStyles.Add(new System.Windows.Forms.RowStyle(System.Windows.Forms.SizeType.Percent, 33.333F));
            this.componentTable.RowStyles.Add(new System.Windows.Forms.RowStyle(System.Windows.Forms.SizeType.Percent, 33.334F));
            this.componentTable.Size = new System.Drawing.Size(680, 148);
            this.componentTable.TabIndex = 0;
            //
            // pwshNameLabel
            //
            this.pwshNameLabel.AutoSize = false;
            this.pwshNameLabel.Anchor = System.Windows.Forms.AnchorStyles.Left;
            this.pwshNameLabel.Font = new System.Drawing.Font("Segoe UI", 9F);
            this.pwshNameLabel.ForeColor = System.Drawing.Color.FromArgb(50, 50, 50);
            this.pwshNameLabel.Location = new System.Drawing.Point(3, 0);
            this.pwshNameLabel.Name = "pwshNameLabel";
            this.pwshNameLabel.Size = new System.Drawing.Size(90, 37);
            this.pwshNameLabel.Text = "PowerShell 7";
            this.pwshNameLabel.TextAlign = System.Drawing.ContentAlignment.MiddleLeft;
            //
            // pwshProgressBar
            //
            this.pwshProgressBar.Anchor = ((System.Windows.Forms.AnchorStyles)((System.Windows.Forms.AnchorStyles.Left | System.Windows.Forms.AnchorStyles.Right)));
            this.pwshProgressBar.Location = new System.Drawing.Point(99, 12);
            this.pwshProgressBar.Name = "pwshProgressBar";
            this.pwshProgressBar.Size = new System.Drawing.Size(468, 12);
            //
            // pwshStatusLabel
            //
            this.pwshStatusLabel.AutoSize = false;
            this.pwshStatusLabel.Anchor = System.Windows.Forms.AnchorStyles.Right;
            this.pwshStatusLabel.Font = new System.Drawing.Font("Segoe UI", 8.5F);
            this.pwshStatusLabel.ForeColor = System.Drawing.Color.FromArgb(160, 160, 160);
            this.pwshStatusLabel.Location = new System.Drawing.Point(573, 0);
            this.pwshStatusLabel.Name = "pwshStatusLabel";
            this.pwshStatusLabel.Size = new System.Drawing.Size(104, 37);
            this.pwshStatusLabel.Text = "Waiting";
            this.pwshStatusLabel.TextAlign = System.Drawing.ContentAlignment.MiddleRight;
            //
            // vscodeNameLabel
            //
            this.vscodeNameLabel.AutoSize = false;
            this.vscodeNameLabel.Anchor = System.Windows.Forms.AnchorStyles.Left;
            this.vscodeNameLabel.Font = new System.Drawing.Font("Segoe UI", 9F);
            this.vscodeNameLabel.ForeColor = System.Drawing.Color.FromArgb(50, 50, 50);
            this.vscodeNameLabel.Location = new System.Drawing.Point(3, 49);
            this.vscodeNameLabel.Name = "vscodeNameLabel";
            this.vscodeNameLabel.Size = new System.Drawing.Size(90, 37);
            this.vscodeNameLabel.Text = "VS Code";
            this.vscodeNameLabel.TextAlign = System.Drawing.ContentAlignment.MiddleLeft;
            //
            // vscodeProgressBar
            //
            this.vscodeProgressBar.Anchor = ((System.Windows.Forms.AnchorStyles)((System.Windows.Forms.AnchorStyles.Left | System.Windows.Forms.AnchorStyles.Right)));
            this.vscodeProgressBar.Location = new System.Drawing.Point(99, 61);
            this.vscodeProgressBar.Name = "vscodeProgressBar";
            this.vscodeProgressBar.Size = new System.Drawing.Size(468, 12);
            //
            // vscodeStatusLabel
            //
            this.vscodeStatusLabel.AutoSize = false;
            this.vscodeStatusLabel.Anchor = System.Windows.Forms.AnchorStyles.Right;
            this.vscodeStatusLabel.Font = new System.Drawing.Font("Segoe UI", 8.5F);
            this.vscodeStatusLabel.ForeColor = System.Drawing.Color.FromArgb(160, 160, 160);
            this.vscodeStatusLabel.Location = new System.Drawing.Point(573, 49);
            this.vscodeStatusLabel.Name = "vscodeStatusLabel";
            this.vscodeStatusLabel.Size = new System.Drawing.Size(104, 37);
            this.vscodeStatusLabel.Text = "Waiting";
            this.vscodeStatusLabel.TextAlign = System.Drawing.ContentAlignment.MiddleRight;
            //
            // pythonNameLabel
            //
            this.pythonNameLabel.AutoSize = false;
            this.pythonNameLabel.Anchor = System.Windows.Forms.AnchorStyles.Left;
            this.pythonNameLabel.Font = new System.Drawing.Font("Segoe UI", 9F);
            this.pythonNameLabel.ForeColor = System.Drawing.Color.FromArgb(50, 50, 50);
            this.pythonNameLabel.Location = new System.Drawing.Point(3, 98);
            this.pythonNameLabel.Name = "pythonNameLabel";
            this.pythonNameLabel.Size = new System.Drawing.Size(90, 37);
            this.pythonNameLabel.Text = "Python";
            this.pythonNameLabel.TextAlign = System.Drawing.ContentAlignment.MiddleLeft;
            //
            // pythonProgressBar
            //
            this.pythonProgressBar.Anchor = ((System.Windows.Forms.AnchorStyles)((System.Windows.Forms.AnchorStyles.Left | System.Windows.Forms.AnchorStyles.Right)));
            this.pythonProgressBar.Location = new System.Drawing.Point(99, 110);
            this.pythonProgressBar.Name = "pythonProgressBar";
            this.pythonProgressBar.Size = new System.Drawing.Size(468, 12);
            //
            // pythonStatusLabel
            //
            this.pythonStatusLabel.AutoSize = false;
            this.pythonStatusLabel.Anchor = System.Windows.Forms.AnchorStyles.Right;
            this.pythonStatusLabel.Font = new System.Drawing.Font("Segoe UI", 8.5F);
            this.pythonStatusLabel.ForeColor = System.Drawing.Color.FromArgb(160, 160, 160);
            this.pythonStatusLabel.Location = new System.Drawing.Point(573, 98);
            this.pythonStatusLabel.Name = "pythonStatusLabel";
            this.pythonStatusLabel.Size = new System.Drawing.Size(104, 37);
            this.pythonStatusLabel.Text = "Waiting";
            this.pythonStatusLabel.TextAlign = System.Drawing.ContentAlignment.MiddleRight;
            //
            // overallProgress
            //
            this.overallProgress.Location = new System.Drawing.Point(20, 168);
            this.overallProgress.Name = "overallProgress";
            this.overallProgress.Size = new System.Drawing.Size(680, 8);
            this.overallProgress.TabIndex = 1;
            //
            // completedLabel
            //
            this.completedLabel.Font = new System.Drawing.Font("Segoe UI", 8.5F);
            this.completedLabel.ForeColor = System.Drawing.Color.FromArgb(120, 120, 120);
            this.completedLabel.Location = new System.Drawing.Point(20, 180);
            this.completedLabel.Name = "completedLabel";
            this.completedLabel.Size = new System.Drawing.Size(250, 18);
            this.completedLabel.TabIndex = 2;
            this.completedLabel.Text = "Overall Progress: 0% complete";
            //
            // elapsedTimeLabel
            //
            this.elapsedTimeLabel.Font = new System.Drawing.Font("Segoe UI", 8.5F);
            this.elapsedTimeLabel.ForeColor = System.Drawing.Color.FromArgb(120, 120, 120);
            this.elapsedTimeLabel.Location = new System.Drawing.Point(272, 180);
            this.elapsedTimeLabel.Name = "elapsedTimeLabel";
            this.elapsedTimeLabel.Size = new System.Drawing.Size(150, 18);
            this.elapsedTimeLabel.TabIndex = 3;
            this.elapsedTimeLabel.Text = "Elapsed (mm:ss): 00:00";
            //
            // currentTaskLabel
            //
            this.currentTaskLabel.Font = new System.Drawing.Font("Segoe UI", 8.5F);
            this.currentTaskLabel.ForeColor = System.Drawing.Color.FromArgb(0, 120, 212);
            this.currentTaskLabel.Location = new System.Drawing.Point(430, 180);
            this.currentTaskLabel.Name = "currentTaskLabel";
            this.currentTaskLabel.Size = new System.Drawing.Size(270, 18);
            this.currentTaskLabel.TabIndex = 4;
            this.currentTaskLabel.Text = "Ready to install";
            this.currentTaskLabel.TextAlign = System.Drawing.ContentAlignment.MiddleRight;
            //
            // logHeaderLabel
            //
            this.logHeaderLabel.AutoSize = true;
            this.logHeaderLabel.Font = new System.Drawing.Font("Segoe UI", 8F);
            this.logHeaderLabel.ForeColor = System.Drawing.Color.FromArgb(140, 140, 140);
            this.logHeaderLabel.Location = new System.Drawing.Point(20, 206);
            this.logHeaderLabel.Name = "logHeaderLabel";
            this.logHeaderLabel.Size = new System.Drawing.Size(64, 13);
            this.logHeaderLabel.TabIndex = 5;
            this.logHeaderLabel.Text = "Key Events";
            //
            // activityLog
            //
            this.activityLog.BackColor = System.Drawing.Color.FromArgb(248, 248, 248);
            this.activityLog.BorderStyle = System.Windows.Forms.BorderStyle.FixedSingle;
            this.activityLog.Font = new System.Drawing.Font("Segoe UI", 8.5F);
            this.activityLog.ForeColor = System.Drawing.Color.FromArgb(64, 64, 64);
            this.activityLog.Location = new System.Drawing.Point(20, 224);
            this.activityLog.Multiline = true;
            this.activityLog.Name = "activityLog";
            this.activityLog.ReadOnly = true;
            this.activityLog.ScrollBars = System.Windows.Forms.ScrollBars.Vertical;
            this.activityLog.Size = new System.Drawing.Size(680, 112);
            this.activityLog.TabIndex = 6;
            this.activityLog.TabStop = false;
            //
            // InstallForm
            //
            this.BackColor = System.Drawing.Color.White;
            this.ClientSize = new System.Drawing.Size(720, 530);
            this.Controls.Add(this.progressPanel);
            this.Controls.Add(this.separatorPanel);
            this.Controls.Add(this.settingsPanel);
            this.Controls.Add(this.headerPanel);
            this.FormBorderStyle = System.Windows.Forms.FormBorderStyle.None;
            this.MaximizeBox = false;
            this.Name = "InstallForm";
            this.StartPosition = System.Windows.Forms.FormStartPosition.CenterScreen;
            this.Text = "CodeNix";
            this.TopMost = true;
            this.headerPanel.ResumeLayout(false);
            this.headerPanel.PerformLayout();
            this.settingsPanel.ResumeLayout(false);
            this.settingsPanel.PerformLayout();
            this.componentTable.ResumeLayout(false);
            this.progressPanel.ResumeLayout(false);
            this.progressPanel.PerformLayout();
            this.ResumeLayout(false);
        }

        #endregion

        private System.Windows.Forms.Panel headerPanel;
        private System.Windows.Forms.Label headerTitleLabel;
        private System.Windows.Forms.Label headerSubLabel;
        private System.Windows.Forms.Panel settingsPanel;
        private System.Windows.Forms.Label pathLabel;
        private System.Windows.Forms.Label installPathHint;
        private System.Windows.Forms.TextBox installPathTextBox;
        private System.Windows.Forms.Button browseButton;
        private System.Windows.Forms.Label pythonLabel;
        private System.Windows.Forms.TextBox pythonVersionTextBox;
        private System.Windows.Forms.Label pythonHint;
        private System.Windows.Forms.Button installButton;
        private System.Windows.Forms.Button cancelButton;
        private System.Windows.Forms.Label label1;
        private System.Windows.Forms.Panel separatorPanel;
        private System.Windows.Forms.Panel progressPanel;
        private System.Windows.Forms.TableLayoutPanel componentTable;
        private System.Windows.Forms.Label pwshNameLabel;
        private VSCodePortableCommon.CodeNixProgressBar pwshProgressBar;
        private System.Windows.Forms.Label pwshStatusLabel;
        private System.Windows.Forms.Label vscodeNameLabel;
        private VSCodePortableCommon.CodeNixProgressBar vscodeProgressBar;
        private System.Windows.Forms.Label vscodeStatusLabel;
        private System.Windows.Forms.Label pythonNameLabel;
        private VSCodePortableCommon.CodeNixProgressBar pythonProgressBar;
        private System.Windows.Forms.Label pythonStatusLabel;
        private VSCodePortableCommon.CodeNixProgressBar overallProgress;
        private System.Windows.Forms.Label completedLabel;
        private System.Windows.Forms.Label elapsedTimeLabel;
        private System.Windows.Forms.Label currentTaskLabel;
        private System.Windows.Forms.Label logHeaderLabel;
        private System.Windows.Forms.TextBox activityLog;
    }
}
