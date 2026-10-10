using System.Diagnostics;
using System.IO;
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
            using var process = new Process
            {
                StartInfo = new ProcessStartInfo
                {
                    FileName = "cmd.exe",
                    Arguments = "/c sfc /scannow && pause",
                    UseShellExecute = true,
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
            using var process = new Process
            {
                StartInfo = new ProcessStartInfo
                {
                    FileName = "cmd.exe",
                    Arguments = "/c DISM /Online /Cleanup-Image /RestoreHealth && pause",
                    UseShellExecute = true,
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
            using (Process.Start(new ProcessStartInfo
            {
                FileName = "https://img.kookapp.cn/attachments/2025-05/19/682b34c651cb8.zip",
                UseShellExecute = true
            })) { }
        }
        catch
        {
            MessageBox.Show("无法打开下载链接。", "错误", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    private void NetworkRepair_Click(object sender, RoutedEventArgs e)
    {
        // 先在进程内关闭系统代理（HKCU，无需管理员）
        try
        {
            using var key = Microsoft.Win32.Registry.CurrentUser.OpenSubKey(
                @"Software\Microsoft\Windows\CurrentVersion\Internet Settings", writable: true);
            if (key != null)
            {
                key.SetValue("ProxyEnable", 0, Microsoft.Win32.RegistryValueKind.DWord);
                Services.LogService.Instance.Info("网络修复: 已关闭系统代理 (ProxyEnable=0)");
            }
        }
        catch (Exception ex)
        {
            Services.LogService.Instance.Warning("[RepairPage.NetworkRepair_Click] 关闭系统代理失败", ex);
        }

        // 再以管理员身份跑 netsh / ipconfig（需要提权，沿用本页其它修复项的弹窗 cmd 风格）
        try
        {
            using var process = new Process
            {
                StartInfo = new ProcessStartInfo
                {
                    FileName = "cmd.exe",
                    Arguments = "/c netsh winsock reset & ipconfig /flushdns & netsh int ip reset & echo. & echo 网络修复完成: Winsock/DNS/IP 已重置，系统代理已关闭，部分设置需重启电脑生效 & pause",
                    UseShellExecute = true,
                    WindowStyle = ProcessWindowStyle.Normal
                }
            };
            process.Start();
            Services.LogService.Instance.Info("网络修复: 已启动 Winsock/DNS/IP 重置");
        }
        catch (Exception ex)
        {
            Services.LogService.Instance.Warning("[RepairPage.NetworkRepair_Click] 启动网络修复失败", ex);
            MessageBox.Show("无法启动网络修复，请确保以管理员权限运行。", "错误", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

        private async void RepairIconCache_Click(object sender, RoutedEventArgs e)
    {
        var confirm = MessageBox.Show(
            "将删除图标缓存并重启资源管理器（桌面会闪一下），确定继续吗？",
            "桌面图标异常修复", MessageBoxButton.YesNo, MessageBoxImage.Question);
        if (confirm != MessageBoxResult.Yes) return;

        var cleanService = new Services.CleanService();
        try
        {
            var result = await Task.Run(() =>
            {
                long totalSize = 0;
                int fileCount = 0;

                string localAppData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
                string iconCacheFile = Path.Combine(localAppData, "IconCache.db");
                string iconCacheDir = Path.Combine(localAppData, "Microsoft", "Windows", "Explorer");

                Services.LogService.Instance.Info($"[桌面图标异常修复] 清理: {iconCacheFile}");
                if (File.Exists(iconCacheFile))
                {
                    try
                    {
                        var fileInfo = new FileInfo(iconCacheFile);
                        totalSize += fileInfo.Length;
                        File.Delete(iconCacheFile);
                        fileCount++;
                    }
                    catch (Exception ex)
                    {
                        Services.LogService.Instance.Warning($"[桌面图标异常修复] IconCache.db 删除失败 - {ex.Message}");
                    }
                }

                if (Directory.Exists(iconCacheDir))
                {
                    foreach (var pattern in new[] { "iconcache_*.db", "thumbcache_*.db" })
                    {
                        foreach (var file in Directory.GetFiles(iconCacheDir, pattern))
                        {
                            try
                            {
                                var fileInfo = new FileInfo(file);
                                totalSize += fileInfo.Length;
                                File.Delete(file);
                                fileCount++;
                            }
                            catch (Exception ex)
                            {
                                Services.LogService.Instance.Warning($"[桌面图标异常修复] 删除失败 {file} - {ex.Message}");
                            }
                        }
                    }
                }

                Services.LogService.Instance.Info("[桌面图标异常修复] 正在重启资源管理器...");
                bool explorerOk = SystemTool.Helpers.SystemInfoHelper.RestartExplorer("桌面图标异常修复");
                if (!explorerOk)
                {
                    Services.LogService.Instance.Error("[桌面图标异常修复] 资源管理器重启失败");
                }

                return (totalSize, fileCount, explorerOk);
            });

            Services.LogService.Instance.Success(
                $"桌面图标异常修复完成：删除 {result.fileCount} 个缓存文件，释放 {cleanService.FormatSize(result.totalSize)}" +
                (result.explorerOk ? "，资源管理器已重启" : "，但资源管理器重启失败：请按 Ctrl+Shift+Esc 打开任务管理器，运行 explorer.exe 手动恢复"));
            MessageBox.Show(
                $"修复完成：删除 {result.fileCount} 个缓存文件，释放 {cleanService.FormatSize(result.totalSize)}。" +
                (result.explorerOk ? "" : "\n\n注意：资源管理器重启失败，请按 Ctrl+Shift+Esc 打开任务管理器，运行 explorer.exe 手动恢复桌面。"),
                "桌面图标异常修复", MessageBoxButton.OK, result.explorerOk ? MessageBoxImage.Information : MessageBoxImage.Warning);
        }
        catch (Exception ex)
        {
            Services.LogService.Instance.Error($"[桌面图标异常修复] 执行失败: {ex.Message}");
            MessageBox.Show("修复失败，请确保以管理员权限运行。", "错误", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }
}
