using System.Diagnostics;
using System.IO;
using System.Net.Http;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using System.Text;

namespace SystemTool.Services;

/// <summary>
/// PawnIO 测温驱动安装服务。
/// LibreHardwareMonitor 0.9.5+ 改用 PawnIO 驱动读取 CPU MSR，但驱动需单独安装；
/// 未安装时所有 CPU 温度传感器读数为 null。本服务负责检测、下载（带哈希与签名校验）、静默安装。
/// 静默参数与成功退出码来自社区实测：-install -silent，0/183/3010 均视为成功。
/// </summary>
public static class PawnIoDriverService
{
    private static readonly Uri InstallerUri =
        new("https://github.com/namazso/PawnIO.Setup/releases/download/2.2.0/PawnIO_setup.exe");

    private const string ExpectedSha256 =
        "1F519A22E47187F70A1379A48CA604981C4FCF694F4E65B734AAA74A9FBA3032";

    private const string ExpectedSignerThumbprint =
        "F380DCC9F706E2756A5047B832FFE719E1BC35F5";

    private const string InstallerFileName = "PawnIO_setup_2.2.0.exe";
    private const string DeclineFlagFileName = "pawnio_prompt_declined.txt";

    private static readonly HttpClient HttpClient = new() { Timeout = TimeSpan.FromMinutes(5) };

