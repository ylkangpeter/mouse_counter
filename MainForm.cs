using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Windows.Forms;
using Microsoft.Win32;
using System.Diagnostics;

namespace MouseClickRecorder
{
    public partial class MainForm : Form
    {
        private NotifyIcon trayIcon;
        private ContextMenuStrip trayMenu;
        private ToolStripMenuItem startupMenuItem;

        public int keyboardPressCount = 0;
        public int mouseLeftClickCount = 0;
        public int mouseRightClickCount = 0;

        private DateTime currentDate;
        private DataGridView eventLogGridView;
        private Dictionary<string, DataGridViewRow> dailyDataRows = new Dictionary<string, DataGridViewRow>();
        private Dictionary<int, int> keyDistribution = new Dictionary<int, int>();
        private Dictionary<int, bool> keyStates = new Dictionary<int, bool>();

        private IntPtr _mouseHookID = IntPtr.Zero;
        private IntPtr _keyboardHookID = IntPtr.Zero;
        private LowLevelProc _mouseProc;
        private LowLevelProc _keyboardProc;

        private FileManager _fileManager;
        private DataImporter _dataImporter;
        private ConfigManager _configManager;

        private Timer _syncTimer;

        private TableLayoutPanel mainLayout;
        private Panel summaryPanel;
        private ScottPlot.FormsPlot formsPlot;
        private Tuple<PictureBox, Label, PictureBox, Label, PictureBox, Label> summaryPicControls;
        private List<double> chartXValues;
        private List<double> chartYKeyboard;
        private List<double> chartYLeftClick;
        private List<double> chartYRightClick;
        private List<string> chartLabels;
        private ScottPlot.Plottable.Text _chartTooltipText;

        public MainForm()
        {
            InitializeComponent();
            this.Size = new Size(800, 800); // Increase form size to accommodate two controls

            _fileManager = new FileManager();
            _dataImporter = new DataImporter(_fileManager);
            _configManager = new ConfigManager();

            InitializeLayout();
            InitializeDataGridView();
            InitializeChart();

            _syncTimer = new Timer
            {
                Interval = _configManager.Config.SyncInterval
            };
            
            keyDistribution = _fileManager.LoadKeyDistribution();
            
            _syncTimer.Tick += (sender, e) => 
            {
                DateTime now = DateTime.Now.Date;
                if (now != currentDate)
                {
                    _fileManager.SaveDataToFile(false, eventLogGridView, currentDate);
                    
                    currentDate = now;
                    keyboardPressCount = 0;
                    mouseLeftClickCount = 0;
                    mouseRightClickCount = 0;
                    
                    uiUpdateNeeded = true;
                    
                    Logger.Instance().Log($"Date changed to {currentDate}, reset counters");
                }
                
                _fileManager.SaveDataToFile(false, eventLogGridView, currentDate);
                _fileManager.SaveKeyDistribution(keyDistribution);
            };
            _syncTimer.Start();

            
            trayMenu = new ContextMenuStrip();
            trayMenu.Items.Add("Restore", null, OnRestore);
            
            trayMenu.Items.Add("View Key Distribution", null, OnViewKeyDistribution);
            
            trayMenu.Items.Add("Import Historical Data", null, OnImportHistoricalData);

            startupMenuItem = new ToolStripMenuItem("Enable Startup", null, OnStartupToggle)
            {
                Checked = IsStartupEnabled("MouseClickRecorder")
            };
            trayMenu.Items.Add(startupMenuItem);
            trayMenu.Items.Add("Exit", null, OnExit);

            trayIcon = new NotifyIcon
            {
                Text = "MouseClickRecorder",
                Icon = SystemIcons.Application,
                ContextMenuStrip = trayMenu,
                Visible = true
            };

            try
            {
                string iconPath = Path.Combine(Application.StartupPath, "ico/mouse.ico");
                trayIcon.Icon = new Icon(iconPath);
            }
            catch (Exception ex)
            {
                MessageBox.Show("Error loading tray icon: " + ex.Message);
                trayIcon.Icon = SystemIcons.Application;
            }

            trayIcon.DoubleClick += OnRestore;

            currentDate = DateTime.Now.Date;

            dailyDataRows.Clear();

            refreshTimer = new System.Windows.Forms.Timer();
            refreshTimer.Interval = 1000;
            refreshTimer.Tick += RefreshTimer_Tick;
            refreshTimer.Start();

            var totalCounts = _fileManager.GetTotalCounts();
            totalKeyboardPress = totalCounts.Item1;
            totalMouseLeftClick = totalCounts.Item2;
            totalMouseRightClick = totalCounts.Item3;

            _fileManager.LoadDataFromFile(this, ref currentDate);

            uiUpdateNeeded = true;

            _mouseProc = MouseHookCallback;
            _keyboardProc = KeyboardHookCallback;
            _mouseHookID = SetHook(_mouseProc, WH_MOUSE_LL);
            _keyboardHookID = SetHook(_keyboardProc, WH_KEYBOARD_LL);

            this.Load += Form1_Load;
            this.Resize += Form1_Resize;
            this.FormClosing += Form1_FormClosing;
        }

