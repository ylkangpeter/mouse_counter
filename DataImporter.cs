using System;
using System.Collections.Generic;
using System.IO;
using System.Data.SQLite;

namespace MouseClickRecorder
{
    public class DataImporter
    {
        private SQLiteConnection _connection;
        private FileManager _fileManager;

        public DataImporter(FileManager fileManager)
        {
            _fileManager = fileManager;
            _connection = fileManager.GetConnection();
        }

        public ImportResult ImportHistoricalData(string filePath)
        {
            ImportResult result = new ImportResult();

            if (!File.Exists(filePath))
            {
                result.Success = false;
                result.ErrorMessage = "Historical data file not found.";
                return result;
            }

            // 开始事务
            using (var transaction = _connection.BeginTransaction())
            {
                try
                {
                    // 首先获取所有已存在的日期，避免重复查询
                    var existingDates = new HashSet<string>();
                    string checkDatesQuery = "SELECT date FROM click_data";
                    using (var command = new SQLiteCommand(checkDatesQuery, _connection, transaction))
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

                                    using (var command = new SQLiteCommand(insertQuery, _connection, transaction))
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
                                        result.HomeKeyPresses += keyboardPress;
                                    }

                                    result.ImportedCount++;
                                }
                                else
                                {
                                    result.SkippedCount++;
                                }
                            }
                        }
                    }

                    // 批量更新Home键的点击次数
                    if (result.HomeKeyPresses > 0)
                    {
                        // 先查询当前Home键的点击次数
                        int existingHomeCount = 0;
                        string checkHomeQuery = "SELECT press_count FROM key_distribution WHERE key_code = @keyCode";
                        using (var command = new SQLiteCommand(checkHomeQuery, _connection, transaction))
                        {
                            command.Parameters.AddWithValue("@keyCode", 0x24); // Home键
                            object dbResult = command.ExecuteScalar();
                            if (dbResult != null && dbResult != DBNull.Value)
                            {
                                existingHomeCount = Convert.ToInt32(dbResult);
                            }
                        }

                        // 插入或更新Home键数据
                        string updateHomeQuery = @"
                            INSERT OR REPLACE INTO key_distribution (key_code, key_name, press_count)
                            VALUES (@keyCode, @keyName, @pressCount);
                        ";

                        using (var command = new SQLiteCommand(updateHomeQuery, _connection, transaction))
                        {
                            command.Parameters.AddWithValue("@keyCode", 0x24); // Home键
                            command.Parameters.AddWithValue("@keyName", "Home");
                            command.Parameters.AddWithValue("@pressCount", existingHomeCount + result.HomeKeyPresses);
                            command.ExecuteNonQuery();
                        }
                    }

                    // 提交事务
                    transaction.Commit();
                    result.Success = true;
                    Logger.Instance().Log($"Import completed: {result.ImportedCount} records imported, {result.SkippedCount} records skipped, {result.HomeKeyPresses} Home key presses added");
                }
                catch (Exception ex)
                {
                    // 回滚事务
                    transaction.Rollback();
                    result.Success = false;
                    result.ErrorMessage = ex.Message;
                    Logger.Instance().Log($"Import failed: {ex.Message}");
                }
            }

            return result;
        }
    }

    public class ImportResult
    {
        public bool Success { get; set; }
        public string ErrorMessage { get; set; }
        public int ImportedCount { get; set; }
        public int SkippedCount { get; set; }
        public int HomeKeyPresses { get; set; }

        public ImportResult()
        {
            Success = false;
            ErrorMessage = string.Empty;
            ImportedCount = 0;
            SkippedCount = 0;
            HomeKeyPresses = 0;
        }
    }
}