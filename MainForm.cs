using Microsoft.Win32;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.Runtime.InteropServices;
using System.Windows.Forms;
using System.IO;
using System.Linq;
using System.Data.SQLite;
using ScottPlot;

namespace MouseClickRecorder
{
    public partial class MainForm : Form
    {
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
        
        // 用于跟踪键盘按键状态，避免重复计数
        private System.Collections.Generic.Dictionary<int, bool> keyStates = new System.Collections.Generic.Dictionary<int, bool>();
        
        // 用于跟踪事件数量，达到阈值时保存数据

        
        // 用于统计按键分布
        private System.Collections.Generic.Dictionary<int, int> keyDistribution = new System.Collections.Generic.Dictionary<int, int>();

        private FileManager _fileManager;
        private DataImporter _dataImporter;
        private Timer _syncTimer;
        private const int SyncInterval = 10000; // 10 seconds
        private const int SyncEventThreshold = 50; // Threshold for saving data

        private TableLayoutPanel mainLayout;
        private Panel summaryPanel;
        private ScottPlot.FormsPlot formsPlot;
        private Tuple<PictureBox, Label, PictureBox, Label, PictureBox, Label> summaryPicControls;
        private List<double> chartXValues;
        private List<double> chartYKeyboard;
        private List<double> chartYLeftClick;
        private List<double> chartYRightClick;
        private List<string> chartLabels;

        public MainForm()
        {
            InitializeComponent();
            this.Size = new Size(800, 800); // Increase form size to accommodate two controls

            // 先初始化 FileManager，因为图表加载需要它
            _fileManager = new FileManager();
            _dataImporter = new DataImporter(_fileManager);

            InitializeLayout();
            InitializeDataGridView();
            InitializeChart();

            _syncTimer = new Timer
            {
                Interval = SyncInterval
            };
            
            // 加载按键分布数据
            keyDistribution = _fileManager.LoadKeyDistribution();
            
            _syncTimer.Tick += (sender, e) => 
            {
                _fileManager.SaveDataToFile(false, eventLogGridView);
                _fileManager.SaveKeyDistribution(keyDistribution);
            };
            _syncTimer.Start();

            
            trayMenu = new ContextMenuStrip();
            trayMenu.Items.Add("Restore", null, OnRestore);
            
            // 添加查看按键分布的菜单项
            trayMenu.Items.Add("View Key Distribution", null, OnViewKeyDistribution);
            
            // 添加导入历史数据的菜单项
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

            // 初始化数据字典
            dailyDataRows.Clear();
            totalKeyboardPress = 0;
            totalMouseLeftClick = 0;
            totalMouseRightClick = 0;
            
            // 初始化刷新定时器
            refreshTimer = new System.Windows.Forms.Timer();
            refreshTimer.Interval = 1000; // 1秒
            refreshTimer.Tick += RefreshTimer_Tick;
            refreshTimer.Start();

            _fileManager.LoadDataFromFile(this, ref currentDate);

            AddNewRow(currentDate, keyboardPressCount, mouseLeftClickCount, mouseRightClickCount);

            _mouseProc = MouseHookCallback;
            _keyboardProc = KeyboardHookCallback;
            _mouseHookID = SetHook(_mouseProc, WH_MOUSE_LL);
            _keyboardHookID = SetHook(_keyboardProc, WH_KEYBOARD_LL);

            this.Resize += Form1_Resize;
            this.FormClosing += Form1_FormClosing;
        }

        private void InitializeLayout()
        {
            mainLayout = new TableLayoutPanel
            {
                Dock = DockStyle.Fill,
                RowCount = 3, // 改回3行
                ColumnCount = 1,
                Padding = new Padding(16) // 增加外边距，实现 Fluent Design
            };

            mainLayout.RowStyles.Add(new RowStyle(SizeType.Absolute, 250)); // 固定高度用于表格视图
            mainLayout.RowStyles.Add(new RowStyle(SizeType.Absolute, 60)); // 固定高度用于累计和
            mainLayout.RowStyles.Add(new RowStyle(SizeType.Percent, 490)); // 图表占用剩余空间

            this.Controls.Add(mainLayout);
        }