        private void InitializeLayout()
        {
            mainLayout = new TableLayoutPanel
            {
                Dock = DockStyle.Fill,
                RowCount = 3,
                ColumnCount = 1,
                Padding = new Padding(16)
            };

            mainLayout.RowStyles.Add(new RowStyle(SizeType.Absolute, 250));
            mainLayout.RowStyles.Add(new RowStyle(SizeType.Absolute, 60));
            mainLayout.RowStyles.Add(new RowStyle(SizeType.Percent, 490));

            this.Controls.Add(mainLayout);
        }

        private void InitializeDataGridView()
        {
            eventLogGridView = new DataGridView
            {
                Dock = DockStyle.Fill,
                AllowUserToAddRows = false,
                AutoSizeColumnsMode = DataGridViewAutoSizeColumnsMode.Fill,
                SelectionMode = DataGridViewSelectionMode.FullRowSelect,
                ReadOnly = true,
                BackgroundColor = Color.White,
                BorderStyle = BorderStyle.None,
                ColumnHeadersDefaultCellStyle = new DataGridViewCellStyle
                {
                    BackColor = Color.FromArgb(0, 120, 212),
                    ForeColor = Color.White,
                    Font = new Font("Segoe UI", 10, FontStyle.Regular),
                    Alignment = DataGridViewContentAlignment.MiddleCenter
                },
                EnableHeadersVisualStyles = false,
                DefaultCellStyle = new DataGridViewCellStyle
                {
                    Font = new Font("Segoe UI", 9, FontStyle.Regular),
                    Alignment = DataGridViewContentAlignment.MiddleCenter,
                    SelectionBackColor = Color.FromArgb(230, 243, 255),
                    SelectionForeColor = Color.Black
                },
                AlternatingRowsDefaultCellStyle = new DataGridViewCellStyle
                {
                    BackColor = Color.FromArgb(248, 249, 250)
                },
                CellBorderStyle = DataGridViewCellBorderStyle.SingleHorizontal,
                GridColor = Color.FromArgb(230, 230, 230)
            };

            eventLogGridView.Columns.Add("Date", "日期");
            eventLogGridView.Columns.Add("KeyboardPress", "键盘按键");
            eventLogGridView.Columns.Add("MouseLeftClick", "鼠标左键");
            eventLogGridView.Columns.Add("MouseRightClick", "鼠标右键");

            eventLogGridView.Columns["Date"].FillWeight = 25;
            eventLogGridView.Columns["KeyboardPress"].FillWeight = 25;
            eventLogGridView.Columns["MouseLeftClick"].FillWeight = 25;
            eventLogGridView.Columns["MouseRightClick"].FillWeight = 25;

            Panel gridViewPanel = new Panel
            {
                Dock = DockStyle.Fill,
                Padding = new Padding(8),
                BackColor = Color.White
            };
            gridViewPanel.Controls.Add(eventLogGridView);

            mainLayout.Controls.Add(gridViewPanel, 0, 0);

            summaryPanel = new Panel
            {
                Dock = DockStyle.Fill,
                Height = 60,
                BackColor = Color.FromArgb(240, 247, 255),
                BorderStyle = BorderStyle.FixedSingle
            };

            Panel horizontalPanel = new Panel
            {
                Dock = DockStyle.Fill,
                AutoSize = true,
                AutoSizeMode = AutoSizeMode.GrowAndShrink
            };

            PictureBox keyboardPic = new PictureBox
            {
                Size = new Size(40, 40),
                Location = new Point(50, 10),
                SizeMode = PictureBoxSizeMode.Zoom
            };
            Label keyboardLabel = new Label
            {
                Text = "0",
                Location = new Point(100, 15),
                Font = new Font("Segoe UI", 12, FontStyle.Bold),
                ForeColor = Color.RoyalBlue
            };

            PictureBox leftPic = new PictureBox
            {
                Size = new Size(40, 40),
                Location = new Point(220, 10),
                SizeMode = PictureBoxSizeMode.Zoom
            };
            Label leftLabel = new Label
            {
                Text = "0",
                Location = new Point(270, 15),
                Font = new Font("Segoe UI", 12, FontStyle.Bold),
                ForeColor = Color.ForestGreen
            };

            PictureBox rightPic = new PictureBox
            {
                Size = new Size(40, 40),
                Location = new Point(390, 10),
                SizeMode = PictureBoxSizeMode.Zoom
            };
            Label rightLabel = new Label
            {
                Text = "0",
                Location = new Point(440, 15),
                Font = new Font("Segoe UI", 12, FontStyle.Bold),
                ForeColor = Color.Crimson
            };

            try
            {
                string iconPath = Path.Combine(Application.StartupPath, "ico");
                keyboardPic.Image = Image.FromFile(Path.Combine(iconPath, "keyboard.png"));
                leftPic.Image = Image.FromFile(Path.Combine(iconPath, "left.png"));
                rightPic.Image = Image.FromFile(Path.Combine(iconPath, "right.png"));
            }
            catch (Exception ex)
            {
                Logger.Instance().Log($"Error loading images: {ex.Message}");
            }

            horizontalPanel.Controls.Add(keyboardPic);
            horizontalPanel.Controls.Add(keyboardLabel);
            horizontalPanel.Controls.Add(leftPic);
            horizontalPanel.Controls.Add(leftLabel);
            horizontalPanel.Controls.Add(rightPic);
            horizontalPanel.Controls.Add(rightLabel);

            summaryPicControls = new Tuple<PictureBox, Label, PictureBox, Label, PictureBox, Label>(
                keyboardPic, keyboardLabel, leftPic, leftLabel, rightPic, rightLabel
            );

            summaryPanel.Controls.Add(horizontalPanel);
            mainLayout.Controls.Add(summaryPanel, 0, 1);

            typeof(Control).GetProperty("DoubleBuffered",
                System.Reflection.BindingFlags.NonPublic |
                System.Reflection.BindingFlags.Instance)
                ?.SetValue(eventLogGridView, true, null);
        }

