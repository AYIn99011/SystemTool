using System.Diagnostics;
using System.IO;
using System.IO.Compression;
using System.Reflection;
using System.Security.Cryptography;
using System.Windows;
using System.Windows.Controls;
using SystemTool.Services;
using SystemTool.Windows;

namespace SystemTool.Pages;

public partial class ToolsPage : Page
{
    /// <summary>内嵌 GEEK.exe 的 SHA256 期望值；运行前先校验，对不上则提示工具损坏并不启动。</summary>
    private const string ExpectedGeekSha256 = "231F2B6767F917D418C16685DA03E3C4E5D505A5793554C8AF76590E203A2F13";

    /// <summary>内嵌 Win11Debloat.zip 的 SHA256 期望值；运行前先校验。</summary>
    private const string ExpectedDebloatZipSha256 = "6AD7B43AB3068ED588DBBE100DEB490EB8CBC4B8421EB72E3B492CE066CC9DA6";

    /// <summary>Win11Debloat.zip 内 Run.bat 的 SHA256 期望值；解压后校验，通过才执行。</summary>
    private const string ExpectedDebloatRunBatSha256 = "BA11528F47CB8A945D1EE48D13FF342FB2319656B021404C9D3B7099B0AE6DBD";

    private bool _isOperating;

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

            // GEEK 临时文件使用唯一文件名，退出时按通配符清理（含崩溃残留）
            foreach (var f in Directory.GetFiles(Path.GetTempPath(), "GEEK-*.exe"))
            {
                try
                {
                    File.Delete(f);
                    LogService.Instance.Info($"已清理 GEEK.exe 临时文件: {Path.GetFileName(f)}");
                }
                catch { }
            }
            // 兼容旧版本固定文件名残留
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
            var tempPath = Path.Combine(Path.GetTempPath(), $"GEEK-{Guid.NewGuid():N}.exe");

