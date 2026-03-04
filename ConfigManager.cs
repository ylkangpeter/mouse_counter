using System;
using System.IO;
using System.Text.Json;

namespace MouseClickRecorder
{
    public class ConfigManager
    {
        private string ConfigFilePath { get; set; }
        public Config Config { get; private set; }

        public ConfigManager()
        {
            ConfigFilePath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "config.json");
            LoadConfig();
        }

        private void LoadConfig()
        {
            if (File.Exists(ConfigFilePath))
            {
                try
                {
                    string json = File.ReadAllText(ConfigFilePath);
                    Config = JsonSerializer.Deserialize<Config>(json) ?? new Config();
                }
                catch (Exception ex)
                {
                    Logger.Instance().Log($"Error loading config: {ex.Message}");
                    Config = new Config();
                }
            }
            else
            {
                Config = new Config();
                SaveConfig();
            }
        }

        public void SaveConfig()
        {
            try
            {
                string json = JsonSerializer.Serialize(Config, new JsonSerializerOptions { WriteIndented = true });
                File.WriteAllText(ConfigFilePath, json);
            }
            catch (Exception ex)
            {
                Logger.Instance().Log($"Error saving config: {ex.Message}");
            }
        }
    }

    public class Config
    {
        public int SyncInterval { get; set; } = 10000; // 默认10秒
        public int SyncEventThreshold { get; set; } = 50; // 默认50个事件
        public int MaxDaysToShow { get; set; } = 365; // 最大显示天数
    }
}