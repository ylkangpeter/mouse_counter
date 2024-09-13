using Microsoft.Win32;
using System;
using System.Diagnostics;
using System.Drawing;
using System.Runtime.InteropServices;
using System.Windows.Forms;
using System.IO;
using System.Linq;
using System.Windows.Forms.DataVisualization.Charting;

namespace MouseClickRecorder
{
    public partial class Form1 : Form
    {
        private int chartDays = 7;

        private IntPtr _mouseHookID = IntPtr.Zero;
        private IntPtr _keyboardHookID = IntPtr.Zero;
        private delegate IntPtr LowLevelProc(int nCode, IntPtr wParam, IntPtr lParam);
        private LowLevelProc _mouseProc;
        private LowLevelProc _keyboardProc;

        private DataGridView eventLogGridView;
        private NotifyIcon trayIcon;
        private ContextMenuStrip trayMenu;
        private ToolStripMenuItem startupMenuItem;

        public int keyboardPressCount = 0;
        public int mouseLeftClickCount = 0;
        public int mouseRightClickCount = 0;
        public DateTime currentDate;

        private FileManager _fileManager;
        private Timer _syncTimer;
        private const int SyncInterval = 60000; // 1 minute
        private const int SyncEventThreshold = 100; // Threshold for saving data

        private Chart eventChart;
        private bool isChartVisible = false; // Track chart visibility

        private TableLayoutPanel mainLayout;
        private Panel summaryPanel;
        private Label summaryLabel;

        public Form1()
        {
            InitializeComponent();
            this.Size = new Size(800, 800); // Increase form size to accommodate two controls

            InitializeLayout();
            InitializeDataGridView();
            InitializeChart();

            _syncTimer = new Timer
            {
                Interval = SyncInterval
            };

            _fileManager = new FileManager();
            _syncTimer.Tick += (sender, e) => _fileManager.SaveDataToFile(false, eventLogGridView);
            _syncTimer.Start();

            
            trayMenu = new ContextMenuStrip();
            trayMenu.Items.Add("Restore", null, OnRestore);

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

            _fileManager.LoadDataFromFile(this,ref currentDate);

            AddNewRow(currentDate, keyboardPressCount, mouseLeftClickCount, mouseRightClickCount);

            _mouseProc = MouseHookCallback;
            _keyboardProc = KeyboardHookCallback;
            _mouseHookID = SetHook(_mouseProc, WH_MOUSE_LL);
            _keyboardHookID = SetHook(_keyboardProc, WH_KEYBOARD_LL);

            this.Resize += Form1_Resize;
            this.FormClosing += Form1_FormClosing;

            // Load data and configure chart
            LoadDataForChart();
        }

        private void InitializeLayout()
        {
            mainLayout = new TableLayoutPanel
            {
                Dock = DockStyle.Fill,
                RowCount = 3, // 增加一行用于显示累计和
                ColumnCount = 1
            };

            mainLayout.RowStyles.Add(new RowStyle(SizeType.Absolute, 400)); // 固定高度用于表格视图
            mainLayout.RowStyles.Add(new RowStyle(SizeType.Absolute, 50)); // 固定高度用于累计和
            mainLayout.RowStyles.Add(new RowStyle(SizeType.Percent, 300)); // 图表占用剩余空间

            this.Controls.Add(mainLayout);
        }