            var assembly = typeof(ToolsPage).Assembly;
            using (var stream = assembly.GetManifestResourceStream("GEEK.exe"))
            {
                if (stream == null)
                {
                    MessageBox.Show("未找到内置的GEEK.exe资源。", "错误", MessageBoxButton.OK, MessageBoxImage.Error);
                    LogService.Instance.Error("未找到内置的GEEK.exe资源");
                    return;
                }

                // 先验证，后运行：校验内嵌资源哈希
                byte[] data;
                using (var ms = new MemoryStream())
                {
                    stream.CopyTo(ms);
                    data = ms.ToArray();
                }

                string actualHash = Convert.ToHexString(SHA256.HashData(data));
                if (!string.Equals(actualHash, ExpectedGeekSha256, StringComparison.OrdinalIgnoreCase))
                {
                    MessageBox.Show("内置的卸载工具已损坏（哈希校验失败），无法启动。\n请重新下载安装系统工具箱。",
                        "工具损坏", MessageBoxButton.OK, MessageBoxImage.Error);
                    LogService.Instance.Error($"GEEK.exe 哈希校验失败：期望 {ExpectedGeekSha256}，实际 {actualHash}");
                    return;
                }
                LogService.Instance.Info("GEEK.exe 哈希校验通过");

                File.WriteAllBytes(tempPath, data);

                using (Process.Start(new ProcessStartInfo
                {
                    FileName = tempPath,
                    UseShellExecute = true
                })) { }
                LogService.Instance.Info("已启动Geek卸载工具");
            }
        }
        catch (Exception ex)
        {
            MessageBox.Show($"启动卸载工具失败: {ex.Message}", "错误", MessageBoxButton.OK, MessageBoxImage.Error);
            LogService.Instance.Error($"启动Geek卸载工具失败: {ex.Message}");
        }
    }

    /// <summary>
    /// 运行 Win11Debloat：用户点击 → 校验 zip 哈希 → 解压 → 校验 Run.bat 哈希 → 执行。
    /// 任何一步校验失败都弹窗提示并不执行。
    /// </summary>
    private void RunWin11Debloat_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            string appPath = AppDomain.CurrentDomain.BaseDirectory;
            string toolPath = Path.Combine(appPath, "Win11Debloat");
            string batPath = Path.Combine(toolPath, "Run.bat");

            // 1. 获取 zip 数据：优先程序目录下的文件，其次内嵌资源
            byte[]? zipData = GetDebloatZipData(appPath);
            if (zipData == null)
            {
                MessageBox.Show("未找到 Win11Debloat.zip（程序目录或内嵌资源中均无）。", "错误", MessageBoxButton.OK, MessageBoxImage.Error);
                LogService.Instance.Error("未找到 Win11Debloat.zip");
                return;
            }

            // 2. 先验证 zip 哈希
            string zipHash = Convert.ToHexString(SHA256.HashData(zipData));
            if (!string.Equals(zipHash, ExpectedDebloatZipSha256, StringComparison.OrdinalIgnoreCase))
            {
                MessageBox.Show("Win11Debloat.zip 已损坏（哈希校验失败），无法继续。\n请重新下载安装系统工具箱。",
                    "工具损坏", MessageBoxButton.OK, MessageBoxImage.Error);
                LogService.Instance.Error($"Win11Debloat.zip 哈希校验失败：期望 {ExpectedDebloatZipSha256}，实际 {zipHash}");
                return;
            }
            LogService.Instance.Info("Win11Debloat.zip 哈希校验通过");

            // 3. 解压
            if (Directory.Exists(toolPath))
            {
                Directory.Delete(toolPath, true);
            }
            using (var ms = new MemoryStream(zipData))
            using (var archive = new ZipArchive(ms, ZipArchiveMode.Read))
            {
                archive.ExtractToDirectory(toolPath);
            }
            LogService.Instance.Info("Win11Debloat.zip 解压完成");

            // 4. 再验证解压出的 Run.bat 哈希
            if (!File.Exists(batPath))
            {
                MessageBox.Show("解压后未找到 Run.bat，工具包可能不完整。", "错误", MessageBoxButton.OK, MessageBoxImage.Error);
                LogService.Instance.Error("解压后未找到 Run.bat");
                return;
            }
            string batHash = Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(batPath)));
            if (!string.Equals(batHash, ExpectedDebloatRunBatSha256, StringComparison.OrdinalIgnoreCase))
            {
                MessageBox.Show("Run.bat 哈希校验失败，工具包可能被篡改，已停止执行。\n请重新下载安装系统工具箱。",
                    "工具损坏", MessageBoxButton.OK, MessageBoxImage.Error);
                LogService.Instance.Error($"Run.bat 哈希校验失败：期望 {ExpectedDebloatRunBatSha256}，实际 {batHash}");
                return;
            }
            LogService.Instance.Info("Run.bat 哈希校验通过");

            // 5. 执行
            using var process = new Process
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
            LogService.Instance.Info("已启动 Win11Debloat");
        }
        catch (Exception ex)
        {
            MessageBox.Show($"无法启动 Win11Debloat 工具：{ex.Message}", "错误", MessageBoxButton.OK, MessageBoxImage.Error);
            LogService.Instance.Error($"启动 Win11Debloat 失败: {ex.Message}");
        }
    }

    private static byte[]? GetDebloatZipData(string appPath)
    {
        string zipPath = Path.Combine(appPath, "Win11Debloat.zip");
        if (File.Exists(zipPath))
        {
            return File.ReadAllBytes(zipPath);
        }

        var assembly = Assembly.GetExecutingAssembly();
        foreach (var name in assembly.GetManifestResourceNames())
        {
            if (name.EndsWith("Win11Debloat.zip", StringComparison.OrdinalIgnoreCase))
            {
                using var stream = assembly.GetManifestResourceStream(name);
                if (stream == null) return null;
                using var ms = new MemoryStream();
                stream.CopyTo(ms);
                return ms.ToArray();
            }
        }
        return null;
    }

    private async void ActivateWindows_Click(object sender, RoutedEventArgs e)
    {
        if (_isOperating)
        {
            LogService.Instance.Warning("正在执行其他操作，请稍候...");
            return;
        }

        // 先显示中文引导，用户确认后再启动英文菜单的激活脚本
        var guide = new MasGuideWindow { Owner = Window.GetWindow(this) };
        if (guide.ShowDialog() != true)
        {
            return;
        }

        _isOperating = true;
        LogService.Instance.Info("开始激活Windows...");

        try
        {
            var result = await Task.Run(() =>
            {
                try
                {
                    var process = Process.Start(new ProcessStartInfo
                    {
                        FileName = "powershell.exe",
                        Arguments = "-NoProfile -ExecutionPolicy Bypass -Command \"irm get.activated.win | iex\"",
                        UseShellExecute = false,
                        CreateNoWindow = true,
                        RedirectStandardOutput = true,
                        RedirectStandardError = true
                        // 注：Verb="runas" 仅在 UseShellExecute=true 时有效，此处提权由 app.manifest 的 requireAdministrator 保证
                    });

                    if (process == null)
                    {
                        return (false, "无法启动PowerShell");
                    }

                    using (process)
                    {
                        // 两路输出异步读取，避免 stdout/stderr 相互阻塞造成死锁
                        var outputTask = Task.Run(() => process.StandardOutput.ReadToEnd());
                        var errorTask = Task.Run(() => process.StandardError.ReadToEnd());

                        if (!process.WaitForExit(300000))
                        {
                            // 超时：先杀进程再取已输出的内容，防止遗弃孤儿进程
                            try { process.Kill(); } catch { /* 进程可能已退出 */ }
                            var soFar = outputTask.IsCompletedSuccessfully ? outputTask.Result : string.Empty;
                            var seFar = errorTask.IsCompletedSuccessfully ? errorTask.Result : string.Empty;
                            var tail = string.IsNullOrEmpty(seFar) ? soFar : seFar;
                            return (false, $"激活脚本执行超时（已超过 5 分钟），进程已被终止。{tail}");
                        }

                        var output = outputTask.Result;
                        var error = errorTask.Result;

                        if (process.ExitCode == 0)
                        {
                            return (true, output);
                        }
                        else
                        {
                            return (false, string.IsNullOrEmpty(error) ? output : error);
                        }
                    }
                }
                catch (Exception ex)
                {
                    return (false, ex.Message);
                }
            });

            if (result.Item1)
            {
                LogService.Instance.Success("Windows激活命令执行成功");
            }
            else
            {
                LogService.Instance.Error($"Windows激活失败: {result.Item2}");
            }
        }
        catch (Exception ex)
        {
            LogService.Instance.Error($"Windows激活失败: {ex.Message}");
        }
        finally
        {
            _isOperating = false;
        }
    }
}