    private static string AppDataDir =>
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "SystemTool");

    /// <summary>驱动是否已安装（读注册表 Uninstall\PawnIO）。</summary>
    public static bool IsDriverInstalled
    {
        get
        {
            try { return LibreHardwareMonitor.PawnIo.PawnIo.IsInstalled; }
            catch (Exception ex)
            {
                LogService.Instance.Warning("[PawnIoDriverService] 检测驱动安装状态失败", ex);
                return false;
            }
        }
    }

    /// <summary>用户是否选择了"不再提醒"。</summary>
    public static bool IsPromptDeclined()
    {
        try { return File.Exists(Path.Combine(AppDataDir, DeclineFlagFileName)); }
        catch { return false; }
    }

    public static void SetPromptDeclined()
    {
        try
        {
            Directory.CreateDirectory(AppDataDir);
            File.WriteAllText(Path.Combine(AppDataDir, DeclineFlagFileName),
                "用户选择不再提示安装 PawnIO 测温驱动。" + Environment.NewLine + DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss"));
        }
        catch (Exception ex)
        {
            LogService.Instance.Warning("[PawnIoDriverService] 保存不再提醒标记失败", ex);
        }
    }

    public enum InstallResult
    {
        Success,
        SuccessRebootRequired,
        Failed,
    }

    /// <summary>
    /// 下载→校验→静默安装。调用方需已获得用户同意；本方法不弹任何确认。
    /// </summary>
    public static async Task<(InstallResult Result, string Message)> InstallAsync(
        IProgress<string>? progress = null, CancellationToken cancellationToken = default)
    {
        try
        {
            Directory.CreateDirectory(AppDataDir);
            string installerPath = Path.Combine(AppDataDir, InstallerFileName);

            if (!File.Exists(installerPath) || !CheckSha256(installerPath))
            {
                // 优先使用程序内置的安装包（部分地区 GitHub 访问不稳定）；缺失才回退到下载
                if (!TryExtractBundledInstaller(installerPath, progress))
                {
                    progress?.Report("正在下载 PawnIO 驱动安装包（约 3.4MB）…");
                    LogService.Instance.Info("[PawnIoDriverService] 开始下载 PawnIO 安装包");
                    installerPath = await DownloadVerifiedAsync(installerPath, progress, cancellationToken);
                }
            }
            else
            {
                LogService.Instance.Info("[PawnIoDriverService] 本地已有校验通过的安装包，跳过解包/下载");
            }

            progress?.Report("正在校验数字签名…");
            if (!CheckSignerThumbprint(installerPath))
            {
                const string msg = "安装包签名校验未通过（签名者不是 namazso.eu），已中止安装。";
                LogService.Instance.Warning("[PawnIoDriverService] " + msg);
                return (InstallResult.Failed, msg);
            }
            LogService.Instance.Info("[PawnIoDriverService] 签名校验通过");

            progress?.Report("正在静默安装驱动…");
            LogService.Instance.Info("[PawnIoDriverService] 启动静默安装");
            int exitCode = await RunSilentInstallAsync(installerPath, cancellationToken);
            LogService.Instance.Info($"[PawnIoDriverService] 安装进程退出码: {exitCode}");

            // 0=成功，183=已存在，3010=需要重启；安装后以注册表为准再确认一次
            bool installedNow = IsDriverInstalled;
            if (exitCode is 0 or 183)
            {
                if (installedNow)
                    return (InstallResult.Success, "PawnIO 驱动安装成功，CPU 温度已可用。");
                // 退出码正常但注册表还没读到：多半需要重启完成驱动注册
                return (InstallResult.SuccessRebootRequired, "驱动已安装，需要重启电脑后生效。");
            }
            if (exitCode == 3010)
                return (InstallResult.SuccessRebootRequired, "驱动已安装，需要重启电脑后生效。");

            string failMsg = $"安装失败，退出码 {exitCode}。可前往 https://github.com/namazso/PawnIO.Setup/releases 手动安装。";
            LogService.Instance.Warning("[PawnIoDriverService] " + failMsg);
            return (InstallResult.Failed, failMsg);
        }
        catch (OperationCanceledException)
        {
            return (InstallResult.Failed, "安装已取消。");
        }
        catch (Exception ex)
        {
            LogService.Instance.Warning("[PawnIoDriverService] 安装过程异常", ex);
            return (InstallResult.Failed, $"安装过程出错：{ex.Message}");
        }
    }

    /// <summary>从程序内嵌资源解包安装包（GEEK.exe 同款模式），并校验哈希。</summary>
    private static bool TryExtractBundledInstaller(string destPath, IProgress<string>? progress)
    {
        try
        {
            var assembly = typeof(PawnIoDriverService).Assembly;
            using var stream = assembly.GetManifestResourceStream("PawnIO_setup.exe");
            if (stream == null)
            {
                LogService.Instance.Info("[PawnIoDriverService] 未找到内置安装包资源，回退到下载");
                return false;
            }
            progress?.Report("正在解包内置驱动安装包…");
            using (var file = File.Create(destPath))
            {
                stream.CopyTo(file);
            }
            if (!CheckSha256(destPath))
            {
                LogService.Instance.Warning("[PawnIoDriverService] 内置安装包哈希校验未通过，回退到下载");
                try { File.Delete(destPath); } catch { }
                return false;
            }
            LogService.Instance.Info("[PawnIoDriverService] 内置安装包解包并校验通过");
            return true;
        }
        catch (Exception ex)
        {
            LogService.Instance.Warning("[PawnIoDriverService] 解包内置安装包失败，回退到下载", ex);
            return false;
        }
    }

    private static async Task<string> DownloadVerifiedAsync(
        string installerPath, IProgress<string>? progress, CancellationToken cancellationToken)
    {
        string tmpPath = installerPath + ".download";
        if (File.Exists(tmpPath)) File.Delete(tmpPath);

        using var response = await HttpClient.GetAsync(InstallerUri, HttpCompletionOption.ResponseHeadersRead, cancellationToken);
        response.EnsureSuccessStatusCode();

        long? total = response.Content.Headers.ContentLength;
        await using var remote = await response.Content.ReadAsStreamAsync(cancellationToken);
        await using var local = File.Create(tmpPath);
        var buffer = new byte[81920];
        long read = 0;
        int n;
        while ((n = await remote.ReadAsync(buffer, cancellationToken)) > 0)
        {
            await local.WriteAsync(buffer.AsMemory(0, n), cancellationToken);
            read += n;
            if (total.HasValue && total.Value > 0)
                progress?.Report($"正在下载 PawnIO 驱动安装包… {read * 100 / total.Value}%");
        }

        if (!CheckSha256(tmpPath))
        {
            File.Delete(tmpPath);
            throw new InvalidOperationException("下载的安装包哈希与官方值不符，已删除。");
        }
        LogService.Instance.Info("[PawnIoDriverService] 安装包哈希校验通过");

        File.Move(tmpPath, installerPath, overwrite: true);
        return installerPath;
    }

    private static bool CheckSha256(string path)
    {
        try
        {
            using var stream = File.OpenRead(path);
            string actual = Convert.ToHexString(SHA256.HashData(stream));
            return string.Equals(actual, ExpectedSha256, StringComparison.OrdinalIgnoreCase);
        }
        catch { return false; }
    }

    private static bool CheckSignerThumbprint(string path)
    {
        try
        {
#pragma warning disable SYSLIB0057 // .NET 10 暂无直接替代品用于从 PE 提取签名证书，该 API 仍可用
            using var cert = new X509Certificate2(X509Certificate.CreateFromSignedFile(path));
#pragma warning restore SYSLIB0057
            return string.Equals(cert.Thumbprint, ExpectedSignerThumbprint, StringComparison.OrdinalIgnoreCase);
        }
        catch (Exception ex)
        {
            LogService.Instance.Warning("[PawnIoDriverService] 读取安装包签名失败", ex);
            return false;
        }
    }

    private static async Task<int> RunSilentInstallAsync(string installerPath, CancellationToken cancellationToken)
    {
        using var process = Process.Start(new ProcessStartInfo(installerPath, "-install -silent")
        {
            UseShellExecute = false,
            CreateNoWindow = true,
        });
        if (process == null) return -1;
        await process.WaitForExitAsync(cancellationToken);
        return process.ExitCode;
    }
}