        private RadioButton rbLastYear; 
        private RadioButton rbAllHistory;

        private void InitializeChart()
        {
            formsPlot = new ScottPlot.FormsPlot
            {
                Dock = DockStyle.Fill
            };

            Panel chartWithControlsPanel = new Panel
            {
                Dock = DockStyle.Fill
            };

            Panel topControlPanel = new Panel
            {
                Dock = DockStyle.Top,
                Height = 40,
                BackColor = Color.WhiteSmoke,
                Padding = new Padding(5, 5, 5, 5)
            };

            FlowLayoutPanel legendAndRadioPanel = new FlowLayoutPanel
            {
                Dock = DockStyle.Fill,
                FlowDirection = FlowDirection.LeftToRight,
                AutoSize = false,
                BackColor = Color.WhiteSmoke
            };

            legendAndRadioPanel.Controls.Add(new Label { Width = 40 });

            rbLastYear = new RadioButton
            {
                Text = "最近一年",
                Checked = true,
                Font = new Font("Microsoft YaHei", 9),
                Margin = new Padding(0, 3, 10, 3)
            };
            rbLastYear.CheckedChanged += (sender, e) => LoadDataForChart(formsPlot);

            rbAllHistory = new RadioButton
            {
                Text = "所有历史",
                Font = new Font("Microsoft YaHei", 9),
                Margin = new Padding(0, 3, 0, 3)
            };
            rbAllHistory.CheckedChanged += (sender, e) => LoadDataForChart(formsPlot);

            legendAndRadioPanel.Controls.Add(rbLastYear);
            legendAndRadioPanel.Controls.Add(rbAllHistory);

            topControlPanel.Controls.Add(legendAndRadioPanel);

            formsPlot.Dock = DockStyle.Fill;

            chartWithControlsPanel.Controls.Add(formsPlot);
            chartWithControlsPanel.Controls.Add(topControlPanel);

            mainLayout.Controls.Add(chartWithControlsPanel, 0, 2);

            formsPlot.MouseMove += FormsPlot_MouseMove;
            LoadDataForChart(formsPlot);
        }