        private void InitializeDataGridView()
        {
            eventLogGridView = new DataGridView
            {
                Dock = DockStyle.Fill,
                AllowUserToAddRows = false,
                ReadOnly = true,
                AutoSizeColumnsMode = DataGridViewAutoSizeColumnsMode.Fill
            };

            eventLogGridView.Columns.Add("Date", "Date");
            eventLogGridView.Columns.Add("KeyboardPress", "Keyboard Press");
            eventLogGridView.Columns.Add("MouseLeftClick", "Mouse Left Click");
            eventLogGridView.Columns.Add("MouseRightClick", "Mouse Right Click");

            Panel gridViewPanel = new Panel
            {
                Dock = DockStyle.Fill,
                AutoScroll = true
            };
            gridViewPanel.Controls.Add(eventLogGridView);

            mainLayout.Controls.Add(gridViewPanel, 0, 0);

            // 初始化累计和面板
            summaryPanel = new Panel
            {
                Dock = DockStyle.Fill,
                Height = 50,
                BackColor = Color.LightGray
            };

            summaryLabel = new Label
            {
                Dock = DockStyle.Fill,
                TextAlign = ContentAlignment.MiddleCenter,
                Font = new Font("Microsoft YaHei", 10, FontStyle.Bold)
            };

            summaryPanel.Controls.Add(summaryLabel);
            mainLayout.Controls.Add(summaryPanel, 0, 1);
        }

        private void InitializeChart()
        {
            eventChart = new Chart
            {
                Dock = DockStyle.Fill,
                BackColor = Color.WhiteSmoke,
                BorderlineColor = Color.LightGray,
                BorderlineDashStyle = ChartDashStyle.Solid,
                BorderlineWidth = 1
            };

            var chartArea = new ChartArea("MainArea")
            {
                BackColor = Color.White,
                BorderColor = Color.LightGray,
                BorderDashStyle = ChartDashStyle.Solid,
                BorderWidth = 1,
                ShadowColor = Color.FromArgb(64, 0, 0, 0),
                ShadowOffset = 2
            };

            chartArea.AxisX.Title = "日期";
            chartArea.AxisX.TitleFont = new Font("Microsoft YaHei", 10, FontStyle.Bold);
            chartArea.AxisX.LabelStyle.Font = new Font("Microsoft YaHei", 8);
            chartArea.AxisX.LineColor = Color.Gray;
            chartArea.AxisX.MajorGrid.LineColor = Color.LightGray;
            chartArea.AxisX.LabelStyle.Format = "MM-dd";
            chartArea.AxisX.Interval = 1;
            chartArea.AxisX.IntervalType = DateTimeIntervalType.Days;
            chartArea.CursorX.IsUserEnabled = true;
            chartArea.CursorX.IsUserSelectionEnabled = true;
            chartArea.AxisX.ScaleView.Zoomable = true;
            chartArea.AxisX.ScrollBar.IsPositionedInside = true;

            chartArea.AxisY.Title = "事件计数";
            chartArea.AxisY.TitleFont = new Font("Microsoft YaHei", 10, FontStyle.Bold);
            chartArea.AxisY.LabelStyle.Font = new Font("Microsoft YaHei", 8);
            chartArea.AxisY.LineColor = Color.Gray;
            chartArea.AxisY.MajorGrid.LineColor = Color.LightGray;

            eventChart.ChartAreas.Add(chartArea);

            var legend = new Legend("MainLegend")
            {
                Docking = Docking.Top,
                Alignment = StringAlignment.Center,
                BackColor = Color.WhiteSmoke,
                BorderColor = Color.LightGray,
                BorderDashStyle = ChartDashStyle.Solid,
                BorderWidth = 1,
                Font = new Font("Microsoft YaHei", 9),
                ShadowOffset = 1
            };
            eventChart.Legends.Add(legend);

            var seriesKeyboard = new Series("键盘按键")
            {
                ChartType = SeriesChartType.Line,
                Color = Color.RoyalBlue,
                BorderWidth = 3,
                MarkerStyle = MarkerStyle.Circle,
                MarkerSize = 8,
                MarkerColor = Color.White,
                MarkerBorderColor = Color.RoyalBlue,
                MarkerBorderWidth = 2
            };
            eventChart.Series.Add(seriesKeyboard);

            var seriesLeftClick = new Series("鼠标左键点击")
            {
                ChartType = SeriesChartType.Line,
                Color = Color.ForestGreen,
                BorderWidth = 3,
                MarkerStyle = MarkerStyle.Circle,
                MarkerSize = 8,
                MarkerColor = Color.White,
                MarkerBorderColor = Color.ForestGreen,
                MarkerBorderWidth = 2
            };
            eventChart.Series.Add(seriesLeftClick);

            var seriesRightClick = new Series("鼠标右键点击")
            {
                ChartType = SeriesChartType.Line,
                Color = Color.Crimson,
                BorderWidth = 3,
                MarkerStyle = MarkerStyle.Circle,
                MarkerSize = 8,
                MarkerColor = Color.White,
                MarkerBorderColor = Color.Crimson,
                MarkerBorderWidth = 2
            };
            eventChart.Series.Add(seriesRightClick);

            mainLayout.Controls.Add(eventChart, 0, 2);
        }

