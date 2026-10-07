using System.Diagnostics;
using System.IO;
using System.IO.Compression;
using System.Reflection;
using System.Windows;
using System.Windows.Controls;
using SystemTool.Services;

namespace SystemTool.Pages;

public partial class ToolsPage : Page
{
    private static readonly object _lockObj = new();
    private static bool _isExtracting = false;

    public ToolsPage()
    {
        InitializeComponent();
    }

    public static void CleanupExtractedFiles()
    {
        try
        {
            string appPath = AppDomain.CurrentDomain.BaseDirectory;
            string toolPath = Path.Combine(appPath, "Win11Debloat");

            if (Directory.Exists(toolPath))
            {
                Directory.Delete(toolPath, true);
                LogService.Instance.Info("已清理 Win11Debloat 临时文件");
            }

            string geekPath = Path.Combine(Path.GetTempPath(), "GEEK.exe");
            if (File.Exists(geekPath))
            {
                File.Delete(geekPath);
                LogService.Instance.Info("已清理 GEEK.exe 临时文件");
            }
        }
        catch (Exception ex)
        {
            LogService.Instance.Warning($"清理临时文件失败: {ex.Message}");
        }
    }

    private void OpenGeekUninstaller_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            var tempPath = Path.Combine(Path.GetTempPath(), "GEEK.exe");

            var assembly = typeof(ToolsPage).Assembly;
            using (var stream = assembly.GetManifestResourceStream("GEEK.exe"))
            {
                if (stream != null)
                {
                    using (var fileStream = File.Create(tempPath))
                    {
                        stream.CopyTo(fileStream);
                    }

                    Process.Start(new ProcessStartInfo
                    {
                        FileName = tempPath,
                        UseShellExecute = true
                    });
                    LogService.Instance.Info("已启动Geek卸载工具");
                }
                else
                {
                    MessageBox.Show("未找到内置的GEEK.exe资源。", "错误", MessageBoxButton.OK, MessageBoxImage.Error);
                    LogService.Instance.Error("未找到内置的GEEK.exe资源");
                }
            }
        }
        catch (Exception ex)
        {
            MessageBox.Show($"启动卸载工具失败: {ex.Message}", "错误", MessageBoxButton.OK, MessageBoxImage.Error);
            LogService.Instance.Error($"启动Geek卸载工具失败: {ex.Message}");
        }
    }

    private void RunWin11Debloat_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            string appPath = AppDomain.CurrentDomain.BaseDirectory;
            string toolPath = Path.Combine(appPath, "Win11Debloat");
            string batPath = Path.Combine(toolPath, "Run.bat");

            if (!Directory.Exists(toolPath) || !File.Exists(batPath))
            {
                ExtractWin11Debloat(appPath);
            }

            if (File.Exists(batPath))
            {
                var process = new Process
                {
                    StartInfo = new ProcessStartInfo
                    {
                        FileName = batPath,
                        UseShellExecute = true,
                        WindowStyle = ProcessWindowStyle.Normal,
                        WorkingDirectory = toolPath
                    }
                };
                process.Start();
            }
            else
            {
                MessageBox.Show("Win11Debloat 工具解压失败，请重试。", "错误", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }
        catch (Exception ex)
        {
            MessageBox.Show($"无法启动 Win11Debloat 工具：{ex.Message}", "错误", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    private void ExtractWin11Debloat(string appPath)
    {
        lock (_lockObj)
        {
            if (_isExtracting)
            {
                MessageBox.Show("正在解压工具，请稍候...", "提示", MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }

            _isExtracting = true;
            try
            {
                string toolPath = Path.Combine(appPath, "Win11Debloat");
                string zipPath = Path.Combine(appPath, "Win11Debloat.zip");

                if (File.Exists(zipPath))
                {
                    if (Directory.Exists(toolPath))
                    {
                        Directory.Delete(toolPath, true);
                    }
                    ZipFile.ExtractToDirectory(zipPath, toolPath);
                    return;
                }

                var assembly = Assembly.GetExecutingAssembly();
                var resourceNames = assembly.GetManifestResourceNames();
                string? zipResourceName = null;

                foreach (var name in resourceNames)
                {
                    if (name.EndsWith("Win11Debloat.zip"))
                    {
                        zipResourceName = name;
                        break;
                    }
                }

                if (zipResourceName == null)
                {
                    throw new FileNotFoundException("未找到 Win11Debloat.zip 嵌入资源");
                }

                if (Directory.Exists(toolPath))
                {
                    Directory.Delete(toolPath, true);
                }

                using (var stream = assembly.GetManifestResourceStream(zipResourceName))
                {
                    if (stream == null)
                    {
                        throw new InvalidOperationException("无法读取嵌入资源");
                    }

                    using (var archive = new ZipArchive(stream))
                    {
                        archive.ExtractToDirectory(toolPath);
                    }
                }
            }
            finally
            {
                _isExtracting = false;
            }
        }
    }
}