        private Panel CreateLegendItem(string text, Color color)
        {
            Panel legendItem = new Panel
            {
                AutoSize = true,
                BackColor = Color.WhiteSmoke,
                Padding = new Padding(5, 5, 5, 5)
            };

            Panel colorBox = new Panel
            {
                Size = new Size(15, 15),
                BackColor = color,
                Margin = new Padding(0, 3, 5, 3)
            };

            Label label = new Label
            {
                Text = text,
                Font = new Font("Microsoft YaHei", 9),
                AutoSize = true,
                Margin = new Padding(0, 3, 0, 3)
            };

            legendItem.Controls.Add(colorBox);
            legendItem.Controls.Add(label);
            return legendItem;
        }

        private void Form1_Resize(object sender, EventArgs e)
        {
            if (this.WindowState == FormWindowState.Minimized)
            {
                this.Hide();
                trayIcon.Visible = true;
                isWindowVisible = false;
            }
            else if (this.WindowState == FormWindowState.Normal)
            {
                isWindowVisible = true;
            }
        }

        private void OnRestore(object sender, EventArgs e)
        {
            this.Show();
            this.WindowState = FormWindowState.Normal;
            isWindowVisible = true;
            // trayIcon.Visible = false;
        }

        private void OnViewKeyDistribution(object sender, EventArgs e)
        {
            KeyDistributionForm keyDistributionForm = new KeyDistributionForm(_fileManager);
            keyDistributionForm.ShowDialog();
        }

        private void OnImportHistoricalData(object sender, EventArgs e)
        {
            OpenFileDialog openFileDialog = new OpenFileDialog
            {
                Filter = "Text files (*.txt)|*.txt|All files (*.*)|*.*",
                Title = "Select Historical Data File",
                FileName = "mouse_clicker.txt"
            };

            if (openFileDialog.ShowDialog() == DialogResult.OK)
            {
                try
                {
                    this.Enabled = false;
                    
                    using (var progressForm = new Form())
                    {
                        progressForm.Text = "Importing Data";
                        progressForm.Size = new Size(400, 150);
                        progressForm.StartPosition = FormStartPosition.CenterParent;
                        progressForm.FormBorderStyle = FormBorderStyle.FixedDialog;
                        progressForm.MaximizeBox = false;
                        progressForm.MinimizeBox = false;
                        
                        var label = new Label
                        {
                            Text = "Importing historical data, please wait...",
                            Dock = DockStyle.Fill,
                            TextAlign = ContentAlignment.MiddleCenter,
                            Font = new Font("Microsoft YaHei", 10)
                        };
                        progressForm.Controls.Add(label);
                        
                        var importTask = System.Threading.Tasks.Task.Run(() =>
                        {
                            _dataImporter.ImportHistoricalData(openFileDialog.FileName);
                        });
                        
                        progressForm.Show(this);
                        
                        importTask.Wait();
                        
                        progressForm.Close();
                    }
                    
                    var totalCounts = _fileManager.GetTotalCounts();
                    totalKeyboardPress = totalCounts.Item1;
                    totalMouseLeftClick = totalCounts.Item2;
                    totalMouseRightClick = totalCounts.Item3;
                    
                    if (summaryPicControls != null)
                    {
                        summaryPicControls.Item2.Text = totalKeyboardPress.ToString("N0");
                        summaryPicControls.Item4.Text = totalMouseLeftClick.ToString("N0");
                        summaryPicControls.Item6.Text = totalMouseRightClick.ToString("N0");
                    }
                    
                    MessageBox.Show("Historical data imported successfully!", "Import Complete", 
                        MessageBoxButtons.OK, MessageBoxIcon.Information);
                    
                    _fileManager.LoadDataFromFile(this, ref currentDate);
                    LoadDataForChart(formsPlot);
                }
                catch (Exception ex)
                {
                    MessageBox.Show($"Error importing data: {ex.Message}", "Import Error", 
                        MessageBoxButtons.OK, MessageBoxIcon.Error);
                }
                finally
                {
                    this.Enabled = true;
                }
            }
        }

