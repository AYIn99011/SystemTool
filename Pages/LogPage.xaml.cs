using System.IO;
using System.Windows;
using System.Windows.Controls;
using Microsoft.Win32;
using SystemTool.Services;

namespace SystemTool.Pages
{
    public partial class LogPage : Page
    {
        public LogPage()
        {
            InitializeComponent();
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
