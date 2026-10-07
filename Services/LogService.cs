using System;
using System.Collections.ObjectModel;
using System.Windows;

namespace SystemTool.Services
{
    public class LogService
    {
        private static readonly Lazy<LogService> _instance = new(() => new LogService());
        public static LogService Instance => _instance.Value;

        private ObservableCollection<LogEntry>? _logs;
        private ObservableCollection<UpdateLogEntry>? _updateLogs;
        
        public ObservableCollection<LogEntry> Logs => _logs ??= new ObservableCollection<LogEntry>();
        public ObservableCollection<UpdateLogEntry> UpdateLogs => _updateLogs ??= new ObservableCollection<UpdateLogEntry>();

        private LogService() { }

        public void Log(string message, LogLevel level = LogLevel.Info)
        {
            Application.Current?.Dispatcher.BeginInvoke(() =>
            {
                Logs.Insert(0, new LogEntry
                {
                    Timestamp = DateTime.Now.ToString("HH:mm:ss"),
                    Message = message,
                    Level = level
                });

                if (Logs.Count > 200)
                {
                    Logs.RemoveAt(Logs.Count - 1);
                }
            });
        }

        public void Info(string message) => Log(message, LogLevel.Info);
        public void Success(string message) => Log(message, LogLevel.Success);
        public void Warning(string message) => Log(message, LogLevel.Warning);
        public void Error(string message) => Log(message, LogLevel.Error);

        public void Warning(string message, Exception? ex) =>
            Log($"{message}（{ex?.GetType().Name}: {ex?.Message}）", LogLevel.Warning);

        public void Error(string message, Exception? ex) =>
            Log($"{message}（{ex?.GetType().Name}: {ex?.Message}）", LogLevel.Error);

        public void AddUpdateLog(string version, string content)
        {
            Application.Current?.Dispatcher.BeginInvoke(() =>
            {
                UpdateLogs.Insert(0, new UpdateLogEntry
                {
                    Date = DateTime.Now.ToString("yyyy-MM-dd"),
                    Version = version,
                    Content = content
                });
            });
        }

        public void Clear()
        {
            Application.Current?.Dispatcher.BeginInvoke(() =>
            {
                _logs?.Clear();
            });
        }

        public void ClearUpdateLogs()
        {
            Application.Current?.Dispatcher.BeginInvoke(() =>
            {
                _updateLogs?.Clear();
            });
        }
    }

    public class LogEntry
    {
        public string Timestamp { get; set; } = "";
        public string Message { get; set; } = "";
        public LogLevel Level { get; set; }
    }

    public class UpdateLogEntry
    {
        public string Date { get; set; } = "";
        public string Version { get; set; } = "";
        public string Content { get; set; } = "";
    }

    public enum LogLevel
    {
        Info,
        Success,
        Warning,
        Error
    }
}