        private bool _isExiting = false;

        private void OnExit(object sender, EventArgs e)
        {
            Logger.Instance().Log("OnExit triggered");

            _fileManager.SaveDataToFile(true, eventLogGridView, currentDate);
            _fileManager.SaveKeyDistribution(keyDistribution);

            trayIcon.Visible = false;

            if (_mouseHookID != IntPtr.Zero)
            {
                Logger.Instance().Log("Unhooking mouse hook");
                UnhookWindowsHookEx(_mouseHookID);
            }
            if (_keyboardHookID != IntPtr.Zero)
            {
                Logger.Instance().Log("Unhooking keyboard hook");
                UnhookWindowsHookEx(_keyboardHookID);
            }

            _isExiting = true;
            Logger.Instance().Log("Exiting application");
            Application.Exit();
        }

        private void Form1_FormClosing(object sender, FormClosingEventArgs e)
        {
            if (!_isExiting)
            {
                e.Cancel = true;
                this.WindowState = FormWindowState.Minimized;
                this.Hide();
                trayIcon.Visible = true;
                isWindowVisible = false;
            }
            else
            {
                _fileManager.SaveDataToFile(true, eventLogGridView, currentDate);
                _fileManager.SaveKeyDistribution(keyDistribution);
            }
        }

        private void Form1_Load(object sender, EventArgs e)
        {
            this.WindowState = FormWindowState.Minimized;
            this.Hide();
            trayIcon.Visible = true;
        }

        private IntPtr SetHook(LowLevelProc proc, int hookType)
        {
            using (Process curProcess = Process.GetCurrentProcess())
            using (ProcessModule curModule = curProcess.MainModule)
            {
                return SetWindowsHookEx(hookType, proc,
                    GetModuleHandle(curModule.ModuleName), 0);
            }
        }

        private IntPtr MouseHookCallback(int nCode, IntPtr wParam, IntPtr lParam)
        {
            if (nCode >= 0)
            {
                EnsureCurrentDate();
                switch ((MouseMessages)wParam)
                {
                    case MouseMessages.WM_LBUTTONDOWN:
                        mouseLeftClickCount++;
                        break;
                    case MouseMessages.WM_RBUTTONDOWN:
                        mouseRightClickCount++;
                        break;
                }

                CheckDateAndUpdateLog();
            }

            return CallNextHookEx(IntPtr.Zero, nCode, wParam, lParam);
        }

        private IntPtr KeyboardHookCallback(int nCode, IntPtr wParam, IntPtr lParam)
        {
            if (nCode >= 0)
            {
                EnsureCurrentDate();
                int vkCode = Marshal.ReadInt32(lParam);
                bool isKeyDown = (wParam == (IntPtr)WM_KEYDOWN || wParam == (IntPtr)WM_SYSKEYDOWN);
                bool isKeyUp = (wParam == (IntPtr)WM_KEYUP || wParam == (IntPtr)WM_SYSKEYUP);
                
                if (isKeyDown)
                {
                    if (!keyStates.ContainsKey(vkCode) || !keyStates[vkCode])
                    {
                        keyboardPressCount++;
                        
                        if (keyDistribution.ContainsKey(vkCode))
                        {
                            keyDistribution[vkCode]++;
                        }
                        else
                        {
                            keyDistribution[vkCode] = 1;
                        }
                        
                        CheckDateAndUpdateLog();
                        keyStates[vkCode] = true;
                    }
                }
                else if (isKeyUp)
                {
                    keyStates[vkCode] = false;
                }
            }

            return CallNextHookEx(IntPtr.Zero, nCode, wParam, lParam);
        }

        private void CheckDateAndUpdateLog()
        {
            uiUpdateNeeded = true;
            
        }

        private void EnsureCurrentDate()
        {
            DateTime now = DateTime.Now.Date;
            if (now == currentDate)
            {
                return;
            }

            _fileManager.SaveDataToFile(false, eventLogGridView, currentDate);

            currentDate = now;
            keyboardPressCount = 0;
            mouseLeftClickCount = 0;
            mouseRightClickCount = 0;
            uiUpdateNeeded = true;

            Logger.Instance().Log($"Date changed to {currentDate}, reset counters");
        }