        private void InitializeDataGridView()
        {
            eventLogGridView = new DataGridView
            {
                Dock = DockStyle.Fill,
                AllowUserToAddRows = false,
                ReadOnly = true,
                AutoSizeColumnsMode = DataGridViewAutoSizeColumnsMode.Fill,
                RowHeadersVisible = false,
                AlternatingRowsDefaultCellStyle = new DataGridViewCellStyle { BackColor = Color.LightBlue },
                DefaultCellStyle = new DataGridViewCellStyle { Font = new Font("Microsoft YaHei", 9) },
                ColumnHeadersDefaultCellStyle = new DataGridViewCellStyle { Font = new Font("Microsoft YaHei", 10, FontStyle.Bold), BackColor = Color.LightGray, Alignment = DataGridViewContentAlignment.MiddleCenter },
                BorderStyle = BorderStyle.Fixed3D,
                CellBorderStyle = DataGridViewCellBorderStyle.SingleHorizontal,
                SelectionMode = DataGridViewSelectionMode.FullRowSelect,
                MultiSelect = false
            };

            eventLogGridView.Columns.Add("Date", "日期");
            eventLogGridView.Columns.Add("KeyboardPress", "键盘按键");
            eventLogGridView.Columns.Add("MouseLeftClick", "鼠标左键");
            eventLogGridView.Columns.Add("MouseRightClick", "鼠标右键");

            // 设置列的格式
            eventLogGridView.Columns["Date"].DefaultCellStyle.Alignment = DataGridViewContentAlignment.MiddleCenter;
            eventLogGridView.Columns["KeyboardPress"].DefaultCellStyle.Alignment = DataGridViewContentAlignment.MiddleRight;
            eventLogGridView.Columns["MouseLeftClick"].DefaultCellStyle.Alignment = DataGridViewContentAlignment.MiddleRight;
            eventLogGridView.Columns["MouseRightClick"].DefaultCellStyle.Alignment = DataGridViewContentAlignment.MiddleRight;

            Panel gridViewPanel = new Panel
            {
                Dock = DockStyle.Fill,
                AutoScroll = true,
                BorderStyle = BorderStyle.FixedSingle
            };
            gridViewPanel.Controls.Add(eventLogGridView);

            mainLayout.Controls.Add(gridViewPanel, 0, 0);

            // 初始化累计和面板
            summaryPanel = new Panel
            {
                Dock = DockStyle.Fill,
                Height = 60,
                BackColor = Color.FromArgb(240, 247, 255), // 淡蓝色背景
                BorderStyle = BorderStyle.FixedSingle
            };

            // 创建水平布局面板
            Panel horizontalPanel = new Panel
            {
                Dock = DockStyle.Fill,
                AutoSize = true,
                AutoSizeMode = AutoSizeMode.GrowAndShrink
            };

            // 键盘图片和计数
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

            // 鼠标左键图片和计数
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

            // 鼠标右键图片和计数
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

            // 加载图片
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

            // 添加控件到面板
            horizontalPanel.Controls.Add(keyboardPic);
            horizontalPanel.Controls.Add(keyboardLabel);
            horizontalPanel.Controls.Add(leftPic);
            horizontalPanel.Controls.Add(leftLabel);
            horizontalPanel.Controls.Add(rightPic);
            horizontalPanel.Controls.Add(rightLabel);

            // 保存引用
            summaryPicControls = new Tuple<PictureBox, Label, PictureBox, Label, PictureBox, Label>(
                keyboardPic, keyboardLabel, leftPic, leftLabel, rightPic, rightLabel
            );

            summaryPanel.Controls.Add(horizontalPanel);
            mainLayout.Controls.Add(summaryPanel, 0, 1);

            // 利用反射开启双缓冲
            typeof(Control).GetProperty("DoubleBuffered",
                System.Reflection.BindingFlags.NonPublic |
                System.Reflection.BindingFlags.Instance)
                ?.SetValue(eventLogGridView, true, null);
        }

