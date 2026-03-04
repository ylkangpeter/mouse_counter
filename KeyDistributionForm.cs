using System;
using System.Collections.Generic;
using System.Windows.Forms;
using System.Drawing;

namespace MouseClickRecorder
{
    public class KeyDistributionForm : Form
    {
        private DataGridView keyDistributionGridView;
        private FileManager _fileManager;

        public KeyDistributionForm(FileManager fileManager)
        {
            _fileManager = fileManager;
            InitializeComponent();
            LoadKeyDistributionData();
        }

        private void InitializeComponent()
        {
            this.Text = "按键分布统计";
            this.Size = new Size(400, 500);
            this.StartPosition = FormStartPosition.CenterScreen;
            this.FormBorderStyle = FormBorderStyle.Sizable;
            this.MaximizeBox = true;

            keyDistributionGridView = new DataGridView
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

            keyDistributionGridView.Columns.Add("Key", "按键");
            keyDistributionGridView.Columns.Add("Count", "点击次数");

            keyDistributionGridView.Columns["Key"].DefaultCellStyle.Alignment = DataGridViewContentAlignment.MiddleCenter;
            keyDistributionGridView.Columns["Count"].DefaultCellStyle.Alignment = DataGridViewContentAlignment.MiddleRight;

            this.Controls.Add(keyDistributionGridView);
        }

        private void LoadKeyDistributionData()
        {
            List<KeyValuePair<string, int>> keyDistributionList = _fileManager.GetKeyDistributionDescending();

            keyDistributionGridView.Rows.Clear();

            foreach (var item in keyDistributionList)
            {
                keyDistributionGridView.Rows.Add(item.Key, item.Value);
            }
        }
    }
}