        public void AddNewRow(DateTime date, int keyboardPress, int leftClick, int rightClick)
        {
            if (eventLogGridView.InvokeRequired)
            {
                eventLogGridView.Invoke(new Action(() =>
                {
                    AddOrUpdateRow(date, keyboardPress, leftClick, rightClick, false);
                }));
            }
            else
            {
                AddOrUpdateRow(date, keyboardPress, leftClick, rightClick, false);
            }
        }

        public void SetCurrentDayData(int keyboardPress, int leftClick, int rightClick)
        {
            keyboardPressCount = keyboardPress;
            mouseLeftClickCount = leftClick;
            mouseRightClickCount = rightClick;
            AddOrUpdateRow(currentDate, keyboardPress, leftClick, rightClick, false);
        }


        private int totalKeyboardPress = 0;
        private int totalMouseLeftClick = 0;
        private int totalMouseRightClick = 0;
        
        private bool uiUpdateNeeded = false;
        
        private bool isWindowVisible = true;
        
        private System.Windows.Forms.Timer refreshTimer;
        
        private DateTime lastMouseMoveTime = DateTime.MinValue;
        private const int MouseMoveIntervalMs = 100;
        private int lastTooltipIndex = -1;

        private void AddOrUpdateRow(DateTime date, int keyboardPress, int leftClick, int rightClick, bool updateTotal = true)
        {
            string dateStr = date.ToString("yyyy-MM-dd");

            if (dailyDataRows.ContainsKey(dateStr))
            {
                DataGridViewRow row = dailyDataRows[dateStr];
                
                int oldKeyboardPress = Convert.ToInt32(row.Cells["KeyboardPress"].Value);
                int oldLeftClick = Convert.ToInt32(row.Cells["MouseLeftClick"].Value);
                int oldRightClick = Convert.ToInt32(row.Cells["MouseRightClick"].Value);
                
                if (updateTotal)
                {
                    totalKeyboardPress += (keyboardPress - oldKeyboardPress);
                    totalMouseLeftClick += (leftClick - oldLeftClick);
                    totalMouseRightClick += (rightClick - oldRightClick);
                }
                
                row.Cells["KeyboardPress"].Value = keyboardPress;
                row.Cells["MouseLeftClick"].Value = leftClick;
                row.Cells["MouseRightClick"].Value = rightClick;
            }
            else
            {
                eventLogGridView.SuspendLayout();
                int rowIndex = eventLogGridView.Rows.Add(dateStr, keyboardPress, leftClick, rightClick);
                DataGridViewRow newRow = eventLogGridView.Rows[rowIndex];
                dailyDataRows[dateStr] = newRow;
                
                if (updateTotal)
                {
                    totalKeyboardPress += keyboardPress;
                    totalMouseLeftClick += leftClick;
                    totalMouseRightClick += rightClick;
                }
                
                if (dailyDataRows.Count > _configManager.Config.MaxDaysToShow)
                {
                    var oldestDate = dailyDataRows.Keys.OrderBy(d => d).First();
                    if (dailyDataRows.TryGetValue(oldestDate, out DataGridViewRow oldRow))
                    {
                        eventLogGridView.Rows.Remove(oldRow);
                        dailyDataRows.Remove(oldestDate);
                    }
                }
                
                SortEventLog();
                
                eventLogGridView.ResumeLayout(false);
                
                Logger.Instance().Log($"Added new row for {dateStr}: KeyboardPress={keyboardPress}, MouseLeftClick={leftClick}, MouseRightClick={rightClick}");
            }

            if (summaryPicControls != null && isWindowVisible)
            {
                summaryPicControls.Item2.Text = totalKeyboardPress.ToString("N0");
                summaryPicControls.Item4.Text = totalMouseLeftClick.ToString("N0");
                summaryPicControls.Item6.Text = totalMouseRightClick.ToString("N0");
            }
        }

