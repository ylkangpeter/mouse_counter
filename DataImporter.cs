using System;
using System.Collections.Generic;
using System.Data.SQLite;
using System.IO;

namespace MouseClickRecorder
{
    public class DataImporter
    {
        private readonly FileManager _fileManager;

        public DataImporter(FileManager fileManager)
        {
            _fileManager = fileManager;
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

            using (var connection = new SQLiteConnection(_fileManager.GetConnectionString()))
            {
                connection.Open();

                using (var transaction = connection.BeginTransaction())
                {
                    try
                    {
                        var existingDates = new HashSet<string>();
                        string checkDatesQuery = "SELECT date FROM click_data";
                        using (var command = new SQLiteCommand(checkDatesQuery, connection, transaction))
                        using (var reader = command.ExecuteReader())
                        {
                            while (reader.Read())
                            {
                                existingDates.Add(reader["date"].ToString());
                            }
                        }

                        using (StreamReader sr = new StreamReader(filePath))
                        {
                            string line;
                            while ((line = sr.ReadLine()) != null)
                            {
                                var parts = line.Split(',');
                                if (parts.Length != 4)
                                {
                                    continue;
                                }

                                DateTime date = DateTime.Parse(parts[0]);
                                string dateStr = date.ToString("yyyy-MM-dd");
                                if (existingDates.Contains(dateStr))
                                {
                                    result.SkippedCount++;
                                    continue;
                                }

                                int keyboardPress = int.Parse(parts[1]);
                                int leftClick = int.Parse(parts[2]);
                                int rightClick = int.Parse(parts[3]);

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

                                if (keyboardPress > 0)
                                {
                                    result.HomeKeyPresses += keyboardPress;
                                }

                                result.ImportedCount++;
                                existingDates.Add(dateStr);
                            }
                        }

                        if (result.HomeKeyPresses > 0)
                        {
                            int existingHomeCount = 0;
                            string checkHomeQuery = "SELECT press_count FROM key_distribution WHERE key_code = @keyCode";
                            using (var command = new SQLiteCommand(checkHomeQuery, connection, transaction))
                            {
                                command.Parameters.AddWithValue("@keyCode", 0x24);
                                object dbResult = command.ExecuteScalar();
                                if (dbResult != null && dbResult != DBNull.Value)
                                {
                                    existingHomeCount = Convert.ToInt32(dbResult);
                                }
                            }

                            string updateHomeQuery = @"
                                INSERT OR REPLACE INTO key_distribution (key_code, key_name, press_count)
                                VALUES (@keyCode, @keyName, @pressCount);
                            ";

                            using (var command = new SQLiteCommand(updateHomeQuery, connection, transaction))
                            {
                                command.Parameters.AddWithValue("@keyCode", 0x24);
                                command.Parameters.AddWithValue("@keyName", "Home");
                                command.Parameters.AddWithValue("@pressCount", existingHomeCount + result.HomeKeyPresses);
                                command.ExecuteNonQuery();
                            }
                        }

                        transaction.Commit();
                        result.Success = true;
                        Logger.Instance().Log($"Import completed: {result.ImportedCount} records imported, {result.SkippedCount} records skipped, {result.HomeKeyPresses} Home key presses added");
                    }
                    catch (Exception ex)
                    {
                        transaction.Rollback();
                        result.Success = false;
                        result.ErrorMessage = ex.Message;
                        Logger.Instance().Log($"Import failed: {ex.Message}");
                    }
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