        private void Form1_Resize(object sender, EventArgs e)
        {
            if (this.WindowState == FormWindowState.Minimized)
            {
                this.Hide();
                trayIcon.Visible = true;
            }
        }

        private void OnRestore(object sender, EventArgs e)
        {
            this.Show();
            this.WindowState = FormWindowState.Normal;
            // trayIcon.Visible = false;
        }

        private void OnExit(object sender, EventArgs e)
        {
            Logger.Instance().Log("OnExit triggered");

            _fileManager.SaveDataToFile(true, eventLogGridView);

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
            Logger.Instance().Log("Exiting application");
            Logger.Instance().Dispose();
            Environment.Exit(0);
        }
        private void Form1_FormClosing(object sender, FormClosingEventArgs e)
        {
            e.Cancel = true;
            this.WindowState = FormWindowState.Minimized;
            this.Hide();
            trayIcon.Visible = true;
        }

        private IntPtr SetHook(LowLevelProc proc, int hookType)
        {
            using (Process curProcess = Process.GetCurrentProcess())
            using (ProcessModule curModule = curProcess.MainModule)
            {
                return SetWindowsHookEx(hookType, proc, GetModuleHandle(curModule.ModuleName), 0);
            }
        }

        [DllImport("user32.dll", CharSet = CharSet.Auto, SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool UnhookWindowsHookEx(IntPtr hhk);

        [DllImport("user32.dll", CharSet = CharSet.Auto, SetLastError = true)]
        private static extern IntPtr CallNextHookEx(IntPtr hhk, int nCode, IntPtr wParam, IntPtr lParam);

        [DllImport("user32.dll", CharSet = CharSet.Auto, SetLastError = true)]
        private static extern IntPtr SetWindowsHookEx(int idHook, LowLevelProc lpfn, IntPtr hMod, uint dwThreadId);

        [DllImport("kernel32.dll", CharSet = CharSet.Auto, SetLastError = true)]
        private static extern IntPtr GetModuleHandle(string lpModuleName);

        private const int WH_MOUSE_LL = 14;
        private const int WH_KEYBOARD_LL = 13;

        private IntPtr MouseHookCallback(int nCode, IntPtr wParam, IntPtr lParam)
        {
            if (nCode >= 0)
            {
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
            if (nCode >= 0 && (wParam == (IntPtr)WM_KEYDOWN || wParam == (IntPtr)WM_SYSKEYDOWN))
            {
                keyboardPressCount++;
                CheckDateAndUpdateLog();
            }

            return CallNextHookEx(IntPtr.Zero, nCode, wParam, lParam);
        }

        private void CheckDateAndUpdateLog()
        {
            DateTime now = DateTime.Now.Date;

            if (now != currentDate)
            {
                AddNewRow(currentDate, keyboardPressCount, mouseLeftClickCount, mouseRightClickCount);

                currentDate = now;
                keyboardPressCount = 0;
                mouseLeftClickCount = 0;
                mouseRightClickCount = 0;

                AddNewRow(currentDate, keyboardPressCount, mouseLeftClickCount, mouseRightClickCount);
            }
            else
            {
                AddNewRow(now, keyboardPressCount, mouseLeftClickCount, mouseRightClickCount);
            }
        }

        public void AddNewRow(DateTime date, int keyboardPress, int leftClick, int rightClick)
        {
            if (eventLogGridView.InvokeRequired)
            {
                eventLogGridView.Invoke(new Action(() =>
                {
                    AddOrUpdateRow(date, keyboardPress, leftClick, rightClick);
                }));
            }
            else
            {
                AddOrUpdateRow(date, keyboardPress, leftClick, rightClick);
            }
        }

        private void AddOrUpdateRow(DateTime date, int keyboardPress, int leftClick, int rightClick)
        {
            bool rowUpdated = false;

            foreach (DataGridViewRow row in eventLogGridView.Rows)
            {
                if (row.IsNewRow) continue;

                if (row.Cells["Date"].Value.ToString() == date.ToString("yyyy-MM-dd"))
                {
                    row.Cells["KeyboardPress"].Value = keyboardPress;
                    row.Cells["MouseLeftClick"].Value = leftClick;
                    row.Cells["MouseRightClick"].Value = rightClick;
                    rowUpdated = true;
                    break;
                }
            }

            if (!rowUpdated)
            {
                eventLogGridView.Rows.Add(date.ToString("yyyy-MM-dd"), keyboardPress, leftClick, rightClick);
                SortEventLog();
                Logger.Instance().Log($"Added new row for {date.ToString("yyyy-MM-dd")}: KeyboardPress={keyboardPress}, MouseLeftClick={leftClick}, MouseRightClick={rightClick}");
            }

            // 计算总计
            int totalKeyboardPress = 0;
            int totalMouseLeftClick = 0;
            int totalMouseRightClick = 0;

            foreach (DataGridViewRow row in eventLogGridView.Rows)
            {
                if (row.IsNewRow) continue;

                totalKeyboardPress += Convert.ToInt32(row.Cells["KeyboardPress"].Value);
                totalMouseLeftClick += Convert.ToInt32(row.Cells["MouseLeftClick"].Value);
                totalMouseRightClick += Convert.ToInt32(row.Cells["MouseRightClick"].Value);
            }  
            // 更新累计和显示
            summaryLabel.Text = $"总计 - 键盘按键: {totalKeyboardPress.ToString("N0")}, 鼠标左键点击: {totalMouseLeftClick.ToString("N0")}, 鼠标右键点击: {totalMouseRightClick.ToString("N0")}";
             
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

        private void LoadDataForChart()
        {
            // Sample data - replace with your actual data
            string[,] data = _fileManager.LoadDataForChart(chartDays);

            // for (int i = 0; i < data.GetLength(0); i++)
            // {
            //     for (int j = 0; j < data.GetLength(1); j++)
            //     {
            //         Logger.Instance().Log(data[i,j]);
            //     }
            // }

            var keyboardSeries = eventChart.Series["键盘按键"];
            var leftClickSeries = eventChart.Series["鼠标左键点击"];
            var rightClickSeries = eventChart.Series["鼠标右键点击"];

            keyboardSeries.Points.Clear();
            leftClickSeries.Points.Clear();
            rightClickSeries.Points.Clear();

            for (int i = 0; i < data.GetLength(0); i++)
            {
                if(data[i,0]==null)
                {
                    continue;
                }
                keyboardSeries.Points.AddXY(data[i,0], data[i,1]);
                leftClickSeries.Points.AddXY(data[i,0], data[i,2]);
                rightClickSeries.Points.AddXY(data[i,0], data[i,3]);
            }
        }


        #region PInvoke

        private enum MouseMessages
        {
            WM_LBUTTONDOWN = 0x0201,
            WM_RBUTTONDOWN = 0x0204,
        }

        private const int WM_KEYDOWN = 0x0100;
        private const int WM_SYSKEYDOWN = 0x0104;

        #endregion

                [STAThread]
        static void Main()
        {
            Application.EnableVisualStyles();
            Application.SetCompatibleTextRenderingDefault(false);
            Application.Run(new Form1());
        }
    }
        

}
