using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Windows.Forms;
using System.Linq;
using System.IO;

namespace SyncDAT
{
    public partial class MainForm : Form
    {
        private AppConfig _config = null!;
        private FileWatcherService _watcherService = null!;
        private DownloadSyncService _syncService = null!;
        private NotifyIcon _trayIcon = null!;
        private bool _isClosing = false;
        private Icon? _customIcon = null;
        private bool _eventsRegistered = false;

        // Sync tab controls
        private CheckBox _chkEnableAutoSync = null!;
        private NumericUpDown _numAutoSyncInterval = null!;
        private RichTextBox _txtSyncLog = null!;
        private Panel _syncTargetsPanel = null!;

        // Modern color scheme matching web dashboard
        private static readonly Color PrimaryBlue = Color.FromArgb(52, 128, 204);
        private static readonly Color DarkBlue = Color.FromArgb(41, 98, 165);
        private static readonly Color LightBlue = Color.FromArgb(230, 240, 250);
        private static readonly Color BackgroundBlue = Color.FromArgb(70, 130, 180);
        private static readonly Color CardWhite = Color.White;
        private static readonly Color TextDark = Color.FromArgb(45, 55, 72);
        private static readonly Color TextLight = Color.FromArgb(113, 128, 150);
        private static readonly Color SuccessGreen = Color.FromArgb(40, 167, 69);
        private static readonly Color WarningOrange = Color.FromArgb(255, 193, 7);
        private static readonly Color DangerRed = Color.FromArgb(220, 53, 69);

        // UI Controls
        private TextBox _txtApiKey = null!;
        private TextBox _txtApiEndpoint = null!;
        private TextBox _txtWoWBasePath = null!;
        private NumericUpDown _numUploadDelay = null!;
        private ListView _lstCharacters = null!;
        private Button _btnAddCharacter = null!;
        private Button _btnRemoveCharacter = null!;
        private Button _btnTestUpload = null!;
        private Button _btnCheckSize = null!;
        private Button _btnManualBackup = null!;
        private RichTextBox _txtLog = null!;
        private CheckBox _chkMinimizeToTray = null!;
        private CheckBox _chkEnableSizeNotifications = null!;
        private CheckBox _chkEnableAutoBackup = null!;
        private NumericUpDown _numBackupThreshold = null!;

        public MainForm()
        {
            LoadCustomIcon();
            InitializeComponent();
            LoadConfiguration();
            SetupTrayIcon();
            SetupFileWatcher();
            SetupSyncService();

            this.Load += MainForm_Load;
        }

        private void MainForm_Load(object? sender, EventArgs e)
        {
            _watcherService.StartWatching();
            _syncService.ConfigureAutoSync();
            LogMessage("Application started — watching for file changes", SuccessGreen);
            LogMessage("✅ SyncDAT v4.0 ready. Upload and Download sync active.", Color.Cyan);
        }

        private void LoadCustomIcon()
        {
            try
            {
                var assembly = System.Reflection.Assembly.GetExecutingAssembly();
                string[] resourceNames = assembly.GetManifestResourceNames();
                string? iconResourceName = resourceNames.FirstOrDefault(r => r.EndsWith(".icon.ico") || r.EndsWith("icon.ico"));

                if (!string.IsNullOrEmpty(iconResourceName))
                {
                    using (var stream = assembly.GetManifestResourceStream(iconResourceName))
                    {
                        if (stream != null)
                        {
                            _customIcon = new Icon(stream);
                            System.Diagnostics.Debug.WriteLine($"Icon loaded from embedded resource: {iconResourceName}");
                            return;
                        }
                    }
                }

                string exeDirectory = AppDomain.CurrentDomain.BaseDirectory;
                string iconPath = Path.Combine(exeDirectory, "icon.ico");
                if (File.Exists(iconPath))
                {
                    _customIcon = new Icon(iconPath);
                    System.Diagnostics.Debug.WriteLine($"Icon loaded from exe directory: {iconPath}");
                    return;
                }

                string currentDir = Directory.GetCurrentDirectory();
                iconPath = Path.Combine(currentDir, "icon.ico");
                if (File.Exists(iconPath))
                {
                    _customIcon = new Icon(iconPath);
                    System.Diagnostics.Debug.WriteLine($"Icon loaded from current directory: {iconPath}");
                    return;
                }

                try
                {
                    var exePath = System.Diagnostics.Process.GetCurrentProcess().MainModule?.FileName;
                    if (!string.IsNullOrEmpty(exePath))
                    {
                        _customIcon = Icon.ExtractAssociatedIcon(exePath);
                        if (_customIcon != null)
                        {
                            System.Diagnostics.Debug.WriteLine("Icon extracted from executable");
                            return;
                        }
                    }
                }
                catch { }

                System.Diagnostics.Debug.WriteLine("No custom icon found, will use default system icon");
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Failed to load custom icon: {ex.Message}");
            }
        }

        private void InitializeComponent()
        {
            this.Text = "Belmont Labs - SyncDAT";
            this.Size = new Size(1200, 750);
            this.MinimumSize = new Size(1200, 750);
            this.MaximumSize = new Size(1200, 750);
            this.StartPosition = FormStartPosition.CenterScreen;
            this.FormClosing += MainForm_FormClosing;
            this.Resize += MainForm_Resize;
            this.BackColor = BackgroundBlue;
            this.FormBorderStyle = FormBorderStyle.FixedSingle;
            this.MaximizeBox = false;

            if (_customIcon != null)
            {
                this.Icon = _customIcon;
            }
            else
            {
                try
                {
                    var exePath = System.Diagnostics.Process.GetCurrentProcess().MainModule?.FileName;
                    if (!string.IsNullOrEmpty(exePath))
                    {
                        var extractedIcon = Icon.ExtractAssociatedIcon(exePath);
                        if (extractedIcon != null)
                            this.Icon = extractedIcon;
                    }
                }
                catch (Exception ex)
                {
                    System.Diagnostics.Debug.WriteLine($"Could not extract icon from executable: {ex.Message}");
                }
            }

            var containerPanel = new Panel
            {
                Dock = DockStyle.Fill,
                BackColor = BackgroundBlue,
                Padding = new Padding(50, 30, 50, 30)
            };

            var cardPanel = new RoundedPanel
            {
                Dock = DockStyle.Fill,
                BackColor = CardWhite,
                Padding = new Padding(30)
            };

            var tabControl = new TabControl
            {
                Dock = DockStyle.Fill,
                Font = new Font("Segoe UI", 10F),
                Padding = new Point(12, 8)
            };

            tabControl.DrawMode = TabDrawMode.OwnerDrawFixed;
            tabControl.DrawItem += TabControl_DrawItem;

            var configTab = new TabPage("⚙️ Configuration");
            CreateConfigurationTab(configTab);
            tabControl.TabPages.Add(configTab);

            var charactersTab = new TabPage("👤 Characters");
            CreateCharactersTab(charactersTab);
            tabControl.TabPages.Add(charactersTab);

            var syncTab = new TabPage("🔄 Sync");
            CreateSyncTab(syncTab);
            tabControl.TabPages.Add(syncTab);

            var logTab = new TabPage("📋 Activity Log");
            CreateLogTab(logTab);
            tabControl.TabPages.Add(logTab);

            var aboutTab = new TabPage("ℹ️ About");
            CreateAboutTab(aboutTab);
            tabControl.TabPages.Add(aboutTab);

            cardPanel.Controls.Add(tabControl);
            containerPanel.Controls.Add(cardPanel);
            this.Controls.Add(containerPanel);
        }

        private void TabControl_DrawItem(object? sender, DrawItemEventArgs e)
        {
            TabControl tabControl = (TabControl)sender!;
            Graphics g = e.Graphics;
            TabPage tabPage = tabControl.TabPages[e.Index];

            bool isSelected = (e.Index == tabControl.SelectedIndex);
            Color tabColor = isSelected ? CardWhite : Color.FromArgb(250, 250, 250);
            using (SolidBrush brush = new SolidBrush(tabColor))
            {
                g.FillRectangle(brush, e.Bounds);
            }

            if (isSelected)
            {
                using (Pen pen = new Pen(PrimaryBlue, 3))
                {
                    g.DrawLine(pen, e.Bounds.Left, e.Bounds.Bottom - 2, e.Bounds.Right, e.Bounds.Bottom - 2);
                }
            }

            Color textColor = isSelected ? PrimaryBlue : TextLight;
            TextRenderer.DrawText(g, tabPage.Text, tabControl.Font, e.Bounds, textColor,
                TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter);
        }

