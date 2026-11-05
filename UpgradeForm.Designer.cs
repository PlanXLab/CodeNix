namespace VSCodePortableLauncher
{
    partial class UpgradeForm
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
            this.label1 = new System.Windows.Forms.Label();
            this.headerSubLabel = new System.Windows.Forms.Label();
            this.headerTitleLabel = new System.Windows.Forms.Label();
            this.settingsPanel = new System.Windows.Forms.Panel();
            this.summaryBodyLabel = new System.Windows.Forms.Label();
            this.summaryTitleLabel = new System.Windows.Forms.Label();
            this.cancelButton = new System.Windows.Forms.Button();
            this.upgradeButton = new System.Windows.Forms.Button();
            this.separatorPanel = new System.Windows.Forms.Panel();
            this.progressPanel = new System.Windows.Forms.Panel();
            this.activityLog = new System.Windows.Forms.TextBox();
            this.logHeaderLabel = new System.Windows.Forms.Label();
            this.currentTaskLabel = new System.Windows.Forms.Label();
            this.elapsedTimeLabel = new System.Windows.Forms.Label();
            this.completedLabel = new System.Windows.Forms.Label();
            this.overallProgress = new System.Windows.Forms.ProgressBar();
            this.componentTable = new System.Windows.Forms.TableLayoutPanel();
            this.pwsh7NameLabel = new System.Windows.Forms.Label();
            this.pwsh7ProgressBar = new System.Windows.Forms.ProgressBar();
            this.pwsh7StatusLabel = new System.Windows.Forms.Label();
            this.ohmyposhNameLabel = new System.Windows.Forms.Label();
            this.ohmyposhProgressBar = new System.Windows.Forms.ProgressBar();
            this.ohmyposhStatusLabel = new System.Windows.Forms.Label();
            this.psfzfNameLabel = new System.Windows.Forms.Label();
            this.psfzfProgressBar = new System.Windows.Forms.ProgressBar();
            this.psfzfStatusLabel = new System.Windows.Forms.Label();
            this.modernUnixNameLabel = new System.Windows.Forms.Label();
            this.modernUnixProgressBar = new System.Windows.Forms.ProgressBar();
            this.modernUnixStatusLabel = new System.Windows.Forms.Label();
            this.vscodeNameLabel = new System.Windows.Forms.Label();
            this.vscodeProgressBar = new System.Windows.Forms.ProgressBar();
            this.vscodeStatusLabel = new System.Windows.Forms.Label();
            this.pythonNameLabel = new System.Windows.Forms.Label();
            this.pythonProgressBar = new System.Windows.Forms.ProgressBar();
            this.pythonStatusLabel = new System.Windows.Forms.Label();
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
            // label1
            //
            this.label1.Font = new System.Drawing.Font("Segoe UI", 8F);
            this.label1.ForeColor = System.Drawing.Color.FromArgb(214, 232, 247);
            this.label1.Location = new System.Drawing.Point(414, 22);
            this.label1.Name = "label1";
            this.label1.Size = new System.Drawing.Size(286, 20);
            this.label1.TabIndex = 2;
            this.label1.Text = "Created by Atticle at PlanXLab | devcamp@planxlab.com";
            this.label1.TextAlign = System.Drawing.ContentAlignment.MiddleRight;
            //
            // headerSubLabel
            //
            this.headerSubLabel.AutoSize = true;
            this.headerSubLabel.Font = new System.Drawing.Font("Segoe UI", 8.5F);
            this.headerSubLabel.ForeColor = System.Drawing.Color.FromArgb(176, 210, 240);
            this.headerSubLabel.Location = new System.Drawing.Point(22, 38);
            this.headerSubLabel.Name = "headerSubLabel";
            this.headerSubLabel.Size = new System.Drawing.Size(234, 15);
            this.headerSubLabel.TabIndex = 1;
            this.headerSubLabel.Text = "Launches portable VSCode with auto-updates on startup.";
            //
            // headerTitleLabel
            //
            this.headerTitleLabel.AutoSize = true;
            this.headerTitleLabel.Font = new System.Drawing.Font("Segoe UI", 14F, System.Drawing.FontStyle.Bold);
            this.headerTitleLabel.ForeColor = System.Drawing.Color.White;
            this.headerTitleLabel.Location = new System.Drawing.Point(20, 10);
            this.headerTitleLabel.Name = "headerTitleLabel";
            this.headerTitleLabel.Size = new System.Drawing.Size(138, 25);
            this.headerTitleLabel.TabIndex = 0;
            this.headerTitleLabel.Text = "CodeNix";
            //
            // settingsPanel
            //
            this.settingsPanel.BackColor = System.Drawing.Color.FromArgb(249, 249, 249);
            this.settingsPanel.Controls.Add(this.summaryBodyLabel);
            this.settingsPanel.Controls.Add(this.summaryTitleLabel);
            this.settingsPanel.Controls.Add(this.cancelButton);
            this.settingsPanel.Controls.Add(this.upgradeButton);
            this.settingsPanel.Location = new System.Drawing.Point(0, 64);
            this.settingsPanel.Name = "settingsPanel";
            this.settingsPanel.Size = new System.Drawing.Size(720, 120);
            this.settingsPanel.TabIndex = 1;
            //
            // summaryBodyLabel
            //
            this.summaryBodyLabel.Font = new System.Drawing.Font("Segoe UI", 8.5F);
            this.summaryBodyLabel.ForeColor = System.Drawing.Color.FromArgb(110, 110, 110);
            this.summaryBodyLabel.Location = new System.Drawing.Point(24, 46);
            this.summaryBodyLabel.Name = "summaryBodyLabel";
            this.summaryBodyLabel.Size = new System.Drawing.Size(430, 54);
            this.summaryBodyLabel.TabIndex = 1;
            this.summaryBodyLabel.Text = "Selected components will be updated while keeping the portable environment in place.";
            //
            // summaryTitleLabel
            //
            this.summaryTitleLabel.Font = new System.Drawing.Font("Segoe UI", 10F, System.Drawing.FontStyle.Bold);
            this.summaryTitleLabel.ForeColor = System.Drawing.Color.FromArgb(50, 50, 50);
            this.summaryTitleLabel.Location = new System.Drawing.Point(24, 18);
            this.summaryTitleLabel.Name = "summaryTitleLabel";
            this.summaryTitleLabel.Size = new System.Drawing.Size(420, 22);
            this.summaryTitleLabel.TabIndex = 0;
            this.summaryTitleLabel.Text = "Pending updates";
            this.summaryTitleLabel.TextAlign = System.Drawing.ContentAlignment.MiddleLeft;
            //
            // cancelButton
            //
            this.cancelButton.BackColor = System.Drawing.Color.White;
            this.cancelButton.Cursor = System.Windows.Forms.Cursors.Hand;
            this.cancelButton.FlatAppearance.BorderColor = System.Drawing.Color.FromArgb(210, 210, 210);
            this.cancelButton.FlatAppearance.MouseDownBackColor = System.Drawing.Color.FromArgb(225, 225, 225);
            this.cancelButton.FlatAppearance.MouseOverBackColor = System.Drawing.Color.FromArgb(240, 240, 240);
            this.cancelButton.FlatStyle = System.Windows.Forms.FlatStyle.Flat;
            this.cancelButton.Font = new System.Drawing.Font("Segoe UI", 9F);
            this.cancelButton.ForeColor = System.Drawing.Color.FromArgb(50, 50, 50);
            this.cancelButton.Location = new System.Drawing.Point(594, 80);
            this.cancelButton.Name = "cancelButton";
            this.cancelButton.Size = new System.Drawing.Size(100, 34);
            this.cancelButton.TabIndex = 3;
            this.cancelButton.Text = "Cancel";
            this.cancelButton.UseVisualStyleBackColor = false;
            this.cancelButton.Click += new System.EventHandler(this.CancelButton_Click);
            //
            // upgradeButton
            //
            this.upgradeButton.BackColor = System.Drawing.Color.FromArgb(0, 120, 212);
            this.upgradeButton.Cursor = System.Windows.Forms.Cursors.Hand;
            this.upgradeButton.FlatAppearance.BorderSize = 0;
            this.upgradeButton.FlatAppearance.MouseDownBackColor = System.Drawing.Color.FromArgb(0, 90, 158);
            this.upgradeButton.FlatAppearance.MouseOverBackColor = System.Drawing.Color.FromArgb(16, 110, 190);
            this.upgradeButton.FlatStyle = System.Windows.Forms.FlatStyle.Flat;
            this.upgradeButton.Font = new System.Drawing.Font("Segoe UI", 9.5F, System.Drawing.FontStyle.Bold);
            this.upgradeButton.ForeColor = System.Drawing.Color.White;
            this.upgradeButton.Location = new System.Drawing.Point(478, 80);
            this.upgradeButton.Name = "upgradeButton";
            this.upgradeButton.Size = new System.Drawing.Size(110, 34);
            this.upgradeButton.TabIndex = 2;
            this.upgradeButton.Text = "Upgrade";
            this.upgradeButton.UseVisualStyleBackColor = false;
            this.upgradeButton.Click += new System.EventHandler(this.UpgradeButton_Click);
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
            this.progressPanel.Controls.Add(this.activityLog);
            this.progressPanel.Controls.Add(this.logHeaderLabel);
            this.progressPanel.Controls.Add(this.currentTaskLabel);
            this.progressPanel.Controls.Add(this.elapsedTimeLabel);
            this.progressPanel.Controls.Add(this.completedLabel);
            this.progressPanel.Controls.Add(this.overallProgress);
            this.progressPanel.Controls.Add(this.componentTable);
            this.progressPanel.Location = new System.Drawing.Point(0, 185);
            this.progressPanel.Name = "progressPanel";
            this.progressPanel.Size = new System.Drawing.Size(720, 350);
            this.progressPanel.TabIndex = 3;
            //
            // activityLog
            //
            this.activityLog.BackColor = System.Drawing.Color.FromArgb(248, 248, 248);
            this.activityLog.BorderStyle = System.Windows.Forms.BorderStyle.FixedSingle;
            this.activityLog.Font = new System.Drawing.Font("Segoe UI", 8.5F);
            this.activityLog.ForeColor = System.Drawing.Color.FromArgb(64, 64, 64);
            this.activityLog.Location = new System.Drawing.Point(20, 264);
            this.activityLog.Multiline = true;
            this.activityLog.Name = "activityLog";
            this.activityLog.ReadOnly = true;
            this.activityLog.ScrollBars = System.Windows.Forms.ScrollBars.Vertical;
            this.activityLog.Size = new System.Drawing.Size(680, 74);
            this.activityLog.TabIndex = 6;
            this.activityLog.TabStop = false;
            //
            // logHeaderLabel
            //
            this.logHeaderLabel.AutoSize = true;
            this.logHeaderLabel.Font = new System.Drawing.Font("Segoe UI", 8F);
            this.logHeaderLabel.ForeColor = System.Drawing.Color.FromArgb(140, 140, 140);
            this.logHeaderLabel.Location = new System.Drawing.Point(20, 246);
            this.logHeaderLabel.Name = "logHeaderLabel";
            this.logHeaderLabel.Size = new System.Drawing.Size(56, 13);
            this.logHeaderLabel.TabIndex = 5;
            this.logHeaderLabel.Text = "Key Events";
            //
            // currentTaskLabel
            //
            this.currentTaskLabel.Font = new System.Drawing.Font("Segoe UI", 8.5F);
            this.currentTaskLabel.ForeColor = System.Drawing.Color.FromArgb(0, 120, 212);
            this.currentTaskLabel.Location = new System.Drawing.Point(430, 220);
            this.currentTaskLabel.Name = "currentTaskLabel";
            this.currentTaskLabel.Size = new System.Drawing.Size(270, 18);
            this.currentTaskLabel.TabIndex = 4;
            this.currentTaskLabel.Text = "Ready to upgrade";
            this.currentTaskLabel.TextAlign = System.Drawing.ContentAlignment.MiddleRight;
            //
            // elapsedTimeLabel
            //
            this.elapsedTimeLabel.Font = new System.Drawing.Font("Segoe UI", 8.5F);
            this.elapsedTimeLabel.ForeColor = System.Drawing.Color.FromArgb(120, 120, 120);
            this.elapsedTimeLabel.Location = new System.Drawing.Point(272, 220);
            this.elapsedTimeLabel.Name = "elapsedTimeLabel";
            this.elapsedTimeLabel.Size = new System.Drawing.Size(150, 18);
            this.elapsedTimeLabel.TabIndex = 3;
            this.elapsedTimeLabel.Text = "Elapsed (mm:ss): 00:00";
            //
            // completedLabel
            //
            this.completedLabel.Font = new System.Drawing.Font("Segoe UI", 8.5F);
            this.completedLabel.ForeColor = System.Drawing.Color.FromArgb(120, 120, 120);
            this.completedLabel.Location = new System.Drawing.Point(20, 220);
            this.completedLabel.Name = "completedLabel";
            this.completedLabel.Size = new System.Drawing.Size(250, 18);
            this.completedLabel.TabIndex = 2;
            this.completedLabel.Text = "Overall Progress: 0% complete";
            //
            // overallProgress
            //
            this.overallProgress.Location = new System.Drawing.Point(20, 208);
            this.overallProgress.Name = "overallProgress";
            this.overallProgress.Size = new System.Drawing.Size(680, 8);
            this.overallProgress.Style = System.Windows.Forms.ProgressBarStyle.Continuous;
            this.overallProgress.TabIndex = 1;
            //
            // componentTable
            //
            this.componentTable.ColumnCount = 3;
            this.componentTable.ColumnStyles.Add(new System.Windows.Forms.ColumnStyle(System.Windows.Forms.SizeType.Absolute, 96F));
            this.componentTable.ColumnStyles.Add(new System.Windows.Forms.ColumnStyle(System.Windows.Forms.SizeType.Percent, 100F));
            this.componentTable.ColumnStyles.Add(new System.Windows.Forms.ColumnStyle(System.Windows.Forms.SizeType.Absolute, 110F));
            this.componentTable.Controls.Add(this.pwsh7NameLabel, 0, 0);
            this.componentTable.Controls.Add(this.pwsh7ProgressBar, 1, 0);
            this.componentTable.Controls.Add(this.pwsh7StatusLabel, 2, 0);
            this.componentTable.Controls.Add(this.ohmyposhNameLabel, 0, 1);
            this.componentTable.Controls.Add(this.ohmyposhProgressBar, 1, 1);
            this.componentTable.Controls.Add(this.ohmyposhStatusLabel, 2, 1);
            this.componentTable.Controls.Add(this.psfzfNameLabel, 0, 2);
            this.componentTable.Controls.Add(this.psfzfProgressBar, 1, 2);
            this.componentTable.Controls.Add(this.psfzfStatusLabel, 2, 2);
            this.componentTable.Controls.Add(this.modernUnixNameLabel, 0, 3);
            this.componentTable.Controls.Add(this.modernUnixProgressBar, 1, 3);
            this.componentTable.Controls.Add(this.modernUnixStatusLabel, 2, 3);
            this.componentTable.Controls.Add(this.vscodeNameLabel, 0, 4);
            this.componentTable.Controls.Add(this.vscodeProgressBar, 1, 4);
            this.componentTable.Controls.Add(this.vscodeStatusLabel, 2, 4);
            this.componentTable.Controls.Add(this.pythonNameLabel, 0, 5);
            this.componentTable.Controls.Add(this.pythonProgressBar, 1, 5);
            this.componentTable.Controls.Add(this.pythonStatusLabel, 2, 5);
            this.componentTable.Location = new System.Drawing.Point(20, 14);
            this.componentTable.Name = "componentTable";
            this.componentTable.RowCount = 6;
            this.componentTable.RowStyles.Add(new System.Windows.Forms.RowStyle(System.Windows.Forms.SizeType.Percent, 16.66667F));
            this.componentTable.RowStyles.Add(new System.Windows.Forms.RowStyle(System.Windows.Forms.SizeType.Percent, 16.66667F));
            this.componentTable.RowStyles.Add(new System.Windows.Forms.RowStyle(System.Windows.Forms.SizeType.Percent, 16.66667F));
            this.componentTable.RowStyles.Add(new System.Windows.Forms.RowStyle(System.Windows.Forms.SizeType.Percent, 16.66667F));
            this.componentTable.RowStyles.Add(new System.Windows.Forms.RowStyle(System.Windows.Forms.SizeType.Percent, 16.66667F));
            this.componentTable.RowStyles.Add(new System.Windows.Forms.RowStyle(System.Windows.Forms.SizeType.Percent, 16.66667F));
            this.componentTable.Size = new System.Drawing.Size(680, 186);
            this.componentTable.TabIndex = 0;
            //
            // pwsh7NameLabel
            //
            this.pwsh7NameLabel.Anchor = System.Windows.Forms.AnchorStyles.Left;
            this.pwsh7NameLabel.Font = new System.Drawing.Font("Segoe UI", 9F);
            this.pwsh7NameLabel.ForeColor = System.Drawing.Color.FromArgb(50, 50, 50);
            this.pwsh7NameLabel.Location = new System.Drawing.Point(3, 0);
            this.pwsh7NameLabel.Name = "pwsh7NameLabel";
            this.pwsh7NameLabel.Size = new System.Drawing.Size(90, 31);
            this.pwsh7NameLabel.TabIndex = 0;
            this.pwsh7NameLabel.Text = "PowerShell 7";
            this.pwsh7NameLabel.TextAlign = System.Drawing.ContentAlignment.MiddleLeft;
            //
            // pwsh7ProgressBar
            //
            this.pwsh7ProgressBar.Anchor = ((System.Windows.Forms.AnchorStyles)((System.Windows.Forms.AnchorStyles.Left | System.Windows.Forms.AnchorStyles.Right)));
            this.pwsh7ProgressBar.Location = new System.Drawing.Point(99, 9);
            this.pwsh7ProgressBar.Name = "pwsh7ProgressBar";
            this.pwsh7ProgressBar.Size = new System.Drawing.Size(468, 12);
            this.pwsh7ProgressBar.Style = System.Windows.Forms.ProgressBarStyle.Continuous;
            this.pwsh7ProgressBar.TabIndex = 1;
            //
            // pwsh7StatusLabel
            //
            this.pwsh7StatusLabel.Anchor = System.Windows.Forms.AnchorStyles.Right;
            this.pwsh7StatusLabel.Font = new System.Drawing.Font("Segoe UI", 8.5F);
            this.pwsh7StatusLabel.ForeColor = System.Drawing.Color.FromArgb(160, 160, 160);
            this.pwsh7StatusLabel.Location = new System.Drawing.Point(573, 0);
            this.pwsh7StatusLabel.Name = "pwsh7StatusLabel";
            this.pwsh7StatusLabel.Size = new System.Drawing.Size(104, 31);
            this.pwsh7StatusLabel.TabIndex = 2;
            this.pwsh7StatusLabel.Text = "Waiting";
            this.pwsh7StatusLabel.TextAlign = System.Drawing.ContentAlignment.MiddleRight;
            //
            // ohmyposhNameLabel
            //
            this.ohmyposhNameLabel.Anchor = System.Windows.Forms.AnchorStyles.Left;
            this.ohmyposhNameLabel.Font = new System.Drawing.Font("Segoe UI", 9F);
            this.ohmyposhNameLabel.ForeColor = System.Drawing.Color.FromArgb(50, 50, 50);
            this.ohmyposhNameLabel.Location = new System.Drawing.Point(3, 31);
            this.ohmyposhNameLabel.Name = "ohmyposhNameLabel";
            this.ohmyposhNameLabel.Size = new System.Drawing.Size(90, 31);
            this.ohmyposhNameLabel.TabIndex = 3;
            this.ohmyposhNameLabel.Text = "Oh My Posh";
            this.ohmyposhNameLabel.TextAlign = System.Drawing.ContentAlignment.MiddleLeft;
            //
            // ohmyposhProgressBar
            //
            this.ohmyposhProgressBar.Anchor = ((System.Windows.Forms.AnchorStyles)((System.Windows.Forms.AnchorStyles.Left | System.Windows.Forms.AnchorStyles.Right)));
            this.ohmyposhProgressBar.Location = new System.Drawing.Point(99, 40);
            this.ohmyposhProgressBar.Name = "ohmyposhProgressBar";
            this.ohmyposhProgressBar.Size = new System.Drawing.Size(468, 12);
            this.ohmyposhProgressBar.Style = System.Windows.Forms.ProgressBarStyle.Continuous;
            this.ohmyposhProgressBar.TabIndex = 4;
            //
            // ohmyposhStatusLabel
            //
            this.ohmyposhStatusLabel.Anchor = System.Windows.Forms.AnchorStyles.Right;
            this.ohmyposhStatusLabel.Font = new System.Drawing.Font("Segoe UI", 8.5F);
            this.ohmyposhStatusLabel.ForeColor = System.Drawing.Color.FromArgb(160, 160, 160);
            this.ohmyposhStatusLabel.Location = new System.Drawing.Point(573, 31);
            this.ohmyposhStatusLabel.Name = "ohmyposhStatusLabel";
            this.ohmyposhStatusLabel.Size = new System.Drawing.Size(104, 31);
            this.ohmyposhStatusLabel.TabIndex = 5;
            this.ohmyposhStatusLabel.Text = "Waiting";
            this.ohmyposhStatusLabel.TextAlign = System.Drawing.ContentAlignment.MiddleRight;
            //
            // psfzfNameLabel
            //
            this.psfzfNameLabel.Anchor = System.Windows.Forms.AnchorStyles.Left;
            this.psfzfNameLabel.Font = new System.Drawing.Font("Segoe UI", 9F);
            this.psfzfNameLabel.ForeColor = System.Drawing.Color.FromArgb(50, 50, 50);
            this.psfzfNameLabel.Location = new System.Drawing.Point(3, 62);
            this.psfzfNameLabel.Name = "psfzfNameLabel";
            this.psfzfNameLabel.Size = new System.Drawing.Size(90, 31);
            this.psfzfNameLabel.TabIndex = 9;
            this.psfzfNameLabel.Text = "PSFzf";
            this.psfzfNameLabel.TextAlign = System.Drawing.ContentAlignment.MiddleLeft;
            //
            // psfzfProgressBar
            //
            this.psfzfProgressBar.Anchor = ((System.Windows.Forms.AnchorStyles)((System.Windows.Forms.AnchorStyles.Left | System.Windows.Forms.AnchorStyles.Right)));
            this.psfzfProgressBar.Location = new System.Drawing.Point(99, 71);
            this.psfzfProgressBar.Name = "psfzfProgressBar";
            this.psfzfProgressBar.Size = new System.Drawing.Size(468, 12);
            this.psfzfProgressBar.Style = System.Windows.Forms.ProgressBarStyle.Continuous;
            this.psfzfProgressBar.TabIndex = 10;
            //
            // psfzfStatusLabel
            //
            this.psfzfStatusLabel.Anchor = System.Windows.Forms.AnchorStyles.Right;
            this.psfzfStatusLabel.Font = new System.Drawing.Font("Segoe UI", 8.5F);
            this.psfzfStatusLabel.ForeColor = System.Drawing.Color.FromArgb(160, 160, 160);
            this.psfzfStatusLabel.Location = new System.Drawing.Point(573, 62);
            this.psfzfStatusLabel.Name = "psfzfStatusLabel";
            this.psfzfStatusLabel.Size = new System.Drawing.Size(104, 31);
            this.psfzfStatusLabel.TabIndex = 11;
            this.psfzfStatusLabel.Text = "Waiting";
            this.psfzfStatusLabel.TextAlign = System.Drawing.ContentAlignment.MiddleRight;
            //
            // modernUnixNameLabel
            //
            this.modernUnixNameLabel.Anchor = System.Windows.Forms.AnchorStyles.Left;
            this.modernUnixNameLabel.Font = new System.Drawing.Font("Segoe UI", 9F);
            this.modernUnixNameLabel.ForeColor = System.Drawing.Color.FromArgb(50, 50, 50);
            this.modernUnixNameLabel.Location = new System.Drawing.Point(3, 93);
            this.modernUnixNameLabel.Name = "modernUnixNameLabel";
            this.modernUnixNameLabel.Size = new System.Drawing.Size(90, 31);
            this.modernUnixNameLabel.TabIndex = 12;
            this.modernUnixNameLabel.Text = "modern-unix-win";
            this.modernUnixNameLabel.TextAlign = System.Drawing.ContentAlignment.MiddleLeft;
            //
            // modernUnixProgressBar
            //
            this.modernUnixProgressBar.Anchor = ((System.Windows.Forms.AnchorStyles)((System.Windows.Forms.AnchorStyles.Left | System.Windows.Forms.AnchorStyles.Right)));
            this.modernUnixProgressBar.Location = new System.Drawing.Point(99, 102);
            this.modernUnixProgressBar.Name = "modernUnixProgressBar";
            this.modernUnixProgressBar.Size = new System.Drawing.Size(468, 12);
            this.modernUnixProgressBar.Style = System.Windows.Forms.ProgressBarStyle.Continuous;
            this.modernUnixProgressBar.TabIndex = 13;
            //
            // modernUnixStatusLabel
            //
            this.modernUnixStatusLabel.Anchor = System.Windows.Forms.AnchorStyles.Right;
            this.modernUnixStatusLabel.Font = new System.Drawing.Font("Segoe UI", 8.5F);
            this.modernUnixStatusLabel.ForeColor = System.Drawing.Color.FromArgb(160, 160, 160);
            this.modernUnixStatusLabel.Location = new System.Drawing.Point(573, 93);
            this.modernUnixStatusLabel.Name = "modernUnixStatusLabel";
            this.modernUnixStatusLabel.Size = new System.Drawing.Size(104, 31);
            this.modernUnixStatusLabel.TabIndex = 14;
            this.modernUnixStatusLabel.Text = "Waiting";
            this.modernUnixStatusLabel.TextAlign = System.Drawing.ContentAlignment.MiddleRight;
            //
            // vscodeNameLabel
            //
            this.vscodeNameLabel.Anchor = System.Windows.Forms.AnchorStyles.Left;
            this.vscodeNameLabel.Font = new System.Drawing.Font("Segoe UI", 9F);
            this.vscodeNameLabel.ForeColor = System.Drawing.Color.FromArgb(50, 50, 50);
            this.vscodeNameLabel.Location = new System.Drawing.Point(3, 124);
            this.vscodeNameLabel.Name = "vscodeNameLabel";
            this.vscodeNameLabel.Size = new System.Drawing.Size(90, 31);
            this.vscodeNameLabel.TabIndex = 15;
            this.vscodeNameLabel.Text = "VS Code";
            this.vscodeNameLabel.TextAlign = System.Drawing.ContentAlignment.MiddleLeft;
            //
            // vscodeProgressBar
            //
            this.vscodeProgressBar.Anchor = ((System.Windows.Forms.AnchorStyles)((System.Windows.Forms.AnchorStyles.Left | System.Windows.Forms.AnchorStyles.Right)));
            this.vscodeProgressBar.Location = new System.Drawing.Point(99, 133);
            this.vscodeProgressBar.Name = "vscodeProgressBar";
            this.vscodeProgressBar.Size = new System.Drawing.Size(468, 12);
            this.vscodeProgressBar.Style = System.Windows.Forms.ProgressBarStyle.Continuous;
            this.vscodeProgressBar.TabIndex = 16;
            //
            // vscodeStatusLabel
            //
            this.vscodeStatusLabel.Anchor = System.Windows.Forms.AnchorStyles.Right;
            this.vscodeStatusLabel.Font = new System.Drawing.Font("Segoe UI", 8.5F);
            this.vscodeStatusLabel.ForeColor = System.Drawing.Color.FromArgb(160, 160, 160);
            this.vscodeStatusLabel.Location = new System.Drawing.Point(573, 124);
            this.vscodeStatusLabel.Name = "vscodeStatusLabel";
            this.vscodeStatusLabel.Size = new System.Drawing.Size(104, 31);
            this.vscodeStatusLabel.TabIndex = 17;
            this.vscodeStatusLabel.Text = "Waiting";
            this.vscodeStatusLabel.TextAlign = System.Drawing.ContentAlignment.MiddleRight;
            //
            // pythonNameLabel
            //
            this.pythonNameLabel.Anchor = System.Windows.Forms.AnchorStyles.Left;
            this.pythonNameLabel.Font = new System.Drawing.Font("Segoe UI", 9F);
            this.pythonNameLabel.ForeColor = System.Drawing.Color.FromArgb(50, 50, 50);
            this.pythonNameLabel.Location = new System.Drawing.Point(3, 155);
            this.pythonNameLabel.Name = "pythonNameLabel";
            this.pythonNameLabel.Size = new System.Drawing.Size(90, 31);
            this.pythonNameLabel.TabIndex = 18;
            this.pythonNameLabel.Text = "uv";
            this.pythonNameLabel.TextAlign = System.Drawing.ContentAlignment.MiddleLeft;
            //
            // pythonProgressBar
            //
            this.pythonProgressBar.Anchor = ((System.Windows.Forms.AnchorStyles)((System.Windows.Forms.AnchorStyles.Left | System.Windows.Forms.AnchorStyles.Right)));
            this.pythonProgressBar.Location = new System.Drawing.Point(99, 164);
            this.pythonProgressBar.Name = "pythonProgressBar";
            this.pythonProgressBar.Size = new System.Drawing.Size(468, 12);
            this.pythonProgressBar.Style = System.Windows.Forms.ProgressBarStyle.Continuous;
            this.pythonProgressBar.TabIndex = 19;
            //
            // pythonStatusLabel
            //
            this.pythonStatusLabel.Anchor = System.Windows.Forms.AnchorStyles.Right;
            this.pythonStatusLabel.Font = new System.Drawing.Font("Segoe UI", 8.5F);
            this.pythonStatusLabel.ForeColor = System.Drawing.Color.FromArgb(160, 160, 160);
            this.pythonStatusLabel.Location = new System.Drawing.Point(573, 155);
            this.pythonStatusLabel.Name = "pythonStatusLabel";
            this.pythonStatusLabel.Size = new System.Drawing.Size(104, 31);
            this.pythonStatusLabel.TabIndex = 20;
            this.pythonStatusLabel.Text = "Waiting";
            this.pythonStatusLabel.TextAlign = System.Drawing.ContentAlignment.MiddleRight;
            //
            // UpgradeForm
            //
            this.AcceptButton = this.upgradeButton;
            this.BackColor = System.Drawing.Color.White;
            this.ClientSize = new System.Drawing.Size(720, 535);
            this.Controls.Add(this.progressPanel);
            this.Controls.Add(this.separatorPanel);
            this.Controls.Add(this.settingsPanel);
            this.Controls.Add(this.headerPanel);
            this.FormBorderStyle = System.Windows.Forms.FormBorderStyle.None;
            this.MaximizeBox = false;
            this.Name = "UpgradeForm";
            this.StartPosition = System.Windows.Forms.FormStartPosition.CenterScreen;
            this.Text = "CodeNix";
            this.TopMost = true;
            this.headerPanel.ResumeLayout(false);
            this.headerPanel.PerformLayout();
            this.settingsPanel.ResumeLayout(false);
            this.progressPanel.ResumeLayout(false);
            this.progressPanel.PerformLayout();
            this.componentTable.ResumeLayout(false);
            this.ResumeLayout(false);

        }

        #endregion
        private System.Windows.Forms.Panel headerPanel;
        private System.Windows.Forms.Label label1;
        private System.Windows.Forms.Label headerSubLabel;
        private System.Windows.Forms.Label headerTitleLabel;
        private System.Windows.Forms.Panel settingsPanel;
        private System.Windows.Forms.Label summaryBodyLabel;
        private System.Windows.Forms.Label summaryTitleLabel;
        private System.Windows.Forms.Button cancelButton;
        private System.Windows.Forms.Button upgradeButton;
        private System.Windows.Forms.Panel separatorPanel;
        private System.Windows.Forms.Panel progressPanel;
        private System.Windows.Forms.TextBox activityLog;
        private System.Windows.Forms.Label logHeaderLabel;
        private System.Windows.Forms.Label currentTaskLabel;
        private System.Windows.Forms.Label elapsedTimeLabel;
        private System.Windows.Forms.Label completedLabel;
        private System.Windows.Forms.ProgressBar overallProgress;
        private System.Windows.Forms.TableLayoutPanel componentTable;
        private System.Windows.Forms.Label pwsh7NameLabel;
        private System.Windows.Forms.ProgressBar pwsh7ProgressBar;
        private System.Windows.Forms.Label pwsh7StatusLabel;
        private System.Windows.Forms.Label ohmyposhNameLabel;
        private System.Windows.Forms.ProgressBar ohmyposhProgressBar;
        private System.Windows.Forms.Label ohmyposhStatusLabel;
        private System.Windows.Forms.Label psfzfNameLabel;
        private System.Windows.Forms.ProgressBar psfzfProgressBar;
        private System.Windows.Forms.Label psfzfStatusLabel;
        private System.Windows.Forms.Label modernUnixNameLabel;
        private System.Windows.Forms.ProgressBar modernUnixProgressBar;
        private System.Windows.Forms.Label modernUnixStatusLabel;
        private System.Windows.Forms.Label vscodeNameLabel;
        private System.Windows.Forms.ProgressBar vscodeProgressBar;
        private System.Windows.Forms.Label vscodeStatusLabel;
        private System.Windows.Forms.Label pythonNameLabel;
        private System.Windows.Forms.ProgressBar pythonProgressBar;
        private System.Windows.Forms.Label pythonStatusLabel;
    }
}
