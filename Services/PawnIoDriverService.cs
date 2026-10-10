using System.Diagnostics;
using System.IO;
using System.Net.Http;
using System.Security.Cryptography;
using System.Text;
using Microsoft.Win32;

namespace SystemTool.Services;

/// <summary>
/// PawnIO 测温驱动安装服务。
/// LibreHardwareMonitor 0.9.5+ 改用 PawnIO 驱动读取 CPU MSR，但驱动需单独安装；
/// 未安装时所有 CPU 温度传感器读数为 null。本服务负责检测、下载（带 SHA-256 完整性校验）、静默安装。
/// 静默参数与成功退出码来自社区实测：-install -silent，0/183/3010 均视为成功。
/// </summary>
public static class PawnIoDriverService
{
    private static readonly Uri InstallerUri =
        new("https://github.com/namazso/PawnIO.Setup/releases/download/2.2.0/PawnIO_setup.exe");

    // ===== 安全模型：防篡改的唯一屏障 =====
    // 安装包完整性唯一依赖下面的 SHA-256 硬编码哈希。安装包在交付系统执行前的
    // 每一条入口（内置解包 / 网络下载 / 本地复用）都必须通过 CheckSha256，
    // 且执行前会再复核一次；任一失败即删除文件并中止安装，绝不执行未通过哈希的文件。
    // 注意：本服务不做 X509 证书链验证（WinVerifyTrust）。SHA-256 已钉死安装包的
    // 精确字节，任何篡改（替换/降级/位翻转）都会改变哈希；"只比证书指纹、不验
    // 信任链"属于半吊子实现，会给安全审计造成"已完整验签"的假象，故彻底移除，
    // 职责单一、表述诚实。
    private const string ExpectedSha256 =
        "1F519A22E47187F70A1379A48CA604981C4FCF694F4E65B734AAA74A9FBA3032";

    private const string InstallerFileName = "PawnIO_setup_2.2.0.exe";
    private const string DeclineFlagFileName = "pawnio_prompt_declined.txt";
    private const string InstallMutexName = "SystemTool_PawnIO_Install";
    private const int InstallTimeoutExitCode = -2;
    private const string DriverUninstallRegistryKey =
        @"SOFTWARE\Microsoft\Windows\CurrentVersion\Uninstall\PawnIO";
    private static readonly Version MinimumDriverVersion = new(2, 2, 0);

    private static readonly HttpClient HttpClient = new() { Timeout = TimeSpan.FromMinutes(5) };

    private static string AppDataDir =>
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "SystemTool");

    /// <summary>驱动是否已安装（读注册表 Uninstall\PawnIO，且 DisplayVersion &gt;= 2.2.0）。</summary>
    public static bool IsDriverInstalled
    {
        get
        {
            try
            {
                if (!LibreHardwareMonitor.PawnIo.PawnIo.IsInstalled) return false;
                string? displayVersion = ReadDriverDisplayVersion();
                if (string.IsNullOrWhiteSpace(displayVersion))
                {
                    LogService.Instance.Warning("[PawnIoDriverService] 未读到驱动 DisplayVersion，视为未安装");
                    return false;
                }
                if (!Version.TryParse(displayVersion.Trim(), out var version))
                {
                    LogService.Instance.Warning($"[PawnIoDriverService] 驱动版本解析失败（{displayVersion}），视为未安装");
                    return false;
                }
                if (version < MinimumDriverVersion)
                {
                    LogService.Instance.Warning($"[PawnIoDriverService] 驱动版本过低（{version}），要求 >= 2.2.0，视为未安装");
                    return false;
                }
                return true;
            }
            catch (Exception ex)
            {
                LogService.Instance.Warning("[PawnIoDriverService] 检测驱动安装状态失败", ex);
                return false;
            }
        }
    }

    /// <summary>从注册表读取驱动的 DisplayVersion，读不到返回 null。</summary>
    private static string? ReadDriverDisplayVersion()
    {
        foreach (var view in new[] { RegistryView.Registry64, RegistryView.Registry32 })
        {
            try
            {
                using var baseKey = RegistryKey.OpenBaseKey(RegistryHive.LocalMachine, view);
                using var key = baseKey.OpenSubKey(DriverUninstallRegistryKey);
                var value = key?.GetValue("DisplayVersion") as string;
                if (!string.IsNullOrWhiteSpace(value)) return value;
            }
            catch (Exception ex)
            {
                LogService.Instance.Warning("[PawnIoDriverService] 读取驱动版本注册表失败", ex);
            }
        }
        return null;
    }

    /// <summary>用户是否选择了"不再提醒"。</summary>
    public static bool IsPromptDeclined()
    {
        try { return File.Exists(Path.Combine(AppDataDir, DeclineFlagFileName)); }
        catch (Exception ex)
        {
            LogService.Instance.Warning("[PawnIoDriverService] 读取不再提醒标记失败", ex);
            return false;
        }
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

    /// <summary>清除"不再提醒"标记（安装成功后调用，驱动日后被卸载时下次启动可重新提示）。</summary>
    public static void ClearPromptDeclined()
    {
        try
        {
            string path = Path.Combine(AppDataDir, DeclineFlagFileName);
            if (File.Exists(path)) File.Delete(path);
        }
        catch (Exception ex)
        {
            LogService.Instance.Warning("[PawnIoDriverService] 清除不再提醒标记失败", ex);
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
        // 命名 Mutex：防止双开并发安装；拿不到锁直接返回
        using var installMutex = new Mutex(initiallyOwned: false, name: InstallMutexName);
        bool lockTaken;
        try
        {
            lockTaken = installMutex.WaitOne(TimeSpan.Zero);
        }
        catch (AbandonedMutexException)
        {
            lockTaken = true;
        }
        if (!lockTaken)
        {
            const string busyMsg = "已有 PawnIO 驱动安装任务在进行，本次安装请求已跳过。";
            LogService.Instance.Warning("[PawnIoDriverService] " + busyMsg);
            return (InstallResult.Failed, busyMsg);
        }

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

            // 执行前完整性复核：收窄"校验通过 → 交付执行"之间的 TOCTOU 窗口。
            // 未通过则删除文件并坚决中止，绝不将未通过哈希检查的文件交付系统执行。
            progress?.Report("正在做执行前完整性复核…");
            if (!CheckSha256(installerPath))
            {
                const string msg = "安装包完整性校验未通过（SHA-256 不匹配），已中止安装。";
                LogService.Instance.Warning("[PawnIoDriverService] " + msg);
                try { File.Delete(installerPath); } catch { }
                return (InstallResult.Failed, msg);
            }
            LogService.Instance.Info("[PawnIoDriverService] 安装包完整性复核通过，开始执行");

            progress?.Report("正在静默安装驱动…");
            LogService.Instance.Info("[PawnIoDriverService] 启动静默安装");
            int exitCode = await RunSilentInstallAsync(installerPath, cancellationToken);
            LogService.Instance.Info($"[PawnIoDriverService] 安装进程退出码: {exitCode}");

            // 0=成功，183=已存在，3010=需要重启；安装后以注册表为准再确认一次
            bool installedNow = IsDriverInstalled;
            if (exitCode is 0 or 183)
            {
                TryDeleteInstaller(installerPath);
                if (installedNow)
                {
                    // 安装成功：删掉"不再提醒"标记，驱动日后被卸载后下次启动可重新提示
                    ClearPromptDeclined();
                    return (InstallResult.Success, "PawnIO 驱动安装成功，CPU 温度已可用。");
                }
                // 退出码正常但注册表还没读到：多半需要重启完成驱动注册
                ClearPromptDeclined();
                return (InstallResult.SuccessRebootRequired, "驱动已安装，需要重启电脑后生效。");
            }
            if (exitCode == 3010)
            {
                TryDeleteInstaller(installerPath);
                if (!installedNow)
                    // 仅 3010 且注册表尚未读到驱动：保留标记，避免重启前反复弹窗
                    SetPromptDeclined();
                else
                    ClearPromptDeclined();
                return (InstallResult.SuccessRebootRequired, "驱动已安装，需要重启电脑后生效。");
            }

            string failMsg = exitCode == InstallTimeoutExitCode
                ? "安装超时（10 分钟），请检查后重试，或前往 https://github.com/namazso/PawnIO.Setup/releases 手动安装。"
                : $"安装失败，退出码 {exitCode}。可前往 https://github.com/namazso/PawnIO.Setup/releases 手动安装。";
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
        finally
        {
            try { installMutex.ReleaseMutex(); } catch { }
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

    /// <summary>安装成功后删除安装包残留文件；删除失败只记日志，不影响安装结果。</summary>
    private static void TryDeleteInstaller(string installerPath)
    {
        try
        {
            if (File.Exists(installerPath))
            {
                File.Delete(installerPath);
                LogService.Instance.Info("[PawnIoDriverService] 已删除安装包残留文件");
            }
        }
        catch (Exception ex)
        {
            LogService.Instance.Warning("[PawnIoDriverService] 删除安装包残留文件失败", ex);
        }
    }

    /// <summary>
    /// SHA-256 完整性校验：防篡改的唯一屏障。
    /// 流式计算哈希（不一次性读入内存）；字节级常量时间比对，防时序侧信道；
    /// 校验失败记准确日志（含期望/实际哈希）并返回 false，调用方必须中止且不得执行该文件。
    /// </summary>
    private static bool CheckSha256(string path)
    {
        try
        {
            using var stream = File.OpenRead(path);
            byte[] actual = SHA256.HashData(stream);
            byte[] expected = Convert.FromHexString(ExpectedSha256);
            bool ok = CryptographicOperations.FixedTimeEquals(actual, expected);
            if (!ok)
            {
                LogService.Instance.Warning(
                    $"[PawnIoDriverService] 安装包哈希校验未通过：期望 {ExpectedSha256}，实际 {Convert.ToHexString(actual)}（{path}），拒绝执行");
            }
            return ok;
        }
        catch (Exception ex)
        {
            LogService.Instance.Warning($"[PawnIoDriverService] 校验安装包哈希失败（{path}）", ex);
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
        // 10 分钟超时：超时按安装失败处理（调用方通过 InstallTimeoutExitCode 识别）
        using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeoutCts.CancelAfter(TimeSpan.FromMinutes(10));
        try
        {
            await process.WaitForExitAsync(timeoutCts.Token);
            return process.ExitCode;
        }
        catch (OperationCanceledException)
        {
            if (cancellationToken.IsCancellationRequested)
            {
                // 调用方主动取消：同样连根拔起，避免安装程序在后台继续跑成孤儿
                await KillProcessTreeAsync(process, "用户取消安装");
                throw;
            }
            LogService.Instance.Warning("[PawnIoDriverService] 静默安装超时（10 分钟），按安装失败处理");
            await KillProcessTreeAsync(process, "安装超时");
            return InstallTimeoutExitCode;
        }
    }

    /// <summary>
    /// 连根拔起进程树并等待其完全退出（30 秒上限，异步等待不阻塞 UI 线程）。
    /// 用于安装超时 / 用户取消 / 致命异常时的清理：PawnIO_setup.exe 会派生
    /// cmd.exe、msiexec、drvload 等子进程，只杀顶层会留下孤儿进程锁死驱动文件，
    /// 导致后续重试直接报错。所有异常内部消化，永不抛出。
    /// 调用方（InstallAsync 的 finally）在本方法返回后才释放互斥锁，
    /// 因此锁的释放一定发生在进程树完全退出之后。
    /// </summary>
    private static async Task KillProcessTreeAsync(Process process, string context)
    {
        try
        {
            bool alreadyExited;
            try { alreadyExited = process.HasExited; }
            catch (InvalidOperationException) { alreadyExited = true; } // 进程对象已失效：视为已结束
            catch { alreadyExited = false; }

            if (!alreadyExited)
            {
                try
                {
                    // entireProcessTree: true —— 把派生子进程连根拔起，防止孤儿残留
                    process.Kill(entireProcessTree: true);
                }
                catch (InvalidOperationException) { /* 竞态下已提前结束：忽略 */ }
                catch (NotSupportedException)
                {
                    try { process.Kill(); } catch { /* 降级为只杀顶层 */ }
                }
                catch (Exception ex)
                {
                    LogService.Instance.Warning($"[PawnIoDriverService] {context}：终止进程树失败", ex);
                }
            }

            // 等待进程完全退出、释放句柄；30 秒上限，超时不阻塞退出流程
            try
            {
                using var waitCts = new CancellationTokenSource(TimeSpan.FromSeconds(30));
                await process.WaitForExitAsync(waitCts.Token).ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                LogService.Instance.Warning($"[PawnIoDriverService] {context}：进程树 30 秒内未完全退出");
            }
            catch (InvalidOperationException) { /* 已结束：忽略 */ }
            catch (Exception ex)
            {
                LogService.Instance.Warning($"[PawnIoDriverService] {context}：等待进程退出异常", ex);
            }
        }
        catch (Exception ex)
        {
            LogService.Instance.Warning($"[PawnIoDriverService] {context}：清理进程异常", ex);
        }
    }
}