        private void CreateConfigurationTab(TabPage tab)
        {
            tab.BackColor = CardWhite;
            tab.Padding = new Padding(20);

            var mainPanel = new Panel
            {
                Dock = DockStyle.Fill,
                AutoScroll = true,
                BackColor = CardWhite
            };

            int yPos = 10;

            // ── WoW Installation Card ─────────────────────────────────────────
            var wowCard = CreateInnerCard("WoW Installation", yPos, 120);
            var wowPanel = new TableLayoutPanel
            {
                Location = new Point(20, 55),
                Size = new Size(wowCard.Width - 40, 45),
                ColumnCount = 3,
                RowCount = 1
            };
            wowPanel.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 160));
            wowPanel.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 570));
            wowPanel.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 130));

            wowPanel.Controls.Add(CreateLabel("WoW Base Path:"), 0, 0);

            _txtWoWBasePath = new TextBox
            {
                Dock = DockStyle.Fill,
                Font = new Font("Consolas", 9F),
                BorderStyle = BorderStyle.FixedSingle,
                Margin = new Padding(0, 5, 5, 5),
                PlaceholderText = @"C:\World of Warcraft\_classic_era_\"
            };
            wowPanel.Controls.Add(_txtWoWBasePath, 1, 0);

            var btnBrowseWoW = CreateModernButton("📁 Browse", Color.FromArgb(108, 117, 125));
            btnBrowseWoW.Size = new Size(125, 35);
            btnBrowseWoW.Margin = new Padding(5, 5, 0, 5);
            btnBrowseWoW.Click += (s, e) =>
            {
                using var dialog = new FolderBrowserDialog
                {
                    Description = "Select your WoW installation folder (e.g. _classic_era_)",
                    UseDescriptionForTitle = true,
                    ShowNewFolderButton = false
                };
                if (!string.IsNullOrEmpty(_txtWoWBasePath.Text))
                    dialog.InitialDirectory = _txtWoWBasePath.Text;

                if (dialog.ShowDialog() == DialogResult.OK)
                    _txtWoWBasePath.Text = dialog.SelectedPath;
            };
            wowPanel.Controls.Add(btnBrowseWoW, 2, 0);

            var wowHint = new Label
            {
                Text = "The root WoW game directory. Used to pre-fill file pickers on the Characters and Sync tabs.",
                Font = new Font("Segoe UI", 8.5F),
                ForeColor = TextLight,
                Location = new Point(20, 100),
                Size = new Size(wowCard.Width - 40, 18),
                AutoSize = false
            };

            wowCard.Controls.Add(wowPanel);
            wowCard.Controls.Add(wowHint);
            mainPanel.Controls.Add(wowCard);
            yPos += wowCard.Height + 20;

            // ── API Settings Card ─────────────────────────────────────────────
            var apiCard = CreateInnerCard("API Settings", yPos, 210);
            var apiPanel = new TableLayoutPanel
            {
                Location = new Point(20, 55),
                Size = new Size(apiCard.Width - 40, 135),
                ColumnCount = 2,
                RowCount = 3
            };
            apiPanel.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 160));
            apiPanel.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 700));
            apiPanel.RowStyles.Add(new RowStyle(SizeType.Absolute, 45));
            apiPanel.RowStyles.Add(new RowStyle(SizeType.Absolute, 45));
            apiPanel.RowStyles.Add(new RowStyle(SizeType.Absolute, 45));

            apiPanel.Controls.Add(CreateLabel("API Key:"), 0, 0);
            _txtApiKey = CreateTextBox(true);
            apiPanel.Controls.Add(_txtApiKey, 1, 0);

            apiPanel.Controls.Add(CreateLabel("API Endpoint:"), 0, 1);
            _txtApiEndpoint = CreateTextBox(false);
            apiPanel.Controls.Add(_txtApiEndpoint, 1, 1);

            apiPanel.Controls.Add(CreateLabel("Upload Delay (s):"), 0, 2);
            _numUploadDelay = new NumericUpDown
            {
                Minimum = 5,
                Maximum = 300,
                Value = 60,
                Dock = DockStyle.Fill,
                Font = new Font("Segoe UI", 10F),
                Margin = new Padding(0, 5, 0, 5)
            };
            apiPanel.Controls.Add(_numUploadDelay, 1, 2);

            apiCard.Controls.Add(apiPanel);
            mainPanel.Controls.Add(apiCard);
            yPos += apiCard.Height + 20;

            // ── File Management Card ──────────────────────────────────────────
            var fileCard = CreateInnerCard("File Management", yPos, 210);
            var filePanel = new TableLayoutPanel
            {
                Location = new Point(20, 55),
                Size = new Size(fileCard.Width - 40, 135),
                ColumnCount = 2,
                RowCount = 4
            };
            filePanel.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 190));
            filePanel.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 670));
            filePanel.RowStyles.Add(new RowStyle(SizeType.Absolute, 35));
            filePanel.RowStyles.Add(new RowStyle(SizeType.Absolute, 35));
            filePanel.RowStyles.Add(new RowStyle(SizeType.Absolute, 35));
            filePanel.RowStyles.Add(new RowStyle(SizeType.Absolute, 30));

            filePanel.Controls.Add(CreateLabel("Size Notifications:"), 0, 0);
            _chkEnableSizeNotifications = new CheckBox
            {
                Text = "Alert at 2.5 MB, 5 MB, and 7 MB thresholds",
                Dock = DockStyle.Fill,
                Font = new Font("Segoe UI", 9.5F),
                ForeColor = TextDark
            };
            filePanel.Controls.Add(_chkEnableSizeNotifications, 1, 0);

            filePanel.Controls.Add(CreateLabel("Auto Backup:"), 0, 1);
            _chkEnableAutoBackup = new CheckBox
            {
                Text = "Enable automatic file backup after upload",
                Dock = DockStyle.Fill,
                Font = new Font("Segoe UI", 9.5F),
                ForeColor = TextDark
            };
            _chkEnableAutoBackup.CheckedChanged += (s, e) => _numBackupThreshold.Enabled = _chkEnableAutoBackup.Checked;
            filePanel.Controls.Add(_chkEnableAutoBackup, 1, 1);

            filePanel.Controls.Add(CreateLabel("Backup Threshold (MB):"), 0, 2);
            _numBackupThreshold = new NumericUpDown
            {
                Minimum = 1,
                Maximum = 50,
                DecimalPlaces = 1,
                Increment = 0.5M,
                Value = 5.0M,
                Dock = DockStyle.Fill,
                Font = new Font("Segoe UI", 10F),
                Enabled = false
            };
            filePanel.Controls.Add(_numBackupThreshold, 1, 2);

            var infoLabel = new Label
            {
                Text = "Files will be backed up with timestamp and a fresh file created after successful upload",
                Dock = DockStyle.Fill,
                Font = new Font("Segoe UI", 8.5F),
                ForeColor = TextLight,
                AutoSize = false,
                TextAlign = ContentAlignment.MiddleLeft
            };
            filePanel.SetColumnSpan(infoLabel, 2);
            filePanel.Controls.Add(infoLabel, 0, 3);

            fileCard.Controls.Add(filePanel);
            mainPanel.Controls.Add(fileCard);
            yPos += fileCard.Height + 20;

            // ── Application Settings Card ─────────────────────────────────────
            var appCard = CreateInnerCard("Application Settings", yPos, 100);
            var appPanel = new TableLayoutPanel
            {
                Location = new Point(20, 55),
                Size = new Size(appCard.Width - 40, 35),
                ColumnCount = 2,
                RowCount = 1
            };
            appPanel.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 190));
            appPanel.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 670));

            appPanel.Controls.Add(CreateLabel("Minimize to Tray:"), 0, 0);
            _chkMinimizeToTray = new CheckBox
            {
                Text = "Minimize to system tray instead of taskbar",
                Dock = DockStyle.Fill,
                Font = new Font("Segoe UI", 9.5F),
                ForeColor = TextDark
            };
            appPanel.Controls.Add(_chkMinimizeToTray, 1, 0);

            appCard.Controls.Add(appPanel);
            mainPanel.Controls.Add(appCard);
            yPos += appCard.Height + 20;

            // ── Save Button ───────────────────────────────────────────────────
            var btnSave = CreateModernButton("💾 Save Configuration", PrimaryBlue);
            btnSave.Location = new Point(20, yPos);
            btnSave.Size = new Size(200, 45);
            btnSave.Click += BtnSave_Click;
            mainPanel.Controls.Add(btnSave);

            tab.Controls.Add(mainPanel);
        }

        private Panel CreateInnerCard(string title, int yPos, int height)
        {
            var card = new Panel
            {
                Location = new Point(20, yPos),
                Size = new Size(920, height),
                BackColor = Color.FromArgb(248, 249, 250),
                BorderStyle = BorderStyle.None
            };

            card.Paint += (s, e) =>
            {
                var g = e.Graphics;
                using (var pen = new Pen(Color.FromArgb(226, 232, 240), 1))
                {
                    g.DrawRectangle(pen, 0, 0, card.Width - 1, card.Height - 1);
                }
            };

            var titleLabel = new Label
            {
                Text = title,
                Font = new Font("Segoe UI Semibold", 13F),
                ForeColor = TextDark,
                Location = new Point(20, 15),
                AutoSize = true
            };
            card.Controls.Add(titleLabel);

            var separator = new Panel
            {
                Location = new Point(20, 45),
                Size = new Size(card.Width - 40, 2),
                BackColor = Color.FromArgb(226, 232, 240)
            };
            card.Controls.Add(separator);

            return card;
        }

        private Label CreateLabel(string text)
        {
            return new Label
            {
                Text = text,
                TextAlign = ContentAlignment.MiddleRight,
                Dock = DockStyle.Fill,
                Font = new Font("Segoe UI", 10F),
                ForeColor = TextDark
            };
        }

        private TextBox CreateTextBox(bool isPassword)
        {
            var textBox = new TextBox
            {
                Dock = DockStyle.Fill,
                Font = new Font("Consolas", 10F),
                BorderStyle = BorderStyle.FixedSingle,
                Margin = new Padding(0, 5, 0, 5)
            };
            if (isPassword)
            {
                textBox.PasswordChar = '•';
            }
            return textBox;
        }

        private Button CreateModernButton(string text, Color backgroundColor)
        {
            var button = new Button
            {
                Text = text,
                BackColor = backgroundColor,
                ForeColor = Color.White,
                FlatStyle = FlatStyle.Flat,
                Font = new Font("Segoe UI Semibold", 10F),
                Cursor = Cursors.Hand
            };
            button.FlatAppearance.BorderSize = 0;

            Color hoverColor = ControlPaint.Dark(backgroundColor, 0.1f);
            button.MouseEnter += (s, e) => button.BackColor = hoverColor;
            button.MouseLeave += (s, e) => button.BackColor = backgroundColor;

            return button;
        }

        private void CreateCharactersTab(TabPage tab)
        {
            tab.BackColor = CardWhite;
            tab.Padding = new Padding(20);

            var mainPanel = new Panel
            {
                Dock = DockStyle.Fill,
                BackColor = Color.FromArgb(248, 249, 250),
                Padding = new Padding(20)
            };

            _lstCharacters = new ListView
            {
                Dock = DockStyle.Fill,
                View = View.Details,
                FullRowSelect = true,
                GridLines = true,
                Font = new Font("Segoe UI", 9.5F),
                BackColor = CardWhite,
                BorderStyle = BorderStyle.None
            };

            _lstCharacters.Columns.Add("Character", 180);
            _lstCharacters.Columns.Add("File Path", 480);
            _lstCharacters.Columns.Add("Last Upload", 150);
            _lstCharacters.Columns.Add("Status", 100);

            var contextMenu = new ContextMenuStrip();
            contextMenu.Items.Add("📊 Check File Size", null, (s, ev) => BtnCheckSize_Click(s, ev));
            contextMenu.Items.Add("🔼 Test Upload", null, (s, ev) => BtnTestUpload_Click(s, ev));
            contextMenu.Items.Add("💾 Backup Now", null, (s, ev) => BtnManualBackup_Click(s, ev));
            contextMenu.Items.Add(new ToolStripSeparator());
            contextMenu.Items.Add("📝 Open File in Notepad", null, (s, ev) =>
            {
                if (_lstCharacters.SelectedItems.Count > 0)
                {
                    var character = _lstCharacters.SelectedItems[0].Tag as CharacterConfig;
                    if (character != null && File.Exists(character.FilePath))
                    {
                        try
                        {
                            System.Diagnostics.Process.Start("notepad.exe", $"\"{character.FilePath}\"");
                        }
                        catch (Exception ex)
                        {
                            MessageBox.Show($"Error opening file: {ex.Message}", "Error",
                                MessageBoxButtons.OK, MessageBoxIcon.Error);
                        }
                    }
                }
            });
            contextMenu.Items.Add("📁 Open File Location", null, (s, ev) =>
            {
                if (_lstCharacters.SelectedItems.Count > 0)
                {
                    var character = _lstCharacters.SelectedItems[0].Tag as CharacterConfig;
                    if (character != null)
                    {
                        try
                        {
                            if (File.Exists(character.FilePath))
                            {
                                System.Diagnostics.Process.Start("explorer.exe", $"/select,\"{character.FilePath}\"");
                            }
                            else
                            {
                                string? dir = Path.GetDirectoryName(character.FilePath);
                                if (!string.IsNullOrEmpty(dir) && Directory.Exists(dir))
                                {
                                    System.Diagnostics.Process.Start("explorer.exe", dir);
                                }
                            }
                        }
                        catch (Exception ex)
                        {
                            MessageBox.Show($"Error opening folder: {ex.Message}", "Error",
                                MessageBoxButtons.OK, MessageBoxIcon.Error);
                        }
                    }
                }
            });
            _lstCharacters.ContextMenuStrip = contextMenu;

            var buttonPanel = new FlowLayoutPanel
            {
                Dock = DockStyle.Bottom,
                Height = 70,
                FlowDirection = FlowDirection.LeftToRight,
                Padding = new Padding(0, 15, 0, 0),
                BackColor = Color.FromArgb(248, 249, 250)
            };

            _btnAddCharacter = CreateModernButton("➕ Add Character", PrimaryBlue);
            _btnAddCharacter.Size = new Size(150, 40);
            _btnAddCharacter.Click += BtnAddCharacter_Click;
            buttonPanel.Controls.Add(_btnAddCharacter);

            buttonPanel.Controls.Add(new Panel { Width = 10 });

            _btnRemoveCharacter = CreateModernButton("➖ Remove", DangerRed);
            _btnRemoveCharacter.Size = new Size(120, 40);
            _btnRemoveCharacter.Click += BtnRemoveCharacter_Click;
            buttonPanel.Controls.Add(_btnRemoveCharacter);

            buttonPanel.Controls.Add(new Panel { Width = 10 });

            _btnTestUpload = CreateModernButton("🔼 Test Upload", SuccessGreen);
            _btnTestUpload.Size = new Size(130, 40);
            _btnTestUpload.Click += BtnTestUpload_Click;
            buttonPanel.Controls.Add(_btnTestUpload);

            buttonPanel.Controls.Add(new Panel { Width = 10 });

            _btnCheckSize = CreateModernButton("📊 Check Size", Color.FromArgb(23, 162, 184));
            _btnCheckSize.Size = new Size(120, 40);
            _btnCheckSize.Click += BtnCheckSize_Click;
            buttonPanel.Controls.Add(_btnCheckSize);

            buttonPanel.Controls.Add(new Panel { Width = 10 });

            _btnManualBackup = CreateModernButton("💾 Backup Now", WarningOrange);
            _btnManualBackup.Size = new Size(130, 40);
            _btnManualBackup.ForeColor = TextDark;
            _btnManualBackup.Click += BtnManualBackup_Click;
            buttonPanel.Controls.Add(_btnManualBackup);

            mainPanel.Controls.Add(_lstCharacters);
            mainPanel.Controls.Add(buttonPanel);
            tab.Controls.Add(mainPanel);
        }

        private void CreateLogTab(TabPage tab)
        {
            tab.BackColor = CardWhite;
            tab.Padding = new Padding(20);

            var mainPanel = new Panel
            {
                Dock = DockStyle.Fill,
                BackColor = Color.FromArgb(248, 249, 250),
                Padding = new Padding(20)
            };

            _txtLog = new RichTextBox
            {
                Dock = DockStyle.Fill,
                ReadOnly = true,
                Font = new Font("Consolas", 9.5F),
                BackColor = Color.FromArgb(30, 30, 30),
                ForeColor = Color.FromArgb(220, 220, 220),
                BorderStyle = BorderStyle.None
            };

            var clearButton = CreateModernButton("🗑️ Clear Log", Color.FromArgb(108, 117, 125));
            clearButton.Dock = DockStyle.Bottom;
            clearButton.Height = 45;
            clearButton.Click += (s, e) => _txtLog.Clear();

            mainPanel.Controls.Add(_txtLog);
            mainPanel.Controls.Add(clearButton);
            tab.Controls.Add(mainPanel);
        }

        private void CreateAboutTab(TabPage tab)
        {
            tab.BackColor = CardWhite;
            tab.Padding = new Padding(20);

            var mainPanel = new Panel
            {
                Dock = DockStyle.Fill,
                BackColor = CardWhite,
                AutoScroll = true
            };

            int yPos = 20;

            var iconLabel = new Label
            {
                Text = "~",
                Font = new Font("Segoe UI", 48F),
                ForeColor = PrimaryBlue,
                AutoSize = true,
                Location = new Point(395, yPos)
            };
            mainPanel.Controls.Add(iconLabel);
            yPos += 70;

            var titleLabel = new Label
            {
                Text = "Belmont Labs - SyncDAT",
                Font = new Font("Segoe UI", 24F, FontStyle.Bold),
                ForeColor = TextDark,
                AutoSize = true,
                Location = new Point(320, yPos)
            };
            mainPanel.Controls.Add(titleLabel);
            yPos += 40;

            var versionLabel = new Label
            {
                Text = "Version 4.0.0",
                Font = new Font("Segoe UI", 11F),
                ForeColor = TextLight,
                AutoSize = true,
                Location = new Point(410, yPos)
            };
            mainPanel.Controls.Add(versionLabel);
            yPos += 50;

            var descCard = new Panel
            {
                Location = new Point(60, yPos),
                Size = new Size(880, 400),
                BackColor = Color.FromArgb(248, 249, 250),
                BorderStyle = BorderStyle.None
            };

            descCard.Paint += (s, e) =>
            {
                var g = e.Graphics;
                using (var pen = new Pen(Color.FromArgb(226, 232, 240), 1))
                {
                    g.DrawRectangle(pen, 0, 0, descCard.Width - 1, descCard.Height - 1);
                }
            };

            var descTitleLabel = new Label
            {
                Text = "What is SyncDAT?",
                Font = new Font("Segoe UI Semibold", 13F),
                ForeColor = TextDark,
                Location = new Point(20, 15),
                AutoSize = true
            };
            descCard.Controls.Add(descTitleLabel);

            var descSeparator = new Panel
            {
                Location = new Point(20, 45),
                Size = new Size(840, 2),
                BackColor = Color.FromArgb(226, 232, 240)
            };
            descCard.Controls.Add(descSeparator);

            var descText = new Label
            {
                Text = "SyncDAT is the sync bridge between your World of Warcraft client and the WhoDASH\n" +
                       "web dashboard. It handles both directions of data flow.\n\n" +
                       "Upload (WoW -> Dashboard):\n" +
                       "  - Monitors SavedVariables for WhoDAT.lua file changes\n" +
                       "  - Automatically uploads character data to your WhoDASH server\n" +
                       "  - Provides file size alerts to prevent data loss\n" +
                       "  - Backs up large files with timestamps\n\n" +
                       "Download (Dashboard -> WoW):\n" +
                       "  - Syncs TheGrudgeDB.lua from your dashboard into the TheGrudge addon folder\n" +
                       "  - Modular design — new addon syncs can be added with zero code changes\n" +
                       "  - Manual sync or automatic schedule — your choice\n\n" +
                       "Runs quietly in the system tray while you play.",
                Font = new Font("Segoe UI", 10F),
                ForeColor = TextDark,
                Location = new Point(30, 60),
                Size = new Size(840, 320),
                AutoSize = false
            };
            descCard.Controls.Add(descText);

            mainPanel.Controls.Add(descCard);
            yPos += descCard.Height + 20;

            var websiteCard = new Panel
            {
                Location = new Point(60, yPos),
                Size = new Size(880, 140),
                BackColor = Color.FromArgb(248, 249, 250),
                BorderStyle = BorderStyle.None
            };

            websiteCard.Paint += (s, e) =>
            {
                var g = e.Graphics;
                using (var pen = new Pen(Color.FromArgb(226, 232, 240), 1))
                {
                    g.DrawRectangle(pen, 0, 0, websiteCard.Width - 1, websiteCard.Height - 1);
                }
            };

            var websiteTitleLabel = new Label
            {
                Text = "Visit Our Website",
                Font = new Font("Segoe UI Semibold", 13F),
                ForeColor = TextDark,
                Location = new Point(20, 15),
                AutoSize = true
            };
            websiteCard.Controls.Add(websiteTitleLabel);

            var websiteSeparator = new Panel
            {
                Location = new Point(20, 45),
                Size = new Size(840, 2),
                BackColor = Color.FromArgb(226, 232, 240)
            };
            websiteCard.Controls.Add(websiteSeparator);

            var websiteInfo = new Label
            {
                Text = "For more information, updates, and support, visit:",
                Font = new Font("Segoe UI", 10F),
                ForeColor = TextDark,
                Location = new Point(30, 60),
                AutoSize = true
            };
            websiteCard.Controls.Add(websiteInfo);

            var websiteLink = new LinkLabel
            {
                Text = "www.BelmontLabs.dev",
                Font = new Font("Segoe UI", 14F, FontStyle.Bold),
                LinkColor = PrimaryBlue,
                ActiveLinkColor = DarkBlue,
                VisitedLinkColor = PrimaryBlue,
                Location = new Point(30, 90),
                AutoSize = true
            };
            websiteLink.LinkClicked += (s, e) =>
            {
                try
                {
                    System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo
                    {
                        FileName = "https://www.BelmontLabs.dev",
                        UseShellExecute = true
                    });
                }
                catch (Exception ex)
                {
                    MessageBox.Show($"Could not open browser: {ex.Message}", "Error",
                        MessageBoxButtons.OK, MessageBoxIcon.Error);
                }
            };
            websiteCard.Controls.Add(websiteLink);

            var copyButton = CreateModernButton("📋 Copy URL", Color.FromArgb(108, 117, 125));
            copyButton.Size = new Size(120, 35);
            copyButton.Location = new Point(280, 85);
            copyButton.Click += (s, e) =>
            {
                try
                {
                    Clipboard.SetText("https://www.BelmontLabs.dev");
                    MessageBox.Show("Website URL copied to clipboard!", "Success",
                        MessageBoxButtons.OK, MessageBoxIcon.Information);
                }
                catch (Exception ex)
                {
                    MessageBox.Show($"Could not copy to clipboard: {ex.Message}", "Error",
                        MessageBoxButtons.OK, MessageBoxIcon.Error);
                }
            };
            websiteCard.Controls.Add(copyButton);

            mainPanel.Controls.Add(websiteCard);
            yPos += websiteCard.Height + 30;

            var creditsLabel = new Label
            {
                Text = "© 2025 Belmont Labs — BelmontLabs.dev",
                Font = new Font("Segoe UI", 9F, FontStyle.Italic),
                ForeColor = TextLight,
                AutoSize = true,
                Location = new Point(360, yPos)
            };
            mainPanel.Controls.Add(creditsLabel);

            tab.Controls.Add(mainPanel);
        }

        private void LoadConfiguration()
        {
            _config = AppConfig.Load();

            if (_txtApiKey != null)
            {
                _txtWoWBasePath.Text = _config.WoWBasePath;
                _txtApiKey.Text = _config.ApiKey;
                _txtApiEndpoint.Text = _config.ApiEndpoint;
                _numUploadDelay.Value = _config.UploadDelaySeconds;
                _chkMinimizeToTray.Checked = _config.MinimizeToTray;
                _chkEnableSizeNotifications.Checked = _config.EnableSizeNotifications;
                _chkEnableAutoBackup.Checked = _config.EnableAutoBackup;
                _numBackupThreshold.Value = (decimal)_config.BackupSizeThresholdMB;
                _numBackupThreshold.Enabled = _config.EnableAutoBackup;
                RefreshCharacterList();
                LoadSyncSettings();
            }
        }

        private void SetupTrayIcon()
        {
            Icon trayIconToUse = _customIcon;

            if (trayIconToUse == null)
            {
                try
                {
                    var exePath = System.Diagnostics.Process.GetCurrentProcess().MainModule?.FileName;
                    if (!string.IsNullOrEmpty(exePath))
                    {
                        trayIconToUse = Icon.ExtractAssociatedIcon(exePath);
                    }
                }
                catch (Exception ex)
                {
                    System.Diagnostics.Debug.WriteLine($"Could not extract icon for tray: {ex.Message}");
                }
            }

            _trayIcon = new NotifyIcon
            {
                Icon = trayIconToUse ?? SystemIcons.Application,
                Text = "Belmont Labs - SyncDAT",
                Visible = false
            };

            var trayMenu = new ContextMenuStrip();
            trayMenu.Items.Add("Show", null, (s, e) => ShowFromTray());
            trayMenu.Items.Add("Exit", null, (s, e) => ExitApplication());
            _trayIcon.ContextMenuStrip = trayMenu;
            _trayIcon.DoubleClick += (s, e) => ShowFromTray();
        }

        private void SetupFileWatcher()
        {
            _watcherService = new FileWatcherService(_config);
            RegisterFileWatcherEvents();
        }

        private void RegisterFileWatcherEvents()
        {
            if (_eventsRegistered) return;

            _watcherService.FileChanged += (s, e) =>
            {
                this.BeginInvoke(new Action(() =>
                {
                    LogMessage($"📁 File changed: {e.Character.CharacterName}", Color.Cyan);
                }));
            };

            _watcherService.UploadScheduled += (s, e) =>
            {
                this.BeginInvoke(new Action(() =>
                {
                    LogMessage($"⏰ Upload scheduled for {e.Character.CharacterName} at {e.ScheduledTime:HH:mm:ss}", Color.Gold);
                }));
            };

            _watcherService.UploadStarted += (s, e) =>
            {
                this.BeginInvoke(new Action(() =>
                {
                    LogMessage($"🔼 Uploading {e.Character.CharacterName}...", Color.White);
                }));
            };

            _watcherService.UploadCompleted += (s, e) =>
            {
                this.BeginInvoke(new Action(() =>
                {
                    LogMessage($"✅ Upload successful: {e.Character.CharacterName}", SuccessGreen);
                    RefreshCharacterList();
                    _trayIcon.ShowBalloonTip(2000, "Upload Complete",
                        $"{e.Character.CharacterName} uploaded successfully", ToolTipIcon.Info);
                }));
            };

            _watcherService.UploadError += (s, e) =>
            {
                if (this.IsHandleCreated)
                {
                    this.BeginInvoke(new Action(() =>
                    {
                        LogMessage($"❌ Upload failed: {e.Character.CharacterName} - {e.Error}", Color.OrangeRed);
                        RefreshCharacterList();
                        _trayIcon.ShowBalloonTip(5000, "Upload Failed",
                            $"{e.Character.CharacterName}: {e.Error}", ToolTipIcon.Error);
                    }));
                }
            };

            _watcherService.FileSizeWarning += (s, e) =>
            {
                this.BeginInvoke(new Action(() =>
                {
                    if (!string.IsNullOrEmpty(e.DebugInfo))
                    {
                        LogMessage($"{e.DebugInfo}", Color.LightBlue);
                    }
                    else if (e.ThresholdMB > 0)
                    {
                        string message = $"⚠️ File size warning: {e.Character.CharacterName} has reached {e.CurrentSizeMB:F2} MB (threshold: {e.ThresholdMB} MB)";
                        LogMessage(message, Color.Orange);
                        _trayIcon.ShowBalloonTip(5000, "File Size Warning",
                            $"{e.Character.CharacterName} has reached {e.CurrentSizeMB:F2} MB", ToolTipIcon.Warning);
                    }
                }));
            };

            _watcherService.BackupCompleted += (s, e) =>
            {
                this.BeginInvoke(new Action(() =>
                {
                    LogMessage($"💾 BACKUP: {e.Message}", SuccessGreen);
                    LogMessage($"  Backup created: {e.BackupPath}", Color.Cyan);
                    LogMessage($"  Original size: {e.FileSizeMB:F2} MB", Color.Cyan);
                    LogMessage($"  New blank file created: {e.Character.FilePath}", Color.Cyan);
                    _trayIcon.ShowBalloonTip(5000, "File Backed Up",
                        $"{e.Character.CharacterName} backed up successfully ({e.FileSizeMB:F2} MB)", ToolTipIcon.Info);
                }));
            };

            _eventsRegistered = true;
        }

        private void RefreshCharacterList()
        {
            _lstCharacters.Items.Clear();

            foreach (var character in _config.Characters)
            {
                var item = new ListViewItem(character.CharacterName);
                item.SubItems.Add(character.FilePath);
                item.SubItems.Add(character.LastUpload?.ToString("yyyy-MM-dd HH:mm:ss") ?? "Never");
                item.SubItems.Add(string.IsNullOrEmpty(character.LastError) ? "OK" : "Error");
                item.Tag = character;

                if (!string.IsNullOrEmpty(character.LastError))
                {
                    item.ForeColor = Color.OrangeRed;
                }

                _lstCharacters.Items.Add(item);
            }
        }

        private void BtnSave_Click(object? sender, EventArgs e)
        {
            _config.WoWBasePath = _txtWoWBasePath.Text.Trim();
            _config.ApiKey = _txtApiKey.Text.Trim();
            _config.ApiEndpoint = _txtApiEndpoint.Text.Trim();
            _config.UploadDelaySeconds = (int)_numUploadDelay.Value;
            _config.MinimizeToTray = _chkMinimizeToTray.Checked;
            _config.EnableSizeNotifications = _chkEnableSizeNotifications.Checked;
            _config.EnableAutoBackup = _chkEnableAutoBackup.Checked;
            _config.BackupSizeThresholdMB = (double)_numBackupThreshold.Value;

            try
            {
                _config.Save();
                MessageBox.Show("Configuration saved successfully.", "Success",
                    MessageBoxButtons.OK, MessageBoxIcon.Information);
                LogMessage("💾 Configuration saved", SuccessGreen);
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Error saving configuration: {ex.Message}", "Error",
                    MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
        }

        private void BtnAddCharacter_Click(object? sender, EventArgs e)
        {
            using var dialog = new OpenFileDialog
            {
                Filter = "Lua Files (*.lua)|*.lua|All Files (*.*)|*.*",
                Title = "Select WhoDAT.lua File",
                CheckFileExists = true
            };

            // Start in WTF\Account if base path is configured, otherwise let Windows decide
            string wtfPath = _config.WtfAccountPath;
            if (!string.IsNullOrEmpty(wtfPath) && Directory.Exists(wtfPath))
                dialog.InitialDirectory = wtfPath;

            if (dialog.ShowDialog() == DialogResult.OK)
            {
                string fileName = System.IO.Path.GetFileNameWithoutExtension(dialog.FileName);
                string characterName = Microsoft.VisualBasic.Interaction.InputBox(
                    "Enter a name for this character:",
                    "Character Name",
                    fileName);

                if (!string.IsNullOrWhiteSpace(characterName))
                {
                    _config.AddCharacter(characterName, dialog.FileName);
                    RefreshCharacterList();
                    _watcherService.RefreshWatchers();
                    LogMessage($"➕ Added character: {characterName}", SuccessGreen);
                }
            }
        }

        private void BtnRemoveCharacter_Click(object? sender, EventArgs e)
        {
            if (_lstCharacters.SelectedItems.Count > 0)
            {
                var character = _lstCharacters.SelectedItems[0].Tag as CharacterConfig;
                if (character == null) return;

                var result = MessageBox.Show(
                    $"Remove {character.CharacterName} from monitoring?",
                    "Confirm Removal",
                    MessageBoxButtons.YesNo,
                    MessageBoxIcon.Question);

                if (result == DialogResult.Yes)
                {
                    _config.RemoveCharacter(character);
                    RefreshCharacterList();
                    _watcherService.RefreshWatchers();
                    LogMessage($"➖ Removed character: {character.CharacterName}", Color.Gold);
                }
            }
        }

        private async void BtnTestUpload_Click(object? sender, EventArgs e)
        {
            if (_lstCharacters.SelectedItems.Count > 0)
            {
                var character = _lstCharacters.SelectedItems[0].Tag as CharacterConfig;
                if (character == null) return;

                LogMessage($"🔼 Manual upload triggered for {character.CharacterName}", Color.Cyan);
                await _watcherService.TriggerUpload(character);
            }
        }

        private async void BtnCheckSize_Click(object? sender, EventArgs e)
        {
            if (_lstCharacters.SelectedItems.Count > 0)
            {
                var character = _lstCharacters.SelectedItems[0].Tag as CharacterConfig;
                if (character == null) return;

                LogMessage($"📊 Checking file size for {character.CharacterName}...", Color.Cyan);
                string sizeInfo = await _watcherService.CheckFileSize(character);

                foreach (string line in sizeInfo.Split('\n'))
                {
                    if (!string.IsNullOrWhiteSpace(line))
                    {
                        LogMessage($"   {line}", Color.LightBlue);
                    }
                }

                ShowFileSizeDialog(character, sizeInfo);
            }
            else
            {
                MessageBox.Show("Please select a character first.", "No Selection",
                    MessageBoxButtons.OK, MessageBoxIcon.Warning);
            }
        }

        private void ShowFileSizeDialog(CharacterConfig character, string sizeInfo)
        {
            var form = new Form
            {
                Text = $"File Size Check - {character.CharacterName}",
                Size = new Size(750, 600),
                StartPosition = FormStartPosition.CenterParent,
                BackColor = BackgroundBlue,
                Icon = this.Icon,
                MinimizeBox = false,
                MaximizeBox = false,
                Padding = new Padding(30)
            };

            var cardPanel = new RoundedPanel
            {
                Dock = DockStyle.Fill,
                BackColor = CardWhite,
                Padding = new Padding(20)
            };

            var textBox = new TextBox
            {
                Multiline = true,
                ReadOnly = true,
                Dock = DockStyle.Fill,
                Font = new Font("Consolas", 9.5F),
                BackColor = Color.FromArgb(30, 30, 30),
                ForeColor = Color.FromArgb(220, 220, 220),
                BorderStyle = BorderStyle.None,
                Text = sizeInfo,
                ScrollBars = ScrollBars.Vertical,
                WordWrap = false,
                TabStop = false
            };

            var buttonPanel = new Panel
            {
                Dock = DockStyle.Bottom,
                Height = 70,
                BackColor = CardWhite,
                Padding = new Padding(0, 15, 0, 0)
            };

            var buttonFlow = new FlowLayoutPanel
            {
                Dock = DockStyle.Fill,
                FlowDirection = FlowDirection.RightToLeft,
                WrapContents = false
            };

            var okButton = CreateModernButton("OK", PrimaryBlue);
            okButton.Size = new Size(100, 40);
            okButton.Click += (s, ev) => form.Close();
            buttonFlow.Controls.Add(okButton);

            buttonFlow.Controls.Add(new Panel { Width = 10 });

            var openFolderButton = CreateModernButton("📁 Open Folder", Color.FromArgb(108, 117, 125));
            openFolderButton.Size = new Size(140, 40);
            openFolderButton.Click += (s, ev) =>
            {
                try
                {
                    if (File.Exists(character.FilePath))
                    {
                        System.Diagnostics.Process.Start("explorer.exe", $"/select,\"{character.FilePath}\"");
                    }
                    else
                    {
                        string? dir = Path.GetDirectoryName(character.FilePath);
                        if (!string.IsNullOrEmpty(dir) && Directory.Exists(dir))
                        {
                            System.Diagnostics.Process.Start("explorer.exe", dir);
                        }
                        else
                        {
                            MessageBox.Show("Cannot open folder - path does not exist.", "Error",
                                MessageBoxButtons.OK, MessageBoxIcon.Error);
                        }
                    }
                }
                catch (Exception ex)
                {
                    MessageBox.Show($"Error opening folder: {ex.Message}", "Error",
                        MessageBoxButtons.OK, MessageBoxIcon.Error);
                }
            };
            buttonFlow.Controls.Add(openFolderButton);

            buttonFlow.Controls.Add(new Panel { Width = 10 });

            var openFileButton = CreateModernButton("📝 Open in Notepad", SuccessGreen);
            openFileButton.Size = new Size(160, 40);
            openFileButton.Click += (s, ev) =>
            {
                try
                {
                    if (File.Exists(character.FilePath))
                    {
                        System.Diagnostics.Process.Start("notepad.exe", $"\"{character.FilePath}\"");
                    }
                    else
                    {
                        MessageBox.Show("File does not exist.", "Error",
                            MessageBoxButtons.OK, MessageBoxIcon.Error);
                    }
                }
                catch (Exception ex)
                {
                    MessageBox.Show($"Error opening file: {ex.Message}", "Error",
                        MessageBoxButtons.OK, MessageBoxIcon.Error);
                }
            };
            buttonFlow.Controls.Add(openFileButton);

            buttonPanel.Controls.Add(buttonFlow);

            cardPanel.Controls.Add(textBox);
            cardPanel.Controls.Add(buttonPanel);
            form.Controls.Add(cardPanel);

            form.ShowDialog(this);
        }

        private async void BtnManualBackup_Click(object? sender, EventArgs e)
        {
            if (_lstCharacters.SelectedItems.Count > 0)
            {
                var character = _lstCharacters.SelectedItems[0].Tag as CharacterConfig;
                if (character == null) return;

                var result = MessageBox.Show(
                    $"This will backup {character.CharacterName}'s file and create a new blank file.\n\n" +
                    $"The current file will be renamed with a timestamp.\n\nContinue?",
                    "Confirm Manual Backup",
                    MessageBoxButtons.YesNo,
                    MessageBoxIcon.Question);

                if (result == DialogResult.Yes)
                {
                    LogMessage($"💾 Manual backup triggered for {character.CharacterName}...", Color.Gold);
                    bool success = await _watcherService.TriggerBackup(character);

                    if (success)
                        LogMessage("✅ Manual backup completed successfully", SuccessGreen);
                    else
                        LogMessage("❌ Manual backup failed", Color.OrangeRed);
                }
            }
            else
            {
                MessageBox.Show("Please select a character first.", "No Selection",
                    MessageBoxButtons.OK, MessageBoxIcon.Warning);
            }
        }

        private void LogMessage(string message, Color color)
        {
            if (_txtLog == null || !_txtLog.IsHandleCreated) return;

            if (_txtLog.InvokeRequired)
            {
                _txtLog.Invoke(new Action(() => LogMessage(message, color)));
                return;
            }

            string timestamp = DateTime.Now.ToString("HH:mm:ss");
            _txtLog.SelectionStart = _txtLog.TextLength;
            _txtLog.SelectionLength = 0;
            _txtLog.SelectionColor = Color.FromArgb(150, 150, 150);
            _txtLog.AppendText($"[{timestamp}] ");
            _txtLog.SelectionColor = color;
            _txtLog.AppendText(message + "\n");
            _txtLog.ScrollToCaret();
        }

        private void MainForm_Resize(object? sender, EventArgs e)
        {
            if (WindowState == FormWindowState.Minimized && _config.MinimizeToTray)
            {
                Hide();
                _trayIcon.Visible = true;
                _trayIcon.ShowBalloonTip(2000, "Belmont Labs - SyncDAT",
                    "Application minimized to tray", ToolTipIcon.Info);
            }
        }

        private void ShowFromTray()
        {
            Show();
            WindowState = FormWindowState.Normal;
            _trayIcon.Visible = false;
        }

        private void MainForm_FormClosing(object? sender, FormClosingEventArgs e)
        {
            if (!_isClosing && _config.MinimizeToTray && e.CloseReason == CloseReason.UserClosing)
            {
                e.Cancel = true;
                WindowState = FormWindowState.Minimized;
            }
        }

        private void SetupSyncService()
        {
            _syncService = new DownloadSyncService(_config);

            _syncService.SyncStarted += (s, e) =>
            {
                if (!this.IsHandleCreated) return;
                this.BeginInvoke(new Action(() =>
                {
                    LogSyncMessage($"🔄 Syncing {e.Target.Name}...", Color.Cyan);
                    LogMessage($"🔄 Downloading {e.Target.Name} from dashboard...", Color.Cyan);
                }));
            };

            _syncService.SyncCompleted += (s, e) =>
            {
                if (!this.IsHandleCreated) return;
                this.BeginInvoke(new Action(() =>
                {
                    LogSyncMessage($"✅ {e.Target.Name} synced -> {e.OutputPath}", SuccessGreen);
                    LogMessage($"✅ Download complete: {e.Target.Name}", SuccessGreen);
                    RefreshSyncTargetList();
                    _trayIcon.ShowBalloonTip(2000, "Sync Complete",
                        $"{e.Target.Name} downloaded successfully", ToolTipIcon.Info);
                }));
            };

            _syncService.SyncError += (s, e) =>
            {
                if (!this.IsHandleCreated) return;
                this.BeginInvoke(new Action(() =>
                {
                    LogSyncMessage($"❌ {e.Target.Name} sync failed: {e.Error}", Color.OrangeRed);
                    LogMessage($"❌ Sync error [{e.Target.Name}]: {e.Error}", Color.OrangeRed);
                    RefreshSyncTargetList();
                    _trayIcon.ShowBalloonTip(4000, "Sync Failed",
                        $"{e.Target.Name}: {e.Error}", ToolTipIcon.Error);
                }));
            };

            _syncService.AutoSyncCycleStarted += (s, e) =>
            {
                if (!this.IsHandleCreated) return;
                this.BeginInvoke(new Action(() =>
                {
                    LogSyncMessage("⏰ Auto-sync cycle starting...", Color.Gold);
                }));
            };
        }

        private void CreateSyncTab(TabPage tab)
        {
            tab.BackColor = CardWhite;
            tab.Padding = new Padding(20);

            var mainPanel = new Panel
            {
                Dock = DockStyle.Fill,
                AutoScroll = true,
                BackColor = CardWhite
            };

            int yPos = 10;

            // ── Auto-sync schedule card ───────────────────────────────────────
            var scheduleCard = CreateInnerCard("Automatic Sync Schedule", yPos, 110);
            var schedPanel = new TableLayoutPanel
            {
                Location = new Point(20, 55),
                Size = new Size(scheduleCard.Width - 40, 35),
                ColumnCount = 4,
                RowCount = 1
            };
            schedPanel.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 160));
            schedPanel.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 300));
            schedPanel.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 160));
            schedPanel.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 200));

            schedPanel.Controls.Add(CreateLabel("Auto-Sync:"), 0, 0);

            _chkEnableAutoSync = new CheckBox
            {
                Text = "Automatically download on a schedule",
                Dock = DockStyle.Fill,
                Font = new Font("Segoe UI", 9.5F),
                ForeColor = TextDark
            };
            _chkEnableAutoSync.CheckedChanged += (s, e) =>
            {
                _numAutoSyncInterval.Enabled = _chkEnableAutoSync.Checked;
            };
            schedPanel.Controls.Add(_chkEnableAutoSync, 1, 0);

            schedPanel.Controls.Add(CreateLabel("Interval (minutes):"), 2, 0);

            _numAutoSyncInterval = new NumericUpDown
            {
                Minimum = 5,
                Maximum = 1440,
                Value = 30,
                Dock = DockStyle.Fill,
                Font = new Font("Segoe UI", 10F),
                Enabled = false
            };
            schedPanel.Controls.Add(_numAutoSyncInterval, 3, 0);

            var schedHint = new Label
            {
                Text = "Auto-sync runs after the configured interval. Sync All triggers an immediate sync of all enabled targets.",
                Font = new Font("Segoe UI", 8.5F),
                ForeColor = TextLight,
                Location = new Point(20, 92),
                Size = new Size(scheduleCard.Width - 40, 15),
                AutoSize = false
            };

            scheduleCard.Controls.Add(schedPanel);
            scheduleCard.Controls.Add(schedHint);
            mainPanel.Controls.Add(scheduleCard);
            yPos += scheduleCard.Height + 20;

            // ── Sync targets card ─────────────────────────────────────────────
            // Fixed height with AutoScroll — RefreshSyncTargetList populates rows after load
            var targetsCard = CreateInnerCard("Sync Targets (Dashboard -> WoW)", yPos, 220);

            _syncTargetsPanel = new Panel
            {
                Location = new Point(20, 55),
                Size = new Size(targetsCard.Width - 40, 150),
                AutoScroll = true,
                BackColor = Color.FromArgb(248, 249, 250)
            };

            targetsCard.Controls.Add(_syncTargetsPanel);
            mainPanel.Controls.Add(targetsCard);
            yPos += targetsCard.Height + 20;

            // ── Sync log card ─────────────────────────────────────────────────
            var logCard = CreateInnerCard("Sync Activity", yPos, 200);

            _txtSyncLog = new RichTextBox
            {
                Location = new Point(20, 55),
                Size = new Size(logCard.Width - 40, 125),
                ReadOnly = true,
                Font = new Font("Consolas", 9F),
                BackColor = Color.FromArgb(30, 30, 30),
                ForeColor = Color.FromArgb(220, 220, 220),
                BorderStyle = BorderStyle.None,
                ScrollBars = RichTextBoxScrollBars.Vertical
            };
            logCard.Controls.Add(_txtSyncLog);
            mainPanel.Controls.Add(logCard);
            yPos += logCard.Height + 20;

            // ── Action buttons ────────────────────────────────────────────────
            var buttonFlow = new FlowLayoutPanel
            {
                Location = new Point(20, yPos),
                Size = new Size(920, 55),
                FlowDirection = FlowDirection.LeftToRight,
                WrapContents = false
            };

            var btnSyncAll = CreateModernButton("🔄 Sync All Now", SuccessGreen);
            btnSyncAll.Size = new Size(160, 45);
            btnSyncAll.Click += async (s, e) =>
            {
                btnSyncAll.Enabled = false;
                btnSyncAll.Text = "⏳ Syncing...";
                try
                {
                    SaveSyncSettings();
                    await _syncService.SyncAllEnabledAsync();
                }
                finally
                {
                    btnSyncAll.Enabled = true;
                    btnSyncAll.Text = "🔄 Sync All Now";
                }
            };
            buttonFlow.Controls.Add(btnSyncAll);

            buttonFlow.Controls.Add(new Panel { Width = 10 });

            var btnSaveSyncConfig = CreateModernButton("💾 Save Sync Settings", PrimaryBlue);
            btnSaveSyncConfig.Size = new Size(200, 45);
            btnSaveSyncConfig.Click += (s, e) =>
            {
                SaveSyncSettings();
                MessageBox.Show("Sync settings saved.", "Saved", MessageBoxButtons.OK, MessageBoxIcon.Information);
                LogSyncMessage("💾 Sync settings saved.", SuccessGreen);
            };
            buttonFlow.Controls.Add(btnSaveSyncConfig);

            buttonFlow.Controls.Add(new Panel { Width = 10 });

            var btnClearSyncLog = CreateModernButton("🗑️ Clear Log", Color.FromArgb(108, 117, 125));
            btnClearSyncLog.Size = new Size(120, 45);
            btnClearSyncLog.Click += (s, e) => _txtSyncLog?.Clear();
            buttonFlow.Controls.Add(btnClearSyncLog);

            mainPanel.Controls.Add(buttonFlow);

            tab.Controls.Add(mainPanel);
        }

        private void SaveSyncSettings()
        {
            _config.EnableAutoSync = _chkEnableAutoSync.Checked;
            _config.AutoSyncIntervalMinutes = (int)_numAutoSyncInterval.Value;

            // Read per-target enabled state and output directory from the UI rows
            foreach (Control ctrl in _syncTargetsPanel.Controls)
            {
                if (ctrl.Tag is SyncTarget target && ctrl is Panel row)
                {
                    foreach (Control child in row.Controls)
                    {
                        if (child is CheckBox chk)
                            target.Enabled = chk.Checked;

                        if (child is TextBox txt && txt.Tag is string tagStr && tagStr == "outputDir")
                            target.OutputDirectory = txt.Text.Trim();
                    }
                }
            }

            _config.Save();
            _syncService.ConfigureAutoSync();
        }

        private void RefreshSyncTargetList()
        {
            if (_syncTargetsPanel == null) return;
            _syncTargetsPanel.Controls.Clear();

            int rowY = 0;
            foreach (var target in _config.SyncTargets)
            {
                // Each target occupies a two-row block: checkbox + status on top, path row below
                var row = new Panel
                {
                    Location = new Point(0, rowY),
                    Size = new Size(_syncTargetsPanel.Width - 20, 76),
                    Tag = target
                };

                // -- Row 1: enable checkbox, last-sync status, sync button --
                var chk = new CheckBox
                {
                    Text = $"{target.Name}  ({target.OutputFileName}  <-  {target.EndpointPath})",
                    Checked = target.Enabled,
                    Location = new Point(0, 6),
                    Size = new Size(500, 22),
                    Font = new Font("Segoe UI", 9.5F),
                    ForeColor = TextDark
                };

                string lastSyncText = target.LastSync.HasValue
                    ? $"Last sync: {target.LastSync:yyyy-MM-dd HH:mm:ss}"
                    : "Never synced";

                if (!string.IsNullOrEmpty(target.LastError))
                    lastSyncText += $"  [Error: {target.LastError}]";

                var statusLabel = new Label
                {
                    Text = lastSyncText,
                    Location = new Point(510, 8),
                    Size = new Size(240, 18),
                    Font = new Font("Segoe UI", 8.5F),
                    ForeColor = string.IsNullOrEmpty(target.LastError) ? TextLight : Color.OrangeRed,
                    AutoSize = false
                };

                var btnSync = CreateModernButton("↓ Sync", Color.FromArgb(23, 162, 184));
                btnSync.Size = new Size(75, 26);
                btnSync.Location = new Point(row.Width - 85, 3);
                btnSync.Anchor = AnchorStyles.Right | AnchorStyles.Top;

                var capturedTarget = target;
                btnSync.Click += async (s, e) =>
                {
                    btnSync.Enabled = false;
                    btnSync.Text = "⏳";
                    try
                    {
                        SaveSyncSettings();
                        await _syncService.SyncTargetAsync(capturedTarget);
                    }
                    finally
                    {
                        btnSync.Enabled = true;
                        btnSync.Text = "↓ Sync";
                    }
                };

                // -- Row 2: output directory path + browse button --
                var pathLabel = new Label
                {
                    Text = "Output folder:",
                    Location = new Point(0, 36),
                    Size = new Size(90, 22),
                    Font = new Font("Segoe UI", 8.5F),
                    ForeColor = TextLight,
                    TextAlign = ContentAlignment.MiddleLeft
                };

                // Seed the path: use stored value if present, otherwise derive from WoWBasePath.
                // Write the seeded value back to the target so it persists on next save.
                if (string.IsNullOrWhiteSpace(target.OutputDirectory))
                {
                    string seeded = _config.GetDefaultAddonOutputDirectory(target.Name);
                    if (!string.IsNullOrWhiteSpace(seeded))
                        target.OutputDirectory = seeded;
                }
                string initialDir = target.OutputDirectory;

                var txtOutputDir = new TextBox
                {
                    Text = initialDir,
                    Location = new Point(96, 34),
                    Size = new Size(row.Width - 210, 24),
                    Font = new Font("Consolas", 8.5F),
                    BorderStyle = BorderStyle.FixedSingle,
                    Tag = "outputDir",    // used by SaveSyncSettings to identify this control
                    Anchor = AnchorStyles.Left | AnchorStyles.Right | AnchorStyles.Top
                };

                var btnBrowseDir = CreateModernButton("📁 Browse", Color.FromArgb(108, 117, 125));
                btnBrowseDir.Size = new Size(80, 24);
                btnBrowseDir.Location = new Point(row.Width - 90, 34);
                btnBrowseDir.Font = new Font("Segoe UI", 8F);
                btnBrowseDir.Anchor = AnchorStyles.Right | AnchorStyles.Top;

                var capturedTxt = txtOutputDir;
                btnBrowseDir.Click += (s, e) =>
                {
                    using var dialog = new FolderBrowserDialog
                    {
                        Description = $"Select output folder for {target.Name}",
                        UseDescriptionForTitle = true,
                        ShowNewFolderButton = true
                    };

                    // Prefer current text, then AddOns base, then WoW base
                    if (!string.IsNullOrWhiteSpace(capturedTxt.Text) && Directory.Exists(capturedTxt.Text))
                        dialog.InitialDirectory = capturedTxt.Text;
                    else if (!string.IsNullOrEmpty(_config.AddOnsPath) && Directory.Exists(_config.AddOnsPath))
                        dialog.InitialDirectory = _config.AddOnsPath;
                    else if (!string.IsNullOrEmpty(_config.WoWBasePath) && Directory.Exists(_config.WoWBasePath))
                        dialog.InitialDirectory = _config.WoWBasePath;

                    if (dialog.ShowDialog() == DialogResult.OK)
                        capturedTxt.Text = dialog.SelectedPath;
                };

                row.Controls.Add(chk);
                row.Controls.Add(statusLabel);
                row.Controls.Add(btnSync);
                row.Controls.Add(pathLabel);
                row.Controls.Add(txtOutputDir);
                row.Controls.Add(btnBrowseDir);

                _syncTargetsPanel.Controls.Add(row);
                rowY += 82;
            }
        }

        private void LoadSyncSettings()
        {
            if (_chkEnableAutoSync != null)
            {
                _chkEnableAutoSync.Checked = _config.EnableAutoSync;
                _numAutoSyncInterval.Value = Math.Max(5, Math.Min(1440, _config.AutoSyncIntervalMinutes));
                _numAutoSyncInterval.Enabled = _config.EnableAutoSync;
                RefreshSyncTargetList();
            }
        }

        private void LogSyncMessage(string message, Color color)
        {
            if (_txtSyncLog == null || !_txtSyncLog.IsHandleCreated) return;

            if (_txtSyncLog.InvokeRequired)
            {
                _txtSyncLog.Invoke(new Action(() => LogSyncMessage(message, color)));
                return;
            }

            string timestamp = DateTime.Now.ToString("HH:mm:ss");
            _txtSyncLog.SelectionStart = _txtSyncLog.TextLength;
            _txtSyncLog.SelectionLength = 0;
            _txtSyncLog.SelectionColor = Color.FromArgb(150, 150, 150);
            _txtSyncLog.AppendText($"[{timestamp}] ");
            _txtSyncLog.SelectionColor = color;
            _txtSyncLog.AppendText(message + "\n");
            _txtSyncLog.ScrollToCaret();
        }

        private void ExitApplication()
        {
            _isClosing = true;
            _watcherService?.Dispose();
            _syncService?.Dispose();
            _trayIcon?.Dispose();
            _customIcon?.Dispose();
            Application.Exit();
        }
    }

    /// <summary>
    /// Custom panel with rounded corners
    /// </summary>
    public class RoundedPanel : Panel
    {
        public int CornerRadius { get; set; } = 20;

        protected override void OnPaint(PaintEventArgs e)
        {
            base.OnPaint(e);

            var graphics = e.Graphics;
            graphics.SmoothingMode = SmoothingMode.AntiAlias;

            using (var path = GetRoundedRectangle(this.ClientRectangle, CornerRadius))
            using (var brush = new SolidBrush(this.BackColor))
            {
                this.Region = new Region(path);
                graphics.FillPath(brush, path);
            }
        }

        private GraphicsPath GetRoundedRectangle(Rectangle rect, int radius)
        {
            var path = new GraphicsPath();
            int diameter = radius * 2;

            path.AddArc(rect.X, rect.Y, diameter, diameter, 180, 90);
            path.AddArc(rect.Right - diameter, rect.Y, diameter, diameter, 270, 90);
            path.AddArc(rect.Right - diameter, rect.Bottom - diameter, diameter, diameter, 0, 90);
            path.AddArc(rect.X, rect.Bottom - diameter, diameter, diameter, 90, 90);
            path.CloseFigure();

            return path;
        }
    }
}