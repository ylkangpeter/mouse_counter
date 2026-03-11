using System;
using System.Data.SQLite;
using System.IO;
using System.Linq;
using System.Windows.Forms;

namespace MouseClickRecorder
{
    public class FileManager
    {
        private string DataFileName = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "mouse_clicker.db");
        private SQLiteConnection _connection;

        public FileManager()
        {
            Logger.Instance().Log($"Data file path: {DataFileName}");
            InitializeDatabase();
        }

        private void InitializeDatabase()
        {
            if (!File.Exists(DataFileName))
            {
                SQLiteConnection.CreateFile(DataFileName);
            }

            _connection = new SQLiteConnection(GetConnectionString());
            _connection.Open();

            string createTableQuery = @"
                CREATE TABLE IF NOT EXISTS click_data (
                    date TEXT PRIMARY KEY,
                    keyboard_press INTEGER,
                    mouse_left_click INTEGER,
                    mouse_right_click INTEGER
                );
            ";

            using (SQLiteCommand command = new SQLiteCommand(createTableQuery, _connection))
            {
                command.ExecuteNonQuery();
            }

            string createKeyDistributionTableQuery = @"
                CREATE TABLE IF NOT EXISTS key_distribution (
                    key_code INTEGER,
                    key_name TEXT,
                    press_count INTEGER DEFAULT 0,
                    PRIMARY KEY (key_code)
                );
            ";

            using (SQLiteCommand command = new SQLiteCommand(createKeyDistributionTableQuery, _connection))
            {
                command.ExecuteNonQuery();
            }
        }

        public void SaveDataToFile(bool forceSave, DataGridView eventLogGridView, DateTime currentDate)
        {
            string todayStr = currentDate.ToString("yyyy-MM-dd");

            using (var transaction = _connection.BeginTransaction())
            {
                try
                {
                    foreach (DataGridViewRow row in eventLogGridView.Rows)
                    {
                        if (row.IsNewRow)
                        {
                            continue;
                        }

                        string date = row.Cells["Date"].Value.ToString();
                        if (date != todayStr && !forceSave)
                        {
                            continue;
                        }

                        int keyboardPress = int.Parse(row.Cells["KeyboardPress"].Value.ToString());
                        int leftClick = int.Parse(row.Cells["MouseLeftClick"].Value.ToString());
                        int rightClick = int.Parse(row.Cells["MouseRightClick"].Value.ToString());

                        string insertQuery = @"
                            INSERT OR REPLACE INTO click_data (date, keyboard_press, mouse_left_click, mouse_right_click)
                            VALUES (@date, @keyboardPress, @leftClick, @rightClick);
                        ";

                        using (SQLiteCommand command = new SQLiteCommand(insertQuery, _connection))
                        {
                            command.Parameters.AddWithValue("@date", date);
                            command.Parameters.AddWithValue("@keyboardPress", keyboardPress);
                            command.Parameters.AddWithValue("@leftClick", leftClick);
                            command.Parameters.AddWithValue("@rightClick", rightClick);
                            command.ExecuteNonQuery();
                        }
                    }

                    transaction.Commit();
                }
                catch
                {
                    transaction.Rollback();
                    throw;
                }
            }
        }

        public void LoadDataFromFile(MainForm form, ref DateTime currentDate)
        {
            int maxDaysToShow = new ConfigManager().Config.MaxDaysToShow;
            DateTime earliestDate = DateTime.Now.Date.AddDays(-(maxDaysToShow - 1));
            string query = "SELECT date, keyboard_press, mouse_left_click, mouse_right_click FROM click_data WHERE date >= @earliestDate ORDER BY date DESC";

            using (SQLiteCommand command = new SQLiteCommand(query, _connection))
            {
                command.Parameters.AddWithValue("@earliestDate", earliestDate.ToString("yyyy-MM-dd"));
                using (SQLiteDataReader reader = command.ExecuteReader())
                {
                    while (reader.Read())
                    {
                        DateTime date = DateTime.Parse(reader["date"].ToString());
                        int keyboardPress = Convert.ToInt32(reader["keyboard_press"]);
                        int leftClick = Convert.ToInt32(reader["mouse_left_click"]);
                        int rightClick = Convert.ToInt32(reader["mouse_right_click"]);

                        if (date.Date != currentDate)
                        {
                            form.AddNewRow(date, keyboardPress, leftClick, rightClick);
                        }
                        else
                        {
                            form.SetCurrentDayData(keyboardPress, leftClick, rightClick);
                        }
                    }
                }
            }
        }

        public (int, int, int) GetTotalCounts()
        {
            string query = "SELECT SUM(keyboard_press) as total_keyboard, SUM(mouse_left_click) as total_left, SUM(mouse_right_click) as total_right FROM click_data";
            int totalKeyboard = 0;
            int totalLeft = 0;
            int totalRight = 0;

            using (SQLiteCommand command = new SQLiteCommand(query, _connection))
            {
                using (SQLiteDataReader reader = command.ExecuteReader())
                {
                    if (reader.Read())
                    {
                        totalKeyboard = reader.IsDBNull(0) ? 0 : Convert.ToInt32(reader[0]);
                        totalLeft = reader.IsDBNull(1) ? 0 : Convert.ToInt32(reader[1]);
                        totalRight = reader.IsDBNull(2) ? 0 : Convert.ToInt32(reader[2]);
                    }
                }
            }

            return (totalKeyboard, totalLeft, totalRight);
        }

        public string[,] LoadDataForChart(int days)
        {
            string query = $"SELECT date, keyboard_press, mouse_left_click, mouse_right_click FROM click_data ORDER BY date DESC LIMIT {days}";
            string[,] result = new string[days, 4];

            using (SQLiteCommand command = new SQLiteCommand(query, _connection))
            {
                using (SQLiteDataReader reader = command.ExecuteReader())
                {
                    int index = days - 1;
                    while (reader.Read() && index >= 0)
                    {
                        result[index, 0] = reader["date"].ToString();
                        result[index, 1] = reader["keyboard_press"].ToString();
                        result[index, 2] = reader["mouse_left_click"].ToString();
                        result[index, 3] = reader["mouse_right_click"].ToString();
                        index--;
                    }
                }
            }

            return result;
        }

        public string[,] LoadDataForChartByWeek()
        {
            string query = @"
                SELECT 
                    strftime('%Y-%W', date) as week,
                    MAX(keyboard_press) as max_keyboard,
                    MAX(mouse_left_click) as max_mouse_left,
                    MAX(mouse_right_click) as max_mouse_right
                FROM click_data
                GROUP BY week
                ORDER BY week ASC
            ";

            string countQuery = @"
                SELECT COUNT(DISTINCT strftime('%Y-%W', date)) as count
                FROM click_data
            ";

            int rowCount = 0;
            using (SQLiteCommand countCommand = new SQLiteCommand(countQuery, _connection))
            {
                object result = countCommand.ExecuteScalar();
                if (result != null)
                {
                    rowCount = Convert.ToInt32(result);
                }
            }

            string[,] resultArray = new string[rowCount, 4];

            using (SQLiteCommand command = new SQLiteCommand(query, _connection))
            {
                using (SQLiteDataReader reader = command.ExecuteReader())
                {
                    int index = 0;
                    while (reader.Read() && index < rowCount)
                    {
                        resultArray[index, 0] = reader["week"].ToString();
                        resultArray[index, 1] = reader["max_keyboard"].ToString();
                        resultArray[index, 2] = reader["max_mouse_left"].ToString();
                        resultArray[index, 3] = reader["max_mouse_right"].ToString();
                        index++;
                    }
                }
            }

            return resultArray;
        }

        public string[,] LoadLastYearDataByWeek()
        {
            string oneYearAgo = DateTime.Now.AddYears(-1).ToString("yyyy-MM-dd");

            string query = @"
                SELECT 
                    strftime('%Y-%W', date) as week,
                    MAX(keyboard_press) as max_keyboard,
                    MAX(mouse_left_click) as max_mouse_left,
                    MAX(mouse_right_click) as max_mouse_right
                FROM click_data
                WHERE date >= @oneYearAgo
                GROUP BY week
                ORDER BY week ASC
            ";

            string countQuery = @"
                SELECT COUNT(DISTINCT strftime('%Y-%W', date)) as count
                FROM click_data
                WHERE date >= @oneYearAgo
            ";

            int rowCount = 0;
            using (SQLiteCommand countCommand = new SQLiteCommand(countQuery, _connection))
            {
                countCommand.Parameters.AddWithValue("@oneYearAgo", oneYearAgo);
                object result = countCommand.ExecuteScalar();
                if (result != null)
                {
                    rowCount = Convert.ToInt32(result);
                }
            }

            string[,] resultArray = new string[rowCount, 4];

            using (SQLiteCommand command = new SQLiteCommand(query, _connection))
            {
                command.Parameters.AddWithValue("@oneYearAgo", oneYearAgo);
                using (SQLiteDataReader reader = command.ExecuteReader())
                {
                    int index = 0;
                    while (reader.Read() && index < rowCount)
                    {
                        resultArray[index, 0] = reader["week"].ToString();
                        resultArray[index, 1] = reader["max_keyboard"].ToString();
                        resultArray[index, 2] = reader["max_mouse_left"].ToString();
                        resultArray[index, 3] = reader["max_mouse_right"].ToString();
                        index++;
                    }
                }
            }

            return resultArray;
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

            string insertQuery = @"
                INSERT OR REPLACE INTO click_data (date, keyboard_press, mouse_left_click, mouse_right_click)
                VALUES (@date, @keyboardPress, @leftClick, @rightClick);
            ";

            using (SQLiteCommand command = new SQLiteCommand(insertQuery, _connection))
            {
                command.Parameters.AddWithValue("@date", date.ToString("yyyy-MM-dd"));
                command.Parameters.AddWithValue("@keyboardPress", keyboardPress);
                command.Parameters.AddWithValue("@leftClick", leftClick);
                command.Parameters.AddWithValue("@rightClick", rightClick);
                command.ExecuteNonQuery();
            }
        }

