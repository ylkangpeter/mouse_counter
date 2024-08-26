using System;
using System.IO;
using System.Windows.Forms;

namespace MouseClickRecorder
{
    public class Logger
    {
        private static Logger _instance;
        private static readonly object _lock = new object();
        private StreamWriter _writer;
        private string _logFilePath;

        // 私有构造函数，防止外部实例化
        private Logger(string logFilePath)
        {
            _logFilePath = logFilePath;
            InitializeWriter();
        }

        // 单例访问点
        public static Logger Instance(string logFilePath = null)
        {
            if (_instance == null)
            {
                lock (_lock)
                {
                    if (_instance == null)
                    {
                        if (logFilePath == null)
                        {
                            logFilePath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "log.txt");
                        }
                        _instance = new Logger(logFilePath);
                    }
                }
            }
            return _instance;
        }

        private void InitializeWriter()
        {
            if (FileManager.IsFileLocked(_logFilePath))
            {
                MessageBox.Show("Data file is currently in use by another process. Application will not start.", "Error", MessageBoxButtons.OK, MessageBoxIcon.Error);
                Application.Exit(); // Exit application
            }
            try
            {
                _writer = new StreamWriter(_logFilePath, true);
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Error initializing log file writer: {ex.Message}");
            }
        }

        public void Log(string message)
        {
            try
            {
                if (_writer == null)
                {
                    InitializeWriter();
                }
                _writer.WriteLine($"{DateTime.Now}: {message}");
                _writer.Flush(); // Ensure the log message is written immediately
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Error writing to log file: {ex.Message}");
            }
        }

        // 释放资源
        public void Dispose()
        {
            if (_writer != null)
            {
                _writer.Close();
                _writer.Dispose();
            }
        }
    }
}
