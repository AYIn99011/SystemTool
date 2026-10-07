using System.IO;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Data;
using Microsoft.Win32;
using SystemTool.Services;

namespace SystemTool.Pages
{
    public partial class LogPage : Page
    {
        private readonly CollectionViewSource _logViewSource = new();
        private readonly ToggleButton[] _filterButtons;
        private LogLevel? _filterLevel; // null = 全部

        public LogPage()
        {
            InitializeComponent();

            _filterButtons = new[] { FilterAllBtn, FilterInfoBtn, FilterSuccessBtn, FilterWarningBtn, FilterErrorBtn };

            _logViewSource.Source = LogService.Instance.Logs;
            _logViewSource.Filter += LogViewFilter;
            LogListBox.ItemsSource = _logViewSource.View;

            LogService.Instance.Logs.CollectionChanged += (_, _) => UpdateStats();
            UpdateStats();
        }

        private void LogViewFilter(object sender, FilterEventArgs e)
        {
            if (_filterLevel == null || e.Item is not LogEntry entry)
            {
                e.Accepted = true;
                return;
            }
            e.Accepted = entry.Level == _filterLevel.Value;
        }

        private void FilterButton_Checked(object sender, RoutedEventArgs e)
        {
            // XAML 初始化期间 IsChecked="True" 会提前触发，此时字段尚未赋值，直接忽略
            if (_filterButtons == null || _logViewSource.View == null) return;

            var clicked = (ToggleButton)sender;
            foreach (var b in _filterButtons)
                if (!ReferenceEquals(b, clicked))
                    b.IsChecked = false;

            _filterLevel = (clicked.Tag as string) switch
            {
                "Info" => LogLevel.Info,
                "Success" => LogLevel.Success,
                "Warning" => LogLevel.Warning,
                "Error" => LogLevel.Error,
                _ => null,
            };
            _logViewSource.View.Refresh();
            UpdateEmptyHint();
        }

        private void FilterButton_Unchecked(object sender, RoutedEventArgs e)
        {
            if (_filterButtons == null) return;
            // 单选：不允许全部取消，保持一个选中
            if (_filterButtons.All(b => b.IsChecked != true))
                ((ToggleButton)sender).IsChecked = true;
        }

        private void UpdateStats()
        {
            var logs = LogService.Instance.Logs;
            TotalCountText.Text = logs.Count.ToString();
            InfoCountText.Text = logs.Count(l => l.Level == LogLevel.Info).ToString();
            SuccessCountText.Text = logs.Count(l => l.Level == LogLevel.Success).ToString();
            WarningCountText.Text = logs.Count(l => l.Level == LogLevel.Warning).ToString();
            ErrorCountText.Text = logs.Count(l => l.Level == LogLevel.Error).ToString();
            UpdateEmptyHint();
        }

        private void UpdateEmptyHint()
        {
            bool hasAny = _filterLevel == null
                || LogService.Instance.Logs.Any(l => l.Level == _filterLevel.Value);
            EmptyHintText.Visibility = hasAny ? Visibility.Collapsed : Visibility.Visible;
        }

        private void ClearLogs_Click(object sender, RoutedEventArgs e)
        {
            LogService.Instance.Clear();
        }

        private void ExportLogs_Click(object sender, RoutedEventArgs e)
        {
            var saveDialog = new SaveFileDialog
            {
                Filter = "文本文件 (*.txt)|*.txt|所有文件 (*.*)|*.*",
                DefaultExt = ".txt",
                FileName = $"SystemTool_Log_{DateTime.Now:yyyyMMdd_HHmmss}.txt",
                Title = "导出日志"
            };

            if (saveDialog.ShowDialog() == true)
            {
                try
                {
                    using (var writer = new StreamWriter(saveDialog.FileName, false, System.Text.Encoding.UTF8))
                    {
                        writer.WriteLine("========================================");
                        writer.WriteLine($"SystemTool 应用日志");
                        writer.WriteLine($"导出时间: {DateTime.Now:yyyy-MM-dd HH:mm:ss}");
                        writer.WriteLine("========================================");
                        writer.WriteLine();

                        foreach (var log in LogService.Instance.Logs)
                        {
                            string levelText = log.Level switch
                            {
                                LogLevel.Success => "[成功]",
                                LogLevel.Warning => "[警告]",
                                LogLevel.Error => "[错误]",
                                _ => "[信息]"
                            };
                            writer.WriteLine($"[{log.Timestamp}] {levelText} {log.Message}");
                        }

                        writer.WriteLine();
                        writer.WriteLine("========================================");
                        writer.WriteLine($"共 {LogService.Instance.Logs.Count} 条日志");
                        writer.WriteLine("========================================");
                    }

                    LogService.Instance.Success($"日志已导出到: {saveDialog.FileName}");
                    MessageBox.Show($"日志已成功导出到:\n{saveDialog.FileName}", "导出成功", MessageBoxButton.OK, MessageBoxImage.Information);
                }
                catch (Exception ex)
                {
                    LogService.Instance.Error($"导出日志失败: {ex.Message}");
                    MessageBox.Show($"导出日志失败:\n{ex.Message}", "导出失败", MessageBoxButton.OK, MessageBoxImage.Error);
                }
            }
        }
    }
}