        private RadioButton rbLastYear; 
        private RadioButton rbAllHistory;

        private void InitializeChart()
        {
            // 创建ScottPlot图表
            formsPlot = new ScottPlot.FormsPlot
            {
                Dock = DockStyle.Fill
            };

            // 创建包含图表和切换按钮的面板
            Panel chartWithControlsPanel = new Panel
            {
                Dock = DockStyle.Fill
            };

            // 创建顶部控制面板（包含图例和切换按钮）
            Panel topControlPanel = new Panel
            {
                Dock = DockStyle.Top,
                Height = 40,
                BackColor = Color.WhiteSmoke,
                Padding = new Padding(5, 5, 5, 5)
            };

            // 创建包含图例和切换按钮的水平布局面板
            FlowLayoutPanel legendAndRadioPanel = new FlowLayoutPanel
            {
                Dock = DockStyle.Fill,
                FlowDirection = FlowDirection.LeftToRight,
                AutoSize = false,
                BackColor = Color.WhiteSmoke
            };

            // 添加间隔
            legendAndRadioPanel.Controls.Add(new Label { Width = 40 });

            // 创建切换按钮
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

            // 将图例和切换按钮添加到顶部控制面板
            topControlPanel.Controls.Add(legendAndRadioPanel);

            // 设置图表的Dock属性为Fill
            formsPlot.Dock = DockStyle.Fill;

            // 先添加图表，再添加顶部控制面板
            chartWithControlsPanel.Controls.Add(formsPlot);
            chartWithControlsPanel.Controls.Add(topControlPanel);

            // 将面板添加到主布局
            mainLayout.Controls.Add(chartWithControlsPanel, 0, 2);

            // 加载初始数据
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
            }
        }

