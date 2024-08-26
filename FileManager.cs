using System;
using System.IO;
using System.Linq;
using System.Windows.Forms;

namespace MouseClickRecorder
{
    public class FileManager
    {
        private string DataFileName = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "mouse_clicker.txt");

        public FileManager()
        {
            Logger.Instance().Log($"Data file path: {DataFileName}");
            EnsureFileExists(DataFileName);

            if (IsFileLocked(DataFileName))
            {
                MessageBox.Show("Data file is currently in use by another process. Application will not start.", "Error", MessageBoxButtons.OK, MessageBoxIcon.Error);
                Application.Exit(); // Exit application
            }
        }

        private void EnsureFileExists(string filePath)
        {
            if (!File.Exists(filePath))
            {
                using (File.Create(filePath)) { }
            }
        }


        public void SaveDataToFile(bool forceSave, DataGridView eventLogGridView)
        {
            using (StreamWriter sw = new StreamWriter(DataFileName))
            {
                foreach (DataGridViewRow row in eventLogGridView.Rows)
                {
                    if (row.IsNewRow) continue;

                    string date = row.Cells["Date"].Value.ToString();
                    string keyboardPress = row.Cells["KeyboardPress"].Value.ToString();
                    string leftClick = row.Cells["MouseLeftClick"].Value.ToString();
                    string rightClick = row.Cells["MouseRightClick"].Value.ToString();

                    sw.WriteLine($"{date},{keyboardPress},{leftClick},{rightClick}");
                }
            }

        }

        public void LoadDataFromFile(Form1 form, ref DateTime currentDate)
        {
            if (!File.Exists(DataFileName))
                return;

            using (StreamReader sr = new StreamReader(DataFileName))
            {
                string line;
                while ((line = sr.ReadLine()) != null)
                {
                    var parts = line.Split(',');
                    if (parts.Length == 4)
                    {
                        DateTime date = DateTime.Parse(parts[0]);
                        int keyboardPress = int.Parse(parts[1]);
                        int leftClick = int.Parse(parts[2]);
                        int rightClick = int.Parse(parts[3]);

                        if (date.Date != currentDate)
                        {
                            form.AddNewRow(date, keyboardPress, leftClick, rightClick);
                        }
                        else
                        {
                            form.keyboardPressCount = keyboardPress;
                            form.mouseLeftClickCount = leftClick;
                            form.mouseRightClickCount = rightClick;
                        }
                    }
                }
            }
        }

        
        public string[,] LoadDataForChart(int days)
        {
            if (!File.Exists(DataFileName))
                return new string[0,0];

            using (StreamReader sr = new StreamReader(DataFileName))
            {
                string line;
                string[,] result = new string[days,4];
                while ((line = sr.ReadLine()) != null && days>0)
                {   
                    var parts = line.Split(',');
                    if (parts.Length == 4)
                    {
                        result[days-1,0] = DateTime.Parse(parts[0]).ToString("yyyy-MM-dd");
                        result[days-1,1] = parts[1];
                        result[days-1,2] = parts[2];
                        result[days-1,3] = parts[3];
                    }
                    // Logger.Instance().Log(days+"-"+inx);
                    days--;
                }
                return result;
            }
        }

        public void AddOrUpdateRow(DataGridView dataGridView, DateTime date, int keyboardPress, int leftClick, int rightClick)
        {
            var row = dataGridView.Rows
                                  .Cast<DataGridViewRow>()
                                  .FirstOrDefault(r => DateTime.Parse(r.Cells[0].Value.ToString()) == date);

            if (row == null)
            {
                dataGridView.Rows.Add(date.ToString("yyyy-MM-dd"), keyboardPress, leftClick, rightClick);
            }
            else
            {
                row.Cells[1].Value = keyboardPress;
                row.Cells[2].Value = leftClick;
                row.Cells[3].Value = rightClick;
            }
        }

        public static bool IsFileLocked(string filePath)
        {
            FileStream fileStream = null;

            try
            {
                fileStream = new FileStream(filePath, FileMode.Open, FileAccess.ReadWrite, FileShare.None);
            }
            catch (IOException)
            {
                return true; // File is locked
            }
            finally
            {
                fileStream?.Close();
            }

            return false; // File is not locked
        }
    }
}
