using System.Diagnostics;
using System.Windows;
using System.Windows.Controls;

namespace SystemTool.Pages;

public partial class RepairPage : Page
{
    public RepairPage()
    {
        InitializeComponent();
    }

    private void RunSfcScan_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            var process = new Process
            {
                StartInfo = new ProcessStartInfo
                {
                    FileName = "cmd.exe",
                    Arguments = "/c sfc /scannow && pause",
                    UseShellExecute = true,
                    Verb = "runas",
                    WindowStyle = ProcessWindowStyle.Normal
                }
            };
            process.Start();
        }
        catch
        {
            MessageBox.Show("无法启动SFC扫描，请确保以管理员权限运行。", "错误", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    private void RunDismRepair_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            var process = new Process
            {
                StartInfo = new ProcessStartInfo
                {
                    FileName = "cmd.exe",
                    Arguments = "/c DISM /Online /Cleanup-Image /RestoreHealth && pause",
                    UseShellExecute = true,
                    Verb = "runas",
                    WindowStyle = ProcessWindowStyle.Normal
                }
            };
            process.Start();
        }
        catch
        {
            MessageBox.Show("无法启动DISM修复，请确保以管理员权限运行。", "错误", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    private void DownloadDllTool_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            Process.Start(new ProcessStartInfo
            {
                FileName = "https://img.kookapp.cn/attachments/2025-05/19/682b34c651cb8.zip",
                UseShellExecute = true
            });
        }
        catch
        {
            MessageBox.Show("无法打开下载链接。", "错误", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }
}