        private void OnRestore(object sender, EventArgs e)
        {
            this.Show();
            this.WindowState = FormWindowState.Normal;
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
                    // 禁用UI更新，显示导入中状态
                    this.Enabled = false;
                    
                    // 显示导入进度对话框
                    using (var progressForm = new Form())
                    {
                        progressForm.Text = "导入历史数据";
                        progressForm.Size = new System.Drawing.Size(400, 120);
                        progressForm.StartPosition = FormStartPosition.CenterScreen;
                        progressForm.FormBorderStyle = FormBorderStyle.FixedDialog;
                        progressForm.MaximizeBox = false;
                        progressForm.MinimizeBox = false;
                        
                        var label = new Label
                        {
                            Text = "正在导入历史数据...",
                            Dock = DockStyle.Top,
                            TextAlign = System.Drawing.ContentAlignment.MiddleCenter,
                            Padding = new Padding(0, 20, 0, 10)
                        };
                        
                        var progressBar = new ProgressBar
                        {
                            Dock = DockStyle.Top,
                            Style = ProgressBarStyle.Marquee,
                            MarqueeAnimationSpeed = 30
                        };
                        
                        progressForm.Controls.Add(progressBar);
                        progressForm.Controls.Add(label);
                        
                        // 在后台线程中执行导入
                System.Threading.Tasks.Task.Run(() =>
                {
                    try
                    {
                        // 使用DataImporter导入数据
                        var importResult = _dataImporter.ImportHistoricalData(openFileDialog.FileName);
                        
                        // 导入完成后，在UI线程中更新界面
                        this.Invoke(new Action(() =>
                        {
                            try
                            {
                                // 先关闭进度对话框
                                progressForm.Close();
                                
                                if (importResult.Success)
                                {
                                    // 显示成功消息
                                    string message = $"历史数据导入成功！\n\n导入记录数: {importResult.ImportedCount}\n跳过记录数: {importResult.SkippedCount}\nHome键按键数: {importResult.HomeKeyPresses}";
                                    MessageBox.Show(message, "成功", MessageBoxButtons.OK, MessageBoxIcon.Information);
                                }
                                else
                                {
                                    // 显示错误消息
                                    MessageBox.Show($"导入失败: {importResult.ErrorMessage}", "错误", MessageBoxButtons.OK, MessageBoxIcon.Error);
                                }
                                
                                // 重新启用主窗口
                                this.Enabled = true;
                                
                                // 延迟更新数据和图表，避免UI卡顿
                                System.Threading.Tasks.Task.Delay(500).ContinueWith((task) =>
                                {
                                    this.Invoke(new Action(() =>
                                    {
                                        // 重新加载数据
                                        eventLogGridView.SuspendLayout();
                                        eventLogGridView.Rows.Clear();
                                        // 清空数据字典，避免引用已删除的行
                                        dailyDataRows.Clear();
                                        totalKeyboardPress = 0;
                                        totalMouseLeftClick = 0;
                                        totalMouseRightClick = 0;
                                        _fileManager.LoadDataFromFile(this, ref currentDate);
                                        eventLogGridView.ResumeLayout();
                                        
                                        // 重新加载图表数据
                                        LoadDataForChart(formsPlot);
                                    }));
                                });
                            }
                            catch (Exception ex)
                            {
                                MessageBox.Show($"Error updating UI: {ex.Message}", "Error", MessageBoxButtons.OK, MessageBoxIcon.Error);
                                progressForm.Close();
                                this.Enabled = true;
                            }
                        }));
                    }
                    catch (Exception ex)
                    {
                        this.Invoke(new Action(() =>
                        {
                            MessageBox.Show($"Error importing historical data: {ex.Message}", "Error", MessageBoxButtons.OK, MessageBoxIcon.Error);
                            progressForm.Close();
                            this.Enabled = true;
                        }));
                    }
                });
                        
                        // 显示进度对话框
                        progressForm.ShowDialog();
                    }
                }
                catch (Exception ex)
                {
                    this.Enabled = true;
                    MessageBox.Show($"Error importing historical data: {ex.Message}", "Error", MessageBoxButtons.OK, MessageBoxIcon.Error);
                }
            }
        }

        private void ImportHistoricalData(string filePath)
        {
            if (!File.Exists(filePath))
            {
                throw new FileNotFoundException("Historical data file not found.");
            }

            // 获取数据库连接
            var connection = (_fileManager as FileManager).GetConnection();
            
            // 开始事务
            using (var transaction = connection.BeginTransaction())
            {
                try
                {
                    int importedCount = 0;
                    int skippedCount = 0;
                    int homeKeyPresses = 0;

                    // 首先获取所有已存在的日期，避免重复查询
                    var existingDates = new HashSet<string>();
                    string checkDatesQuery = "SELECT date FROM click_data";
                    using (var command = new SQLiteCommand(checkDatesQuery, connection, transaction))
                    {
                        using (var reader = command.ExecuteReader())
                        {
                            while (reader.Read())
                            {
                                existingDates.Add(reader["date"].ToString());
                            }
                        }
                    }

                    // 读取并处理历史数据
                    using (StreamReader sr = new StreamReader(filePath))
                    {
                        string line;
                        while ((line = sr.ReadLine()) != null)
                        {
                            var parts = line.Split(',');
                            if (parts.Length == 4)
                            {
                                DateTime date = DateTime.Parse(parts[0]);
                                string dateStr = date.ToString("yyyy-MM-dd");
                                
                                // 检查日期是否已存在
                                if (!existingDates.Contains(dateStr))
                                {
                                    int keyboardPress = int.Parse(parts[1]);
                                    int leftClick = int.Parse(parts[2]);
                                    int rightClick = int.Parse(parts[3]);

                                    // 插入数据到click_data表
                                    string insertQuery = @"
                                        INSERT INTO click_data (date, keyboard_press, mouse_left_click, mouse_right_click)
                                        VALUES (@date, @keyboardPress, @leftClick, @rightClick);
                                    ";

                                    using (var command = new SQLiteCommand(insertQuery, connection, transaction))
                                    {
                                        command.Parameters.AddWithValue("@date", dateStr);
                                        command.Parameters.AddWithValue("@keyboardPress", keyboardPress);
                                        command.Parameters.AddWithValue("@leftClick", leftClick);
                                        command.Parameters.AddWithValue("@rightClick", rightClick);
                                        command.ExecuteNonQuery();
                                    }

                                    // 将键盘数据都映射到Home键（VK_HOME = 0x24）
                                    if (keyboardPress > 0)
                                    {
                                        homeKeyPresses += keyboardPress;
                                    }

                                    importedCount++;
                                }
                                else
                                {
                                    skippedCount++;
                                }
                            }
                        }
                    }

                    // 批量更新Home键的点击次数
                    if (homeKeyPresses > 0)
                    {
                        // 先查询当前Home键的点击次数
                        int existingHomeCount = 0;
                        string checkHomeQuery = "SELECT press_count FROM key_distribution WHERE key_code = @keyCode";
                        using (var command = new SQLiteCommand(checkHomeQuery, connection, transaction))
                        {
                            command.Parameters.AddWithValue("@keyCode", 0x24); // Home键
                            object result = command.ExecuteScalar();
                            if (result != null && result != DBNull.Value)
                            {
                                existingHomeCount = Convert.ToInt32(result);
                            }
                        }

                        // 插入或更新Home键数据
                        string updateHomeQuery = @"
                            INSERT OR REPLACE INTO key_distribution (key_code, key_name, press_count)
                            VALUES (@keyCode, @keyName, @pressCount);
                        ";

                        using (var command = new SQLiteCommand(updateHomeQuery, connection, transaction))
                        {
                            command.Parameters.AddWithValue("@keyCode", 0x24); // Home键
                            command.Parameters.AddWithValue("@keyName", "Home");
                            command.Parameters.AddWithValue("@pressCount", existingHomeCount + homeKeyPresses);
                            command.ExecuteNonQuery();
                        }
                    }

                    // 提交事务
                    transaction.Commit();

                    Logger.Instance().Log($"Import completed: {importedCount} records imported, {skippedCount} records skipped, {homeKeyPresses} Home key presses added");
                }
                catch (Exception ex)
                {
                    // 回滚事务
                    transaction.Rollback();
                    Logger.Instance().Log($"Import failed: {ex.Message}");
                    throw;
                }
            }
        }

        private bool CheckDateExists(DateTime date)
        {
            string query = "SELECT COUNT(*) FROM click_data WHERE date = @date";
            
            using (var command = new SQLiteCommand(query, (_fileManager as FileManager).GetConnection()))
            {
                command.Parameters.AddWithValue("@date", date.ToString("yyyy-MM-dd"));
                object result = command.ExecuteScalar();
                return Convert.ToInt32(result) > 0;
            }
        }

        private void InsertClickData(DateTime date, int keyboardPress, int leftClick, int rightClick)
        {
            string query = @"
                INSERT INTO click_data (date, keyboard_press, mouse_left_click, mouse_right_click)
                VALUES (@date, @keyboardPress, @leftClick, @rightClick);
            ";

            using (var command = new SQLiteCommand(query, (_fileManager as FileManager).GetConnection()))
            {
                command.Parameters.AddWithValue("@date", date.ToString("yyyy-MM-dd"));
                command.Parameters.AddWithValue("@keyboardPress", keyboardPress);
                command.Parameters.AddWithValue("@leftClick", leftClick);
                command.Parameters.AddWithValue("@rightClick", rightClick);
                command.ExecuteNonQuery();
            }
        }

        private void UpdateKeyDistribution(int keyCode, int count)
        {
            // 先查询是否已存在该按键
            string checkQuery = "SELECT press_count FROM key_distribution WHERE key_code = @keyCode";
            
            int existingCount = 0;
            using (var command = new SQLiteCommand(checkQuery, (_fileManager as FileManager).GetConnection()))
            {
                command.Parameters.AddWithValue("@keyCode", keyCode);
                object result = command.ExecuteScalar();
                if (result != null && result != DBNull.Value)
                {
                    existingCount = Convert.ToInt32(result);
                }
            }
            
            // 插入或更新数据
            string query = @"
                INSERT OR REPLACE INTO key_distribution (key_code, key_name, press_count)
                VALUES (@keyCode, @keyName, @pressCount);
            ";

            using (var command = new SQLiteCommand(query, (_fileManager as FileManager).GetConnection()))
            {
                command.Parameters.AddWithValue("@keyCode", keyCode);
                command.Parameters.AddWithValue("@keyName", "Home");
                command.Parameters.AddWithValue("@pressCount", existingCount + count);
                command.ExecuteNonQuery();
            }
        }

        private void OnExit(object sender, EventArgs e)
        {
            Logger.Instance().Log("OnExit triggered");

            _fileManager.SaveDataToFile(true, eventLogGridView);
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
            
            // Close database connection
            (_fileManager as FileManager)?.CloseConnection();
            
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
            if (nCode >= 0)
            {
                int vkCode = Marshal.ReadInt32(lParam);
                bool isKeyDown = (wParam == (IntPtr)WM_KEYDOWN || wParam == (IntPtr)WM_SYSKEYDOWN);
                bool isKeyUp = (wParam == (IntPtr)WM_KEYUP || wParam == (IntPtr)WM_SYSKEYUP);
                
                if (isKeyDown)
                {
                    // 只有当按键之前未按下时才计数
                    if (!keyStates.ContainsKey(vkCode) || !keyStates[vkCode])
                    {
                        keyboardPressCount++;
                        
                        // 统计按键分布
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
                    // 标记按键为释放状态
                    keyStates[vkCode] = false;
                }
            }

            return CallNextHookEx(IntPtr.Zero, nCode, wParam, lParam);
        }

        private void CheckDateAndUpdateLog()
        {
            DateTime now = DateTime.Now.Date;

            if (now != currentDate)
            {
                // 标记需要更新UI
                uiUpdateNeeded = true;

                currentDate = now;
                keyboardPressCount = 0;
                mouseLeftClickCount = 0;
                mouseRightClickCount = 0;

                // 标记需要更新UI
                uiUpdateNeeded = true;
            }
            else
            {
                // 标记需要更新UI
                uiUpdateNeeded = true;
            }
            
            // 不再使用事件阈值保存，只通过定时保存
            // eventCount++; // 移除事件计数
            // 保存逻辑完全交给 _syncTimer
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

        // 用于存储每日数据的字典，提高查找效率
        private Dictionary<string, DataGridViewRow> dailyDataRows = new Dictionary<string, DataGridViewRow>();
        
        // 用于缓存总计数据
        private int totalKeyboardPress = 0;
        private int totalMouseLeftClick = 0;
        private int totalMouseRightClick = 0;
        
        // 用于标记是否需要更新UI
        private bool uiUpdateNeeded = false;
        
        // 用于定时更新UI的定时器
        private System.Windows.Forms.Timer refreshTimer;

        private void AddOrUpdateRow(DateTime date, int keyboardPress, int leftClick, int rightClick)
        {
            string dateStr = date.ToString("yyyy-MM-dd");

            // 使用字典查找，O(1)时间复杂度
            if (dailyDataRows.ContainsKey(dateStr))
            {
                // 更新现有行
                DataGridViewRow row = dailyDataRows[dateStr];
                
                // 计算差值，更新总计
                int oldKeyboardPress = Convert.ToInt32(row.Cells["KeyboardPress"].Value);
                int oldLeftClick = Convert.ToInt32(row.Cells["MouseLeftClick"].Value);
                int oldRightClick = Convert.ToInt32(row.Cells["MouseRightClick"].Value);
                
                totalKeyboardPress += (keyboardPress - oldKeyboardPress);
                totalMouseLeftClick += (leftClick - oldLeftClick);
                totalMouseRightClick += (rightClick - oldRightClick);
                
                // 更新单元格值
                row.Cells["KeyboardPress"].Value = keyboardPress;
                row.Cells["MouseLeftClick"].Value = leftClick;
                row.Cells["MouseRightClick"].Value = rightClick;
            }
            else
            {
                // 添加新行
                eventLogGridView.SuspendLayout();
                int rowIndex = eventLogGridView.Rows.Add(dateStr, keyboardPress, leftClick, rightClick);
                DataGridViewRow newRow = eventLogGridView.Rows[rowIndex];
                dailyDataRows[dateStr] = newRow;
                
                // 更新总计
                totalKeyboardPress += keyboardPress;
                totalMouseLeftClick += leftClick;
                totalMouseRightClick += rightClick;
                
                // 限制表格只显示365天的数据
                if (dailyDataRows.Count > 365)
                {
                    // 找出最旧的日期
                    string oldestDate = dailyDataRows.Keys.Min();
                    if (dailyDataRows.TryGetValue(oldestDate, out DataGridViewRow oldestRow))
                    {
                        // 从总计中减去旧行的数据
                        totalKeyboardPress -= Convert.ToInt32(oldestRow.Cells["KeyboardPress"].Value);
                        totalMouseLeftClick -= Convert.ToInt32(oldestRow.Cells["MouseLeftClick"].Value);
                        totalMouseRightClick -= Convert.ToInt32(oldestRow.Cells["MouseRightClick"].Value);
                        
                        // 移除旧行
                        eventLogGridView.Rows.Remove(oldestRow);
                        dailyDataRows.Remove(oldestDate);
                    }
                }
                
                eventLogGridView.ResumeLayout();
                
                Logger.Instance().Log($"Added new row for {dateStr}: KeyboardPress={keyboardPress}, MouseLeftClick={leftClick}, MouseRightClick={rightClick}");
            }

            // 更新累计和显示（历史总数）
            if (summaryPicControls != null)
            {
                summaryPicControls.Item2.Text = totalKeyboardPress.ToString("N0");
                summaryPicControls.Item4.Text = totalMouseLeftClick.ToString("N0");
                summaryPicControls.Item6.Text = totalMouseRightClick.ToString("N0");
            }
        }

        private void RefreshTimer_Tick(object sender, EventArgs e)
        {
            // 只有在数据真正变化且需要更新时才操作 UI
            if (uiUpdateNeeded)
            {
                uiUpdateNeeded = false;
                
                // 确保使用 BeginInvoke 以免阻塞回调线程
                this.BeginInvoke(new Action(() => {
                    eventLogGridView.SuspendLayout();
                    // 只更新当前行，不要触发全表重绘
                    AddOrUpdateRow(currentDate, keyboardPressCount, mouseLeftClickCount, mouseRightClickCount);
                    eventLogGridView.ResumeLayout(false); // false 表示不强制立即布局
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
                // 最近一年数据，按周聚合
                data = (_fileManager as FileManager).LoadLastYearDataByWeek();
            }
            else
            {
                // 所有历史数据，使用每周最大值
                data = (_fileManager as FileManager).LoadDataForChartByWeek();
            }

            // 清除现有数据
            formsPlot.Plot.Clear();

            // 准备数据点
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
                // 使用索引作为X轴值
                x.Add(i);
                yKeyboard.Add(double.Parse(data[i,1]));
                yLeftClick.Add(double.Parse(data[i,2]));
                yRightClick.Add(double.Parse(data[i,3]));
                
                // 格式化周标签为日期显示
                string weekStr = data[i,0];
                if (weekStr.Length >= 6)
                {
                    string year = weekStr.Substring(0, 4);
                    string week = weekStr.Substring(5);
                    labels.Add($"{year}年{week}周");
                }
                else
                {
                    labels.Add(weekStr);
                }
            }

            // 保存数据到类变量
            chartXValues = x;
            chartYKeyboard = yKeyboard;
            chartYLeftClick = yLeftClick;
            chartYRightClick = yRightClick;
            chartLabels = labels;

            // 只有在有数据时才绘制图表
            if (x.Count > 0)
            {
                // 优化X轴标签显示，只保留关键节点
                List<double> xTickPositions = new List<double>();
                List<string> xTickLabels = new List<string>();
                
                int step = Math.Max(1, x.Count / 10); // 最多显示10个标签
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

                // 添加数据系列，启用抗锯齿
                formsPlot.Plot.AddScatter(x.ToArray(), yKeyboard.ToArray(), Color.RoyalBlue, 2, markerShape: MarkerShape.filledCircle, label: "键盘按键");
                formsPlot.Plot.AddScatter(x.ToArray(), yLeftClick.ToArray(), Color.ForestGreen, 2, markerShape: MarkerShape.filledCircle, label: "鼠标左键");
                formsPlot.Plot.AddScatter(x.ToArray(), yRightClick.ToArray(), Color.Crimson, 2, markerShape: MarkerShape.filledCircle, label: "鼠标右键");

                // 恢复原来的配色
                formsPlot.Plot.Style(ScottPlot.Style.Default);
                formsPlot.Plot.XAxis.Grid(true);
                formsPlot.Plot.YAxis.Grid(true);
                formsPlot.Plot.XAxis2.Ticks(false);
                formsPlot.Plot.YAxis2.Ticks(false);
                formsPlot.Plot.XAxis.TickLabelStyle(fontName: "Segoe UI");
                formsPlot.Plot.YAxis.TickLabelStyle(fontName: "Segoe UI");


                // 设置X轴标签
                formsPlot.Plot.XTicks(xTickPositions.ToArray(), xTickLabels.ToArray());
                formsPlot.Plot.XAxis.TickLabelStyle(rotation: 45, fontName: "Segoe UI");

                // 移除固定图例，添加动态提示
                ScottPlot.Plottable.Text tooltipText = null;
                
                // 鼠标移动时更新提示
                formsPlot.MouseMove += (sender, e) =>
                {
                    if (chartXValues != null && chartXValues.Count > 0)
                    {
                        // 从鼠标事件获取坐标并转换为图表坐标
                        double mouseX = formsPlot.Plot.GetCoordinateX(e.Location.X);
                        
                        // 将鼠标X坐标转换为索引
                        int index = (int)Math.Round(mouseX);
                        
                        // 确保索引在有效范围内
                        if (index >= 0 && index < chartXValues.Count)
                        {
                            string dateLabel = chartLabels[index];
                            string keyboard = ((int)chartYKeyboard[index]).ToString("N0");
                            string leftClick = ((int)chartYLeftClick[index]).ToString("N0");
                            string rightClick = ((int)chartYRightClick[index]).ToString("N0");
                            
                            string tooltip = $"{dateLabel}\n键盘: {keyboard}\n左键: {leftClick}\n右键: {rightClick}";
                            
                            // 移除旧的提示
                            if (tooltipText != null)
                            {
                                formsPlot.Plot.Remove(tooltipText);
                            }
                            
                            // 添加新提示，固定在图表顶部中间
                            double centerX = (chartXValues[chartXValues.Count - 1] - chartXValues[0]) / 2 + chartXValues[0];
                            
                            // 计算图表顶部位置（使用数据最大值的1.1倍作为顶部参考）
                            double maxY = Math.Max(
                                chartYKeyboard.Max(),
                                Math.Max(chartYLeftClick.Max(), chartYRightClick.Max())
                            );
                            double chartTopY = maxY * 0.9; // 放在数据最大值的90%位置，接近顶部
                            
                            tooltipText = formsPlot.Plot.AddText(tooltip, centerX, chartTopY);
                            tooltipText.Font.Size = 14;
                            tooltipText.Font.Name = "Segoe UI";
                            tooltipText.Font.Color = Color.Black;
                            tooltipText.BackgroundColor = Color.FromArgb(220, 255, 255, 255);
                            
                            formsPlot.Refresh();
                        }
                    }
                };

                // 平滑动画效果
                formsPlot.Refresh();
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
        private const int WM_KEYUP = 0x0101;
        private const int WM_SYSKEYUP = 0x0105;

        #endregion
    }
}
