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

        public Form1()
        {
            InitializeComponent();
            this.Size = new Size(600, 500);

            InitializeDataGridView();

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
            trayMenu.Items.Add("Show/Hide Chart", null, OnToggleChart);
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

            InitializeChart();

            // Load data and configure chart
            LoadDataForChart();
        }

        private void InitializeDataGridView()
        {
            eventLogGridView = new DataGridView
            {
                Dock = DockStyle.Fill,
                AllowUserToAddRows = false,
                ReadOnly = true
            };

            eventLogGridView.Columns.Add("Date", "Date");
            eventLogGridView.Columns.Add("KeyboardPress", "Keyboard Press");
            eventLogGridView.Columns.Add("MouseLeftClick", "Mouse Left Click");
            eventLogGridView.Columns.Add("MouseRightClick", "Mouse Right Click");

            this.Controls.Add(eventLogGridView);
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
                    // Log($"Updated row for {date.ToString("yyyy-MM-dd")}: KeyboardPress={keyboardPress}, MouseLeftClick={leftClick}, MouseRightClick={rightClick}");
                    break;
                }
            }

            if (!rowUpdated)
            {
                eventLogGridView.Rows.Add(date.ToString("yyyy-MM-dd"), keyboardPress, leftClick, rightClick);
                SortEventLog();
                Logger.Instance().Log($"Added new row for {date.ToString("yyyy-MM-dd")}: KeyboardPress={keyboardPress}, MouseLeftClick={leftClick}, MouseRightClick={rightClick}");
            }

            // Check the row count and last row data
            // Logger.Instance().Log($"Row count: {eventLogGridView.Rows.Count}");
            if (eventLogGridView.Rows.Count > 0)
            {
                var lastRow = eventLogGridView.Rows[eventLogGridView.Rows.Count - 1];
                // Logger.Instance().Log($"Last row: {lastRow.Cells["Date"].Value}, {lastRow.Cells["KeyboardPress"].Value}, {lastRow.Cells["MouseLeftClick"].Value}, {lastRow.Cells["MouseRightClick"].Value}");
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


        private void InitializeChart()
        {
            eventChart = new Chart
            {
                Dock = DockStyle.Fill,
                Visible = isChartVisible
            };

            var chartArea = new ChartArea("MainArea")
            {
                AxisX = { Title = "Date" },
                AxisY = { Title = "Event Count" }
            };
            eventChart.ChartAreas.Add(chartArea);

            var seriesKeyboard = new Series("Keyboard Presses")
            {
                ChartType = SeriesChartType.Line,
                Color = Color.Blue,
                BorderWidth = 3
            };
            eventChart.Series.Add(seriesKeyboard);

            var seriesLeftClick = new Series("Mouse Left Clicks")
            {
                ChartType = SeriesChartType.Line,
                Color = Color.Green,
                BorderWidth = 3
            };
            eventChart.Series.Add(seriesLeftClick);

            var seriesRightClick = new Series("Mouse Right Clicks")
            {
                ChartType = SeriesChartType.Line,
                Color = Color.Red,
                BorderWidth = 3
            };
            eventChart.Series.Add(seriesRightClick);

            this.Controls.Add(eventChart);
            eventChart.BringToFront(); // Ensure chart is on top
        }

        private void OnToggleChart(object sender, EventArgs e)
        {
            isChartVisible = !isChartVisible;
            eventChart.Visible = isChartVisible;
            if (isChartVisible)
            {
                eventChart.BringToFront(); // Ensure chart is visible
                this.Show(); // Show the form if it's hidden
                this.WindowState = FormWindowState.Normal;
            }
            else
            {
                this.Hide(); // Hide the form if chart is hidden
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

            var keyboardSeries = eventChart.Series["Keyboard Presses"];
            var leftClickSeries = eventChart.Series["Mouse Left Clicks"];
            var rightClickSeries = eventChart.Series["Mouse Right Clicks"];

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