        private void RefreshTimer_Tick(object sender, EventArgs e)
        {
            if (!isWindowVisible)
            {
                return;
            }
            
            if (uiUpdateNeeded)
            {
                uiUpdateNeeded = false;
                
                this.BeginInvoke(new Action(() => {
                    eventLogGridView.SuspendLayout();
                    AddOrUpdateRow(currentDate, keyboardPressCount, mouseLeftClickCount, mouseRightClickCount);
                    eventLogGridView.ResumeLayout(false);
                }));
            }
        }

        private void SortEventLog()
        {
            eventLogGridView.Sort(eventLogGridView.Columns["Date"], System.ComponentModel.ListSortDirection.Descending);
        }


        private void OnStartupToggle(object sender, EventArgs e)
        {
            ToolStripMenuItem item = (ToolStripMenuItem)sender;
            bool enabled = !item.Checked;

            SetStartup(enabled);
            item.Checked = enabled;
        }

        private void SetStartup(bool enabled)
        {
            string appPath = Application.ExecutablePath;
            string keyName = @"Software\Microsoft\Windows\CurrentVersion\Run";

            using (RegistryKey key = Registry.CurrentUser.OpenSubKey(keyName, true))
            {
                if (enabled)
                {
                    key.SetValue("MouseClickRecorder", appPath);
                }
                else
                {
                    key.DeleteValue("MouseClickRecorder", false);
                }
            }
        }

        private bool IsStartupEnabled(string appName)
        {
            string keyName = @"Software\Microsoft\Windows\CurrentVersion\Run";
            using (RegistryKey key = Registry.CurrentUser.OpenSubKey(keyName))
            {
                return key.GetValue(appName) != null;
            }
        }

        private void LoadDataForChart(ScottPlot.FormsPlot formsPlot)
        {
            string[,] data;

            if (rbLastYear.Checked)
            {
                data = (_fileManager as FileManager).LoadLastYearDataByWeek();
            }
            else
            {
                data = (_fileManager as FileManager).LoadDataForChartByWeek();
            }

            formsPlot.Plot.Clear();

            List<double> x = new List<double>();
            List<double> yKeyboard = new List<double>();
            List<double> yLeftClick = new List<double>();
            List<double> yRightClick = new List<double>();
            List<string> labels = new List<string>();

            for (int i = 0; i < data.GetLength(0); i++)
            {
                if(data[i,0]==null)
                {
                    continue;
                }
                x.Add(i);
                yKeyboard.Add(double.Parse(data[i,1]));
                yLeftClick.Add(double.Parse(data[i,2]));
                yRightClick.Add(double.Parse(data[i,3]));
                
                string weekStr = data[i,0];
                if (weekStr.Length >= 6)
                {
                    string year = weekStr.Substring(0, 4);
                    string week = weekStr.Substring(5);
                    labels.Add($"{year}-W{week}");
                }
                else
                {
                    labels.Add(weekStr);
                }
            }

            chartXValues = x;
            chartYKeyboard = yKeyboard;
            chartYLeftClick = yLeftClick;
            chartYRightClick = yRightClick;
            chartLabels = labels;

            if (x.Count > 0)
            {
                List<double> xTickPositions = new List<double>();
                List<string> xTickLabels = new List<string>();
                
                int step = Math.Max(1, x.Count / 10);
                for (int i = 0; i < x.Count; i += step)
                {
                    xTickPositions.Add(x[i]);
                    xTickLabels.Add(labels[i]);
                }
                if (x.Count > 0 && xTickPositions.Last() != x.Last())
                {
                    xTickPositions.Add(x.Last());
                    xTickLabels.Add(labels.Last());
                }

                formsPlot.Plot.AddScatter(x.ToArray(), yKeyboard.ToArray(), Color.RoyalBlue, 2, label: "Keyboard");
                formsPlot.Plot.AddScatter(x.ToArray(), yLeftClick.ToArray(), Color.ForestGreen, 2, label: "Left Click");
                formsPlot.Plot.AddScatter(x.ToArray(), yRightClick.ToArray(), Color.Crimson, 2, label: "Right Click");

                formsPlot.Plot.Style(ScottPlot.Style.Default);
                formsPlot.Plot.XAxis.Grid(true);
                formsPlot.Plot.YAxis.Grid(true);
                formsPlot.Plot.XAxis2.Ticks(false);
                formsPlot.Plot.YAxis2.Ticks(false);
                formsPlot.Plot.XAxis.TickLabelStyle(fontName: "Segoe UI");
                formsPlot.Plot.YAxis.TickLabelStyle(fontName: "Segoe UI");


                formsPlot.Plot.XTicks(xTickPositions.ToArray(), xTickLabels.ToArray());
                formsPlot.Plot.XAxis.TickLabelStyle(rotation: 45, fontName: "Segoe UI");


                formsPlot.Refresh();
            }
        }