        public void SaveKeyDistribution(System.Collections.Generic.Dictionary<int, int> keyDistribution)
        {
            var keyDistributionCopy = new System.Collections.Generic.Dictionary<int, int>(keyDistribution);

            using (var transaction = _connection.BeginTransaction())
            {
                try
                {
                    foreach (var kvp in keyDistributionCopy)
                    {
                        int keyCode = kvp.Key;
                        int pressCount = kvp.Value;
                        string keyName = GetKeyName(keyCode);

                        string insertQuery = @"
                            INSERT OR REPLACE INTO key_distribution (key_code, key_name, press_count)
                            VALUES (@keyCode, @keyName, @pressCount);
                        ";

                        using (SQLiteCommand command = new SQLiteCommand(insertQuery, _connection))
                        {
                            command.Parameters.AddWithValue("@keyCode", keyCode);
                            command.Parameters.AddWithValue("@keyName", keyName);
                            command.Parameters.AddWithValue("@pressCount", pressCount);
                            command.ExecuteNonQuery();
                        }
                    }

                    transaction.Commit();
                }
                catch
                {
                    transaction.Rollback();
                    throw;
                }
            }
        }

        public System.Collections.Generic.Dictionary<int, int> LoadKeyDistribution()
        {
            System.Collections.Generic.Dictionary<int, int> keyDistribution = new System.Collections.Generic.Dictionary<int, int>();

            string query = "SELECT key_code, press_count FROM key_distribution";
            using (SQLiteCommand command = new SQLiteCommand(query, _connection))
            {
                using (SQLiteDataReader reader = command.ExecuteReader())
                {
                    while (reader.Read())
                    {
                        int keyCode = Convert.ToInt32(reader["key_code"]);
                        int pressCount = Convert.ToInt32(reader["press_count"]);
                        keyDistribution[keyCode] = pressCount;
                    }
                }
            }

            return keyDistribution;
        }

        public System.Collections.Generic.List<System.Collections.Generic.KeyValuePair<string, int>> GetKeyDistributionDescending()
        {
            System.Collections.Generic.List<System.Collections.Generic.KeyValuePair<string, int>> keyDistributionList = new System.Collections.Generic.List<System.Collections.Generic.KeyValuePair<string, int>>();

            string query = "SELECT key_name, press_count FROM key_distribution ORDER BY press_count DESC";
            using (SQLiteCommand command = new SQLiteCommand(query, _connection))
            {
                using (SQLiteDataReader reader = command.ExecuteReader())
                {
                    while (reader.Read())
                    {
                        string keyName = reader["key_name"].ToString();
                        int pressCount = Convert.ToInt32(reader["press_count"]);
                        keyDistributionList.Add(new System.Collections.Generic.KeyValuePair<string, int>(keyName, pressCount));
                    }
                }
            }

            return keyDistributionList;
        }

        private string GetKeyName(int keyCode)
        {
            try
            {
                System.Windows.Forms.KeysConverter converter = new System.Windows.Forms.KeysConverter();
                return converter.ConvertToString(keyCode);
            }
            catch
            {
                return keyCode.ToString();
            }
        }

        public SQLiteConnection GetConnection()
        {
            return _connection;
        }

        public string GetConnectionString()
        {
            return $"Data Source={DataFileName};Version=3;";
        }

        public void CloseConnection()
        {
            if (_connection != null && _connection.State == System.Data.ConnectionState.Open)
            {
                _connection.Close();
            }
        }
    }
}