        private void FormsPlot_MouseMove(object sender, MouseEventArgs e)
        {
            if (!isWindowVisible)
            {
                return;
            }

            DateTime now = DateTime.Now;
            if ((now - lastMouseMoveTime).TotalMilliseconds < MouseMoveIntervalMs)
            {
                return;
            }
            lastMouseMoveTime = now;

            if (chartXValues == null || chartXValues.Count == 0)
            {
                return;
            }

            double mouseX = formsPlot.Plot.GetCoordinateX(e.Location.X);
            int index = (int)Math.Round(mouseX);
            if (index < 0 || index >= chartXValues.Count)
            {
                return;
            }

            if (index == lastTooltipIndex && _chartTooltipText != null)
            {
                return;
            }
            lastTooltipIndex = index;

            string dateLabel = chartLabels[index];
            string keyboard = ((int)chartYKeyboard[index]).ToString("N0");
            string leftClick = ((int)chartYLeftClick[index]).ToString("N0");
            string rightClick = ((int)chartYRightClick[index]).ToString("N0");
            string tooltip = $"{dateLabel}\nKeyboard: {keyboard}\nLeft: {leftClick}\nRight: {rightClick}";

            if (_chartTooltipText != null)
            {
                formsPlot.Plot.Remove(_chartTooltipText);
            }

            double centerX = (chartXValues[chartXValues.Count - 1] - chartXValues[0]) / 2 + chartXValues[0];
            double maxY = Math.Max(
                chartYKeyboard.Max(),
                Math.Max(chartYLeftClick.Max(), chartYRightClick.Max())
            );
            double chartTopY = maxY * 0.9;

            _chartTooltipText = formsPlot.Plot.AddText(tooltip, centerX, chartTopY);
            _chartTooltipText.Font.Size = 14;
            _chartTooltipText.Font.Name = "Segoe UI";
            _chartTooltipText.Font.Color = Color.Black;
            _chartTooltipText.BackgroundColor = Color.FromArgb(220, 255, 255, 255);

            formsPlot.Refresh();
        }

        #region PInvoke

        private delegate IntPtr LowLevelProc(int nCode, IntPtr wParam, IntPtr lParam);

        private const int WH_MOUSE_LL = 14;
        private const int WH_KEYBOARD_LL = 13;

        private enum MouseMessages
        {
            WM_LBUTTONDOWN = 0x0201,
            WM_RBUTTONDOWN = 0x0204,
        }

        private const int WM_KEYDOWN = 0x0100;
        private const int WM_SYSKEYDOWN = 0x0104;
        private const int WM_KEYUP = 0x0101;
        private const int WM_SYSKEYUP = 0x0105;

        [System.Runtime.InteropServices.DllImport("user32.dll", CharSet = System.Runtime.InteropServices.CharSet.Auto, SetLastError = true)]
        private static extern IntPtr SetWindowsHookEx(int idHook, LowLevelProc lpfn, IntPtr hMod, uint dwThreadId);

        [System.Runtime.InteropServices.DllImport("user32.dll", CharSet = System.Runtime.InteropServices.CharSet.Auto, SetLastError = true)]
        [return: System.Runtime.InteropServices.MarshalAs(System.Runtime.InteropServices.UnmanagedType.Bool)]
        private static extern bool UnhookWindowsHookEx(IntPtr hhk);

        [System.Runtime.InteropServices.DllImport("user32.dll", CharSet = System.Runtime.InteropServices.CharSet.Auto, SetLastError = true)]
        private static extern IntPtr CallNextHookEx(IntPtr hhk, int nCode, IntPtr wParam, IntPtr lParam);

        [System.Runtime.InteropServices.DllImport("kernel32.dll", CharSet = System.Runtime.InteropServices.CharSet.Auto, SetLastError = true)]
        private static extern IntPtr GetModuleHandle(string lpModuleName);

        #endregion
    }
}
