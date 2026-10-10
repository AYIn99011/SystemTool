using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Runtime.InteropServices;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using Microsoft.Win32;
using SystemTool.Services;

namespace SystemTool.Pages;

public partial class OptimizerPage : Page
{
    private bool _isOperating;
    private CancellationTokenSource? _cancellationTokenSource;

    [DllImport("psapi.dll")]
    private static extern bool EmptyWorkingSet(IntPtr hProcess);

    [DllImport("kernel32.dll")]
    private static extern IntPtr OpenProcess(uint dwDesiredAccess, bool bInheritHandle, int dwProcessId);

    [DllImport("kernel32.dll")]
    private static extern bool CloseHandle(IntPtr hObject);

    private const uint PROCESS_SET_QUOTA = 0x0100;
    private const uint PROCESS_QUERY_INFORMATION = 0x0400;

    public OptimizerPage()
    {
        InitializeComponent();
    }

    private async void ActivateWindows_Click(object sender, RoutedEventArgs e)
    {
        if (_isOperating)
        {
            LogService.Instance.Warning("正在执行其他操作，请稍候...");
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
                        RedirectStandardError = true,
                        Verb = "runas"
                    });

                    if (process == null)
                    {
                        return (false, "无法启动PowerShell");
                    }

                    var output = process.StandardOutput.ReadToEnd();
                    var error = process.StandardError.ReadToEnd();
                    process.WaitForExit(300000);

                    if (process.ExitCode == 0)
                    {
                        return (true, output);
                    }
                    else
                    {
                        return (false, string.IsNullOrEmpty(error) ? output : error);
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

    private async void HideShortcutArrow_Click(object sender, RoutedEventArgs e)
    {
        await ExecuteRegistryOperationAsync("隐藏快捷方式小箭头", () =>
        {
            using var key = Registry.LocalMachine.CreateSubKey(@"SOFTWARE\Microsoft\Windows\CurrentVersion\Explorer\Shell Icons");
            key?.SetValue("29", "%systemroot%\\system32\\imageres.dll,197", RegistryValueKind.String);
        }, true);
    }

    private async void ShowShortcutArrow_Click(object sender, RoutedEventArgs e)
    {
        await ExecuteRegistryOperationAsync("恢复快捷方式小箭头", () =>
        {
            using var key = Registry.LocalMachine.OpenSubKey(@"SOFTWARE\Microsoft\Windows\CurrentVersion\Explorer\Shell Icons", true);
            if (key != null)
            {
                key.DeleteValue("29", false);
            }
        }, true);
    }

    private async void HideUacShield_Click(object sender, RoutedEventArgs e)
    {
        await ExecuteRegistryOperationAsync("隐藏可执行文件小盾牌", () =>
        {
            using var key = Registry.LocalMachine.CreateSubKey(@"SOFTWARE\Microsoft\Windows\CurrentVersion\Explorer\Shell Icons");
            key?.SetValue("77", "%systemroot%\\system32\\imageres.dll,197", RegistryValueKind.String);
        }, true);
    }

    private async void ShowUacShield_Click(object sender, RoutedEventArgs e)
    {
        await ExecuteRegistryOperationAsync("恢复可执行文件小盾牌", () =>
        {
            using var key = Registry.LocalMachine.OpenSubKey(@"SOFTWARE\Microsoft\Windows\CurrentVersion\Explorer\Shell Icons", true);
            if (key != null)
            {
                key.DeleteValue("77", false);
            }
        }, true);
    }

    private async void ShowHiddenFiles_Click(object sender, RoutedEventArgs e)
    {
        await ExecuteRegistryOperationAsync("显示隐藏文件", () =>
        {
            using var key = Registry.CurrentUser.CreateSubKey(@"Software\Microsoft\Windows\CurrentVersion\Explorer\Advanced");
            key?.SetValue("Hidden", 1, RegistryValueKind.DWord);
        }, true);
    }

    private async void HideHiddenFiles_Click(object sender, RoutedEventArgs e)
    {
        await ExecuteRegistryOperationAsync("隐藏文件", () =>
        {
            using var key = Registry.CurrentUser.CreateSubKey(@"Software\Microsoft\Windows\CurrentVersion\Explorer\Advanced");
            key?.SetValue("Hidden", 2, RegistryValueKind.DWord);
        }, true);
    }

    private async void ShowClockSeconds_Click(object sender, RoutedEventArgs e)
    {
        await ExecuteRegistryOperationAsync("显示任务栏时钟秒数", () =>
        {
            using var key = Registry.CurrentUser.CreateSubKey(@"Software\Microsoft\Windows\CurrentVersion\Explorer\Advanced");
            key?.SetValue("ShowSecondsInSystemClock", 1, RegistryValueKind.DWord);
        }, true);
    }

    private async void HideClockSeconds_Click(object sender, RoutedEventArgs e)
    {
        await ExecuteRegistryOperationAsync("隐藏任务栏时钟秒数", () =>
        {
            using var key = Registry.CurrentUser.CreateSubKey(@"Software\Microsoft\Windows\CurrentVersion\Explorer\Advanced");
            key?.SetValue("ShowSecondsInSystemClock", 0, RegistryValueKind.DWord);
        }, true);
    }

    private async void EnableLargeSystemCache_Click(object sender, RoutedEventArgs e)
    {
        await ExecuteRegistryOperationAsync("启用大系统缓存", () =>
        {
            using var key = Registry.LocalMachine.CreateSubKey(@"SYSTEM\CurrentControlSet\Control\Session Manager\Memory Management");
            key?.SetValue("LargeSystemCache", 1, RegistryValueKind.DWord);
        }, false);
    }

    private async void DisableLargeSystemCache_Click(object sender, RoutedEventArgs e)
    {
        await ExecuteRegistryOperationAsync("禁用大系统缓存", () =>
        {
            using var key = Registry.LocalMachine.CreateSubKey(@"SYSTEM\CurrentControlSet\Control\Session Manager\Memory Management");
            key?.SetValue("LargeSystemCache", 0, RegistryValueKind.DWord);
        }, false);
    }

    private async void EnableDisablePaging_Click(object sender, RoutedEventArgs e)
    {
        await ExecuteRegistryOperationAsync("启用禁止内核分页", () =>
        {
            using var key = Registry.LocalMachine.CreateSubKey(@"SYSTEM\CurrentControlSet\Control\Session Manager\Memory Management");
            key?.SetValue("DisablePagingExecutive", 1, RegistryValueKind.DWord);
        }, false);
    }

    private async void DisableDisablePaging_Click(object sender, RoutedEventArgs e)
    {
        await ExecuteRegistryOperationAsync("禁用禁止内核分页", () =>
        {
            using var key = Registry.LocalMachine.CreateSubKey(@"SYSTEM\CurrentControlSet\Control\Session Manager\Memory Management");
            key?.SetValue("DisablePagingExecutive", 0, RegistryValueKind.DWord);
        }, false);
    }

    private async void EnableIoPageLock_Click(object sender, RoutedEventArgs e)
    {
        await ExecuteRegistryOperationAsync("启用文件系统缓存", () =>
        {
            using var key = Registry.LocalMachine.CreateSubKey(@"SYSTEM\CurrentControlSet\Control\Session Manager\Memory Management");
            key?.SetValue("IoPageLockLimit", 0x10000000, RegistryValueKind.DWord);
        }, false);
    }

    private async void DisableIoPageLock_Click(object sender, RoutedEventArgs e)
    {
        await ExecuteRegistryOperationAsync("禁用文件系统缓存", () =>
        {
            using var key = Registry.LocalMachine.CreateSubKey(@"SYSTEM\CurrentControlSet\Control\Session Manager\Memory Management");
            key?.SetValue("IoPageLockLimit", 0, RegistryValueKind.DWord);
        }, false);
    }

    private async void EnableClassicContextMenu_Click(object sender, RoutedEventArgs e)
    {
        await ExecuteRegistryOperationAsync("恢复Win11经典右键菜单", () =>
        {
            var clsidPath = @"Software\Classes\CLSID\{86ca1aa0-34aa-4e8b-a509-50c905bae2a2}\InProcServer32";
            using (var key = Registry.CurrentUser.CreateSubKey(clsidPath))
            {
                key?.SetValue("", "", RegistryValueKind.String);
            }
        }, true);
    }

    private async void DisableClassicContextMenu_Click(object sender, RoutedEventArgs e)
    {
        await ExecuteRegistryOperationAsync("恢复Win11默认右键菜单", () =>
        {
            var clsidPath = @"Software\Classes\CLSID\{86ca1aa0-34aa-4e8b-a509-50c905bae2a2}\InProcServer32";
            using (var key = Registry.CurrentUser.CreateSubKey(clsidPath))
            {
                key?.SetValue("", "Windows.UI.FileExplorer.dll", RegistryValueKind.String);
            }
        }, true);
    }

    private async void EnableClassicExplorer_Click(object sender, RoutedEventArgs e)
    {
        var version = Environment.OSVersion.Version;
        var build = version.Build;

        if (build >= 22621)
        {
            await ExecuteRegistryOperationAsync("恢复Win11经典资源管理器", () =>
            {
                var clsidPath = @"Software\Classes\CLSID\{6480100b-5a83-4d1e-9f69-8ae5a88e9a33}\InProcServer32";
                using (var key = Registry.CurrentUser.CreateSubKey(clsidPath))
                {
                    key?.SetValue("", "", RegistryValueKind.String);
                }
            }, true);
        }
        else if (build >= 22000)
        {
            await ExecuteRegistryOperationAsync("恢复Win11经典资源管理器", () =>
            {
                var clsidPath = @"Software\Classes\CLSID\{e2bf9676-5f8f-435c-97eb-11607a5bedf7}\InProcServer32";
                using (var key = Registry.CurrentUser.CreateSubKey(clsidPath))
                {
                    key?.SetValue("", "", RegistryValueKind.String);
                }
            }, true);
        }
        else
        {
            LogService.Instance.Warning("当前系统版本不支持此功能，需要Windows 11 (Build 22000+)");
        }
    }

    private async void DisableClassicExplorer_Click(object sender, RoutedEventArgs e)
    {
        var version = Environment.OSVersion.Version;
        var build = version.Build;

        if (build >= 22621)
        {
            await ExecuteRegistryOperationAsync("恢复Win11默认资源管理器", () =>
            {
                var clsidPath = @"Software\Classes\CLSID\{6480100b-5a83-4d1e-9f69-8ae5a88e9a33}\InProcServer32";
                using (var key = Registry.CurrentUser.CreateSubKey(clsidPath))
                {
                    key?.SetValue("", "Windows.UI.FileExplorer.dll", RegistryValueKind.String);
                }
            }, true);
        }
        else if (build >= 22000)
        {
            await ExecuteRegistryOperationAsync("恢复Win11默认资源管理器", () =>
            {
                var clsidPath = @"Software\Classes\CLSID\{e2bf9676-5f8f-435c-97eb-11607a5bedf7}\InProcServer32";
                using (var key = Registry.CurrentUser.CreateSubKey(clsidPath))
                {
                    key?.SetValue("", "ntshrui.dll", RegistryValueKind.String);
                }
            }, true);
        }
        else
        {
            LogService.Instance.Warning("当前系统版本不支持此功能，需要Windows 11 (Build 22000+)");
        }
    }

    private async Task ExecuteRegistryOperationAsync(string operationName, Action action, bool restartExplorer = false)
    {
        if (_isOperating)
        {
            LogService.Instance.Warning("正在执行其他操作，请稍候...");
            return;
        }

        _isOperating = true;
        LogService.Instance.Info($"开始执行: {operationName}...");

        try
        {
            var result = await Task.Run(() =>
            {
                try
                {
                    action();

                    if (restartExplorer)
                    {
                        foreach (var proc in Process.GetProcessesByName("explorer"))
                        {
                            try { proc.Kill(); }
                            catch (Exception ex)
                            {
                                LogService.Instance.Warning("[OptimizerPage.ExecuteRegistryOperationAsync] 重启资源管理器失败", ex);
                            }
                        }
                        Thread.Sleep(500);
                        Process.Start(new ProcessStartInfo
                        {
                            FileName = "explorer.exe",
                            UseShellExecute = true
                        });
                    }

                    return (true, "");
                }
                catch (UnauthorizedAccessException)
                {
                    return (false, "需要管理员权限，请以管理员身份运行程序");
                }
                catch (System.Security.SecurityException)
                {
                    return (false, "需要管理员权限，请以管理员身份运行程序");
                }
                catch (Exception ex)
                {
                    return (false, ex.Message);
                }
            });

            if (result.Item1)
            {
                LogService.Instance.Success($"{operationName}执行成功");
            }
            else
            {
                LogService.Instance.Error($"{operationName}执行失败: {result.Item2}");
            }
        }
        catch (Exception ex)
        {
            LogService.Instance.Error($"{operationName}执行失败: {ex.Message}");
        }
        finally
        {
            _isOperating = false;
        }
    }



    private async void DisableFastStartup_Click(object sender, RoutedEventArgs e)
    {
        await ExecuteRegistryOperationAsync("关闭快速启动", () =>
        {
            using var key = Registry.LocalMachine.CreateSubKey(@"SYSTEM\CurrentControlSet\Control\Session Manager\Power");
            key?.SetValue("HiberbootEnabled", 0, RegistryValueKind.DWord);
        }, false);
    }

    private async void EnableFastStartup_Click(object sender, RoutedEventArgs e)
    {
        await ExecuteRegistryOperationAsync("开启快速启动", () =>
        {
            var process = Process.Start(new ProcessStartInfo
            {
                FileName = "powercfg.exe",
                Arguments = "-hibernate on",
                UseShellExecute = false,
                CreateNoWindow = true
            });
            process?.WaitForExit(10000);

            using var powerKey = Registry.LocalMachine.CreateSubKey(@"SYSTEM\CurrentControlSet\Control\Power");
            powerKey?.SetValue("HibernateEnabled", 1, RegistryValueKind.DWord);

            using var sessionKey = Registry.LocalMachine.CreateSubKey(@"SYSTEM\CurrentControlSet\Control\Session Manager\Power");
            sessionKey?.SetValue("HiberbootEnabled", 1, RegistryValueKind.DWord);
        }, false);
    }

    private async void EnableUltimatePerformance_Click(object sender, RoutedEventArgs e)
    {
        if (_isOperating)
        {
            LogService.Instance.Warning("正在执行其他操作，请稍候...");
            return;
        }

        _isOperating = true;
        LogService.Instance.Info("开始启用卓越性能电源计划...");

        try
        {
            var result = await Task.Run(() =>
            {
                try
                {
                    var listProcess = Process.Start(new ProcessStartInfo
                    {
                        FileName = "powercfg.exe",
                        Arguments = "/list",
                        UseShellExecute = false,
                        CreateNoWindow = true,
                        RedirectStandardOutput = true
                    });

                    if (listProcess == null)
                        return (false, "无法启动powercfg");

                    var output = listProcess.StandardOutput.ReadToEnd();
                    listProcess.WaitForExit(10000);

                    if (output.Contains("卓越性能") || output.Contains("Ultimate Performance"))
                    {
                        var lines = output.Split(new[] { Environment.NewLine }, StringSplitOptions.RemoveEmptyEntries);
                        foreach (var line in lines)
                        {
                            if (line.Contains("卓越性能") || line.Contains("Ultimate Performance"))
                            {
                                var match = System.Text.RegularExpressions.Regex.Match(line, @"([a-f0-9]{8}-[a-f0-9]{4}-[a-f0-9]{4}-[a-f0-9]{4}-[a-f0-9]{12})");
                                if (match.Success)
                                {
                                    var guid = match.Groups[1].Value;
                                    var setActiveProcess = Process.Start(new ProcessStartInfo
                                    {
                                        FileName = "powercfg.exe",
                                        Arguments = $"/setactive {guid}",
                                        UseShellExecute = false,
                                        CreateNoWindow = true
                                    });
                                    setActiveProcess?.WaitForExit(10000);
                                    return (true, "卓越性能电源计划已激活");
                                }
                            }
                        }
                    }

                    var duplicateProcess = new Process
                    {
                        StartInfo = new ProcessStartInfo
                        {
                            FileName = "powercfg.exe",
                            Arguments = "-duplicatescheme e9a42b02-d5df-448d-aa00-03f14749eb61",
                            UseShellExecute = true,
                            CreateNoWindow = true,
                            WindowStyle = ProcessWindowStyle.Hidden,
                            Verb = "runas"
                        }
                    };

                    duplicateProcess.Start();
                    duplicateProcess.WaitForExit(30000);

                    if (duplicateProcess.ExitCode != 0)
                    {
                        return (false, $"创建失败，退出代码: {duplicateProcess.ExitCode}。系统可能不支持卓越性能计划（Windows家庭版可能无此功能）");
                    }

                    Thread.Sleep(500);

                    var listProcess2 = Process.Start(new ProcessStartInfo
                    {
                        FileName = "powercfg.exe",
                        Arguments = "/list",
                        UseShellExecute = false,
                        CreateNoWindow = true,
                        RedirectStandardOutput = true
                    });

                    if (listProcess2 == null)
                        return (false, "无法获取电源计划列表");

                    var output2 = listProcess2.StandardOutput.ReadToEnd();
                    listProcess2.WaitForExit(10000);

                    var lines2 = output2.Split(new[] { Environment.NewLine }, StringSplitOptions.RemoveEmptyEntries);
                    foreach (var line in lines2)
                    {
                        if (line.Contains("卓越性能") || line.Contains("Ultimate Performance"))
                        {
                            var match = System.Text.RegularExpressions.Regex.Match(line, @"([a-f0-9]{8}-[a-f0-9]{4}-[a-f0-9]{4}-[a-f0-9]{4}-[a-f0-9]{12})");
                            if (match.Success)
                            {
                                var foundGuid = match.Groups[1].Value;

                                var setActiveProcess = Process.Start(new ProcessStartInfo
                                {
                                    FileName = "powercfg.exe",
                                    Arguments = $"/setactive {foundGuid}",
                                    UseShellExecute = false,
                                    CreateNoWindow = true
                                });
                                setActiveProcess?.WaitForExit(10000);

                                return (true, "卓越性能电源计划已创建并激活");
                            }
                        }
                    }

                    return (false, "系统可能不支持卓越性能计划（Windows家庭版可能无此功能）");
                }
                catch (Exception ex)
                {
                    return (false, ex.Message);
                }
            });

            if (result.Item1)
            {
                LogService.Instance.Success(result.Item2);
            }
            else
            {
                LogService.Instance.Error($"启用卓越性能失败: {result.Item2}");
            }
        }
        catch (Exception ex)
        {
            LogService.Instance.Error($"启用卓越性能失败: {ex.Message}");
        }
        finally
        {
            _isOperating = false;
        }
    }

    private async void RestoreBalancedPowerPlan_Click(object sender, RoutedEventArgs e)
    {
        if (_isOperating)
        {
            LogService.Instance.Warning("正在执行其他操作，请稍候...");
            return;
        }

        _isOperating = true;
        LogService.Instance.Info("开始恢复平衡电源计划...");

        try
        {
            var result = await Task.Run(() =>
            {
                try
                {
                    var process = Process.Start(new ProcessStartInfo
                    {
                        FileName = "powercfg.exe",
                        Arguments = "/setactive 381b4222-f694-41f0-9685-ff5bb260df2e",
                        UseShellExecute = false,
                        CreateNoWindow = true
                    });

                    process?.WaitForExit(10000);
                    return (true, "已恢复平衡电源计划");
                }
                catch (Exception ex)
                {
                    return (false, ex.Message);
                }
            });

            if (result.Item1)
            {
                LogService.Instance.Success(result.Item2);
            }
            else
            {
                LogService.Instance.Error($"恢复平衡模式失败: {result.Item2}");
            }
        }
        catch (Exception ex)
        {
            LogService.Instance.Error($"恢复平衡模式失败: {ex.Message}");
        }
        finally
        {
            _isOperating = false;
        }
    }

    #region VBS 备份与恢复

    /// <summary>VBS 相关注册表/引导配置的原始状态备份；"开启 VBS"时优先恢复备份，而不是无脑全开。</summary>
    private sealed class VbsBackup
    {
        public List<VbsRegValueBackup> RegValues { get; set; } = new();
        public bool BcdHadValue { get; set; }
        public string? BcdValue { get; set; }
    }

    private sealed class VbsRegValueBackup
    {
        public string SubKey { get; set; } = "";
        public string Name { get; set; } = "";
        public string Label { get; set; } = "";
        public bool Existed { get; set; }
        public int Kind { get; set; }
        public string? Value { get; set; }
    }

    private static string VbsBackupPath => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "SystemTool", "vbs_backup.json");

    private static void BackupVbsRegValue(VbsBackup backup, string subKey, string name, string label)
    {
        try
        {
            using var key = Registry.LocalMachine.OpenSubKey(subKey, writable: false);
            bool existed = key != null && key.GetValueNames().Contains(name, StringComparer.OrdinalIgnoreCase);
            var entry = new VbsRegValueBackup { SubKey = subKey, Name = name, Label = label, Existed = existed };
            if (existed)
            {
                var kind = key!.GetValueKind(name);
                entry.Kind = (int)kind;
                entry.Value = Convert.ToString(key.GetValue(name), CultureInfo.InvariantCulture);
            }
            backup.RegValues.Add(entry);
        }
        catch (Exception ex)
        {
            LogService.Instance.Warning($"[VBS] 备份注册表值失败: {subKey}\\{name}", ex);
        }
    }

    /// <summary>读取 bcdedit 中 hypervisorlaunchtype 的当前值；无该项返回 null（表示默认值）。</summary>
    private static string? GetBcdHypervisorLaunchType(out bool hadValue)
    {
        hadValue = false;
        try
        {
            using var process = Process.Start(new ProcessStartInfo
            {
                FileName = "bcdedit.exe",
                Arguments = "/enum {current}",
                UseShellExecute = false,
                RedirectStandardOutput = true,
                CreateNoWindow = true
            });
            if (process == null) return null;
            string output = process.StandardOutput.ReadToEnd();
            process.WaitForExit(10000);
            foreach (var line in output.Split('\n'))
            {
                var t = line.Trim();
                if (t.StartsWith("hypervisorlaunchtype", StringComparison.OrdinalIgnoreCase))
                {
                    var parts = t.Split(new[] { ' ', '\t' }, StringSplitOptions.RemoveEmptyEntries);
                    if (parts.Length >= 2)
                    {
                        hadValue = true;
                        return parts[1];
                    }
                }
            }
            return null;
        }
        catch
        {
            return null;
        }
    }

    private static void RestoreVbsRegValue(VbsRegValueBackup entry, List<string> results)
    {
        try
        {
            using var key = Registry.LocalMachine.OpenSubKey(entry.SubKey, writable: true)
                ?? Registry.LocalMachine.CreateSubKey(entry.SubKey);
            if (key == null)
            {
                results.Add($"✗ 恢复{entry.Label}失败：无法打开注册表项");
                return;
            }
            if (entry.Existed)
            {
                var kind = (RegistryValueKind)entry.Kind;
                object value = kind switch
                {
                    RegistryValueKind.DWord => int.Parse(entry.Value ?? "0", CultureInfo.InvariantCulture),
                    RegistryValueKind.QWord => long.Parse(entry.Value ?? "0", CultureInfo.InvariantCulture),
                    _ => (object)(entry.Value ?? ""),
                };
                key.SetValue(entry.Name, value, kind);
            }
            else
            {
                // 禁用前该值不存在：删掉我们创建的值，还原本来面目
                key.DeleteValue(entry.Name, throwOnMissingValue: false);
            }
            results.Add($"✓ 恢复{entry.Label}");
        }
        catch (Exception ex)
        {
            results.Add($"✗ 恢复{entry.Label}失败: {ex.Message}");
        }
    }

    private static bool RunBcdedit(string arguments, List<string> results, string okLabel, string failLabel)
    {
        try
        {
            using var process = Process.Start(new ProcessStartInfo
            {
                FileName = "bcdedit.exe",
                Arguments = arguments,
                UseShellExecute = false,
                CreateNoWindow = true
            });
            if (process == null)
            {
                results.Add($"✗ {failLabel}：无法启动 bcdedit.exe");
                return false;
            }
            process.WaitForExit(10000);
            if (process.ExitCode == 0)
            {
                results.Add($"✓ {okLabel}");
                return true;
            }
            results.Add($"✗ {failLabel}，bcdedit 退出代码: {process.ExitCode}");
            return false;
        }
        catch (Exception ex)
        {
            results.Add($"✗ {failLabel}: {ex.Message}");
            return false;
        }
    }

    #endregion

    private async void DisableVBS_Click(object sender, RoutedEventArgs e)
    {
        if (_isOperating)
        {
            LogService.Instance.Warning("正在执行其他操作，请稍候...");
            return;
        }

        _isOperating = true;
        LogService.Instance.Info("开始关闭VBS和内核隔离...");

        // 禁用前先备份原始状态（仅首次备份，避免重复禁用覆盖真正的原始值）
        if (!File.Exists(VbsBackupPath))
        {
            try
            {
                var backup = new VbsBackup();
                BackupVbsRegValue(backup, @"SYSTEM\CurrentControlSet\Control\DeviceGuard", "EnableVirtualizationBasedSecurity", "Device Guard");
                BackupVbsRegValue(backup, @"SYSTEM\CurrentControlSet\Control\DeviceGuard", "RequireMicrosoftSignedBootChain", "Device Guard 启动链");
                BackupVbsRegValue(backup, @"SYSTEM\CurrentControlSet\Control\DeviceGuard\Scenarios\HypervisorEnforcedCodeIntegrity", "Enabled", "内核完整性(HVCI)");
                BackupVbsRegValue(backup, @"SYSTEM\CurrentControlSet\Control\Lsa", "LsaCfgFlags", "Credential Guard");
                BackupVbsRegValue(backup, @"SYSTEM\CurrentControlSet\Control\DeviceGuard\Scenarios\SystemGuard", "Enabled", "System Guard");
                backup.BcdValue = GetBcdHypervisorLaunchType(out bool hadValue);
                backup.BcdHadValue = hadValue;
                Directory.CreateDirectory(Path.GetDirectoryName(VbsBackupPath)!);
                File.WriteAllText(VbsBackupPath, JsonSerializer.Serialize(backup));
                LogService.Instance.Info("[VBS] 已备份禁用前的原始配置");
            }
            catch (Exception ex)
            {
                LogService.Instance.Warning("[VBS] 备份原始配置失败", ex);
            }
        }

        var results = new List<string>();

        try
        {
            await Task.Run(() =>
            {
                try
                {
                    var process = Process.Start(new ProcessStartInfo
                    {
                        FileName = "bcdedit.exe",
                        Arguments = "/set hypervisorlaunchtype off",
                        UseShellExecute = false,
                        CreateNoWindow = true
                    });
                    if (process != null)
                    {
                        process.WaitForExit(10000);
                        if (process.ExitCode == 0)
                            results.Add("✓ 禁用Hypervisor启动类型");
                        else
                            results.Add($"✗ 禁用Hypervisor失败，bcdedit 退出代码: {process.ExitCode}");
                    }
                    else
                    {
                        results.Add("✗ 无法启动 bcdedit.exe");
                    }
                }
                catch (Exception ex)
                {
                    results.Add($"✗ 禁用Hypervisor失败: {ex.Message}");
                }

                try
                {
                    using var key1 = Registry.LocalMachine.CreateSubKey(@"SYSTEM\CurrentControlSet\Control\DeviceGuard");
                    key1?.SetValue("EnableVirtualizationBasedSecurity", 0, RegistryValueKind.DWord);
                    key1?.SetValue("RequireMicrosoftSignedBootChain", 0, RegistryValueKind.DWord);
                    results.Add("✓ 禁用Device Guard");
                }
                catch (Exception ex)
                {
                    results.Add($"✗ 禁用Device Guard失败: {ex.Message}");
                }

                try
                {
                    using var key2 = Registry.LocalMachine.CreateSubKey(@"SYSTEM\CurrentControlSet\Control\DeviceGuard\Scenarios\HypervisorEnforcedCodeIntegrity");
                    key2?.SetValue("Enabled", 0, RegistryValueKind.DWord);
                    results.Add("✓ 禁用内核完整性(HVCI)");
                }
                catch (Exception ex)
                {
                    results.Add($"✗ 禁用内核完整性失败: {ex.Message}");
                }

                try
                {
                    using var key3 = Registry.LocalMachine.CreateSubKey(@"SYSTEM\CurrentControlSet\Control\Lsa");
                    key3?.SetValue("LsaCfgFlags", 0, RegistryValueKind.DWord);
                    results.Add("✓ 禁用Credential Guard");
                }
                catch (Exception ex)
                {
                    results.Add($"✗ 禁用Credential Guard失败: {ex.Message}");
                }

                try
                {
                    using var key4 = Registry.LocalMachine.CreateSubKey(@"SYSTEM\CurrentControlSet\Control\DeviceGuard\Scenarios\SystemGuard");
                    key4?.SetValue("Enabled", 0, RegistryValueKind.DWord);
                    results.Add("✓ 禁用System Guard");
                }
                catch (Exception ex)
                {
                    results.Add($"✗ 禁用System Guard失败: {ex.Message}");
                }
            });

            foreach (var result in results)
            {
                if (result.StartsWith("✓"))
                    LogService.Instance.Success(result);
                else
                    LogService.Instance.Error(result);
            }

            LogService.Instance.Success("关闭VBS和内核隔离完成，请重启电脑生效");
        }
        catch (Exception ex)
        {
            LogService.Instance.Error($"关闭VBS和内核隔离失败: {ex.Message}");
        }
        finally
        {
            _isOperating = false;
        }
    }

    private async void EnableVBS_Click(object sender, RoutedEventArgs e)
    {
        if (_isOperating)
        {
            LogService.Instance.Warning("正在执行其他操作，请稍候...");
            return;
        }

        _isOperating = true;
        LogService.Instance.Info("开始开启VBS和内核隔离...");

        var results = new List<string>();

        try
        {
            await Task.Run(() =>
            {
                // 有禁用前备份 → 精确恢复原始状态；无备份 → 沿用原来的"全部开启"（用户主动点的"开启 VBS"）
                VbsBackup? backup = null;
                if (File.Exists(VbsBackupPath))
                {
                    try
                    {
                        backup = JsonSerializer.Deserialize<VbsBackup>(File.ReadAllText(VbsBackupPath));
                        LogService.Instance.Info("[VBS] 检测到禁用前备份，将恢复原始配置");
                    }
                    catch (Exception ex)
                    {
                        LogService.Instance.Warning("[VBS] 读取备份失败，将执行默认开启逻辑", ex);
                    }
                }

                if (backup != null)
                {
                    foreach (var entry in backup.RegValues)
                    {
                        RestoreVbsRegValue(entry, results);
                    }

                    if (backup.BcdHadValue && !string.IsNullOrEmpty(backup.BcdValue))
                        RunBcdedit($"/set hypervisorlaunchtype {backup.BcdValue}", results, "恢复Hypervisor启动类型", "恢复Hypervisor启动类型失败");
                    else if (!backup.BcdHadValue)
                        RunBcdedit("/deletevalue hypervisorlaunchtype", results, "恢复Hypervisor启动类型（默认值）", "恢复Hypervisor启动类型失败");
                    else
                        results.Add("✗ 恢复Hypervisor启动类型失败：备份中无有效值");

                    try { File.Delete(VbsBackupPath); } catch { }
                }
                else
                {
                    LogService.Instance.Info("[VBS] 无禁用前备份，执行默认开启逻辑");
                    RunBcdedit("/set hypervisorlaunchtype auto", results, "启用Hypervisor启动类型", "启用Hypervisor失败");

                    try
                    {
                        using var key1 = Registry.LocalMachine.CreateSubKey(@"SYSTEM\CurrentControlSet\Control\DeviceGuard");
                        key1?.SetValue("EnableVirtualizationBasedSecurity", 1, RegistryValueKind.DWord);
                        key1?.SetValue("RequireMicrosoftSignedBootChain", 1, RegistryValueKind.DWord);
                        results.Add("✓ 启用Device Guard");
                    }
                    catch (Exception ex)
                    {
                        results.Add($"✗ 启用Device Guard失败: {ex.Message}");
                    }

                    try
                    {
                        using var key2 = Registry.LocalMachine.CreateSubKey(@"SYSTEM\CurrentControlSet\Control\DeviceGuard\Scenarios\HypervisorEnforcedCodeIntegrity");
                        key2?.SetValue("Enabled", 1, RegistryValueKind.DWord);
                        results.Add("✓ 启用内核完整性(HVCI)");
                    }
                    catch (Exception ex)
                    {
                        results.Add($"✗ 启用内核完整性失败: {ex.Message}");
                    }

                    try
                    {
                        using var key3 = Registry.LocalMachine.CreateSubKey(@"SYSTEM\CurrentControlSet\Control\Lsa");
                        key3?.SetValue("LsaCfgFlags", 1, RegistryValueKind.DWord);
                        results.Add("✓ 启用Credential Guard");
                    }
                    catch (Exception ex)
                    {
                        results.Add($"✗ 启用Credential Guard失败: {ex.Message}");
                    }

                    try
                    {
                        using var key4 = Registry.LocalMachine.CreateSubKey(@"SYSTEM\CurrentControlSet\Control\DeviceGuard\Scenarios\SystemGuard");
                        key4?.SetValue("Enabled", 1, RegistryValueKind.DWord);
                        results.Add("✓ 启用System Guard");
                    }
                    catch (Exception ex)
                    {
                        results.Add($"✗ 启用System Guard失败: {ex.Message}");
                    }
                }
            });

            foreach (var result in results)
            {
                if (result.StartsWith("✓"))
                    LogService.Instance.Success(result);
                else
                    LogService.Instance.Error(result);
            }

            LogService.Instance.Success("开启VBS和内核隔离完成，请重启电脑生效");
        }
        catch (Exception ex)
        {
            LogService.Instance.Error($"开启VBS和内核隔离失败: {ex.Message}");
        }
        finally
        {
            _isOperating = false;
        }
    }

    private async void DisableSystemLogs_Click(object sender, RoutedEventArgs e)
    {
        if (_isOperating)
        {
            LogService.Instance.Warning("正在执行其他操作，请稍候...");
            return;
        }

        _isOperating = true;
        LogService.Instance.Info("开始禁用系统日志...");

        var results = new List<string>();

        try
        {
            await Task.Run(() =>
            {
                try
                {
                    var regContent = @"Windows Registry Editor Version 5.00

[HKEY_LOCAL_MACHINE\SOFTWARE\Microsoft\Windows\CurrentVersion\Component Based Servicing]
""EnableLog""=dword:00000000
""EnableDpxLog""=dword:00000000

[HKEY_LOCAL_MACHINE\SOFTWARE\Microsoft\Windows NT\CurrentVersion\Winlogon]
""ReportBootOk""=""0""

[HKEY_LOCAL_MACHINE\SYSTEM\CurrentControlSet\Services\BFE\Parameters\Policy\Options]
""CollectNetEvents""=dword:00000000
";
                    var tempRegFile = Path.Combine(Path.GetTempPath(), $"SystemTool_DisableLogs_{Guid.NewGuid():N}.reg");
                    File.WriteAllText(tempRegFile, regContent, System.Text.Encoding.Unicode);

                    var process = Process.Start(new ProcessStartInfo
                    {
                        FileName = "regedit.exe",
                        Arguments = $"/s \"{tempRegFile}\"",
                        UseShellExecute = true,
                        Verb = "runas",
                        WindowStyle = ProcessWindowStyle.Hidden
                    });

                    if (process != null)
                    {
                        process.WaitForExit(30000);
                        File.Delete(tempRegFile);

                        if (process.ExitCode == 0)
                        {
                            results.Add("✓ 禁用组件堆栈日志");
                            results.Add("✓ 禁用更新解压模块日志");
                            results.Add("✓ 禁用账户登录日志报告");
                        }
                        else
                        {
                            results.Add($"✗ 注册表导入失败，退出代码: {process.ExitCode}");
                        }
                    }
                    else
                    {
                        results.Add("✗ 无法启动注册表编辑器（需要管理员权限）");
                    }
                }
                catch (Exception ex)
                {
                    results.Add($"✗ 注册表操作失败: {ex.Message}");
                }

                try
                {
                    var process = Process.Start(new ProcessStartInfo
                    {
                        FileName = "netsh.exe",
                        Arguments = "wfp set options netevents=off",
                        UseShellExecute = false,
                        CreateNoWindow = true
                    });
                    process?.WaitForExit(10000);
                    results.Add("✓ 禁用WfpDiag.ETL日志");
                }
                catch (Exception ex)
                {
                    results.Add($"✗ 禁用WfpDiag.ETL日志失败: {ex.Message}");
                }
            });

            foreach (var result in results)
            {
                if (result.StartsWith("✓"))
                    LogService.Instance.Success(result);
                else
                    LogService.Instance.Error(result);
            }

            LogService.Instance.Success("禁用系统日志完成");
        }
        catch (Exception ex)
        {
            LogService.Instance.Error($"禁用系统日志失败: {ex.Message}");
        }
        finally
        {
            _isOperating = false;
        }
    }

    private async void EnableSystemLogs_Click(object sender, RoutedEventArgs e)
    {
        if (_isOperating)
        {
            LogService.Instance.Warning("正在执行其他操作，请稍候...");
            return;
        }

        _isOperating = true;
        LogService.Instance.Info("开始恢复系统日志...");

        var results = new List<string>();

        try
        {
            await Task.Run(() =>
            {
                try
                {
                    using var key1 = Registry.LocalMachine.CreateSubKey(@"SOFTWARE\Microsoft\Windows\CurrentVersion\Component Based Servicing");
                    key1?.SetValue("EnableLog", 1, RegistryValueKind.DWord);
                    results.Add("✓ 恢复组件堆栈日志");
                }
                catch (Exception ex)
                {
                    results.Add($"✗ 恢复组件堆栈日志失败: {ex.Message}");
                }

                try
                {
                    using var key2 = Registry.LocalMachine.CreateSubKey(@"SOFTWARE\Microsoft\Windows\CurrentVersion\Component Based Servicing");
                    key2?.SetValue("EnableDpxLog", 1, RegistryValueKind.DWord);
                    results.Add("✓ 恢复更新解压模块日志");
                }
                catch (Exception ex)
                {
                    results.Add($"✗ 恢复更新解压模块日志失败: {ex.Message}");
                }

                try
                {
                    using var key3 = Registry.LocalMachine.CreateSubKey(@"SOFTWARE\Microsoft\Windows NT\CurrentVersion\Winlogon");
                    key3?.SetValue("ReportBootOk", "1", RegistryValueKind.String);
                    results.Add("✓ 恢复账户登录日志报告");
                }
                catch (Exception ex)
                {
                    results.Add($"✗ 恢复账户登录日志报告失败: {ex.Message}");
                }

                try
                {
                    var process = Process.Start(new ProcessStartInfo
                    {
                        FileName = "netsh.exe",
                        Arguments = "wfp set options netevents=on",
                        UseShellExecute = false,
                        CreateNoWindow = true
                    });
                    process?.WaitForExit(10000);

                    using var key4 = Registry.LocalMachine.OpenSubKey(@"SYSTEM\CurrentControlSet\Services\BFE\Parameters\Policy\Options", true);
                    key4?.DeleteValue("CollectNetEvents", false);
                    results.Add("✓ 恢复WfpDiag.ETL日志");
                }
                catch (Exception ex)
                {
                    results.Add($"✗ 恢复WfpDiag.ETL日志失败: {ex.Message}");
                }
            });

            foreach (var result in results)
            {
                if (result.StartsWith("✓"))
                    LogService.Instance.Success(result);
                else
                    LogService.Instance.Error(result);
            }

            LogService.Instance.Success("恢复系统日志完成");
        }
        catch (Exception ex)
        {
            LogService.Instance.Error($"恢复系统日志失败: {ex.Message}");
        }
        finally
        {
            _isOperating = false;
        }
    }

    private async void OptimizeMemory_Click(object sender, RoutedEventArgs e)
    {
        if (_isOperating)
        {
            LogService.Instance.Warning("内存优化正在进行中，请稍候...");
            return;
        }

        _isOperating = true;
        _cancellationTokenSource = new CancellationTokenSource();
        var token = _cancellationTokenSource.Token;

        LogService.Instance.Info("开始优化内存...");

        try
        {
            var result = await Task.Run(() => OptimizeMemoryInternal(token), token);

            if (result.Success)
            {
                LogService.Instance.Success($"内存优化完成，已优化 {result.OptimizedCount} 个进程，释放约 {result.FreedMB:F2} MB");
            }
            else if (result.Cancelled)
            {
                LogService.Instance.Warning("内存优化已取消");
            }
        }
        catch (OperationCanceledException)
        {
            LogService.Instance.Warning("内存优化已取消");
        }
        catch (Exception ex)
        {
            LogService.Instance.Error($"内存优化失败: {ex.Message}");
        }
        finally
        {
            _isOperating = false;
            _cancellationTokenSource?.Dispose();
            _cancellationTokenSource = null;
        }
    }

    private (bool Success, bool Cancelled, int OptimizedCount, double FreedMB) OptimizeMemoryInternal(CancellationToken token)
    {
        int optimizedCount = 0;
        long freedBytes = 0;

        try
        {
            var processes = Process.GetProcesses();
            var options = new ParallelOptions
            {
                CancellationToken = token,
                MaxDegreeOfParallelism = Environment.ProcessorCount
            };

            Parallel.ForEach(processes, options, process =>
            {
                if (token.IsCancellationRequested)
                    return;

                try
                {
                    var beforeMemory = process.WorkingSet64;

                    var handle = OpenProcess(PROCESS_SET_QUOTA | PROCESS_QUERY_INFORMATION, false, process.Id);
                    if (handle != IntPtr.Zero)
                    {
                        try
                        {
                            if (EmptyWorkingSet(handle))
                            {
                                Interlocked.Increment(ref optimizedCount);
                                Interlocked.Add(ref freedBytes, beforeMemory);
                            }
                        }
                        finally
                        {
                            CloseHandle(handle);
                        }
                    }
                }
                catch (Exception ex)
                {
                    LogService.Instance.Warning("[OptimizerPage.内存优化] 优化单个进程失败", ex);
                }
                finally
                {
                    process.Dispose();
                }
            });

            if (token.IsCancellationRequested)
            {
                return (false, true, optimizedCount, freedBytes / (1024.0 * 1024.0));
            }

            GC.Collect(GC.MaxGeneration, GCCollectionMode.Optimized, true);
            GC.WaitForPendingFinalizers();
            GC.Collect();

            return (true, false, optimizedCount, freedBytes / (1024.0 * 1024.0));
        }
        catch (OperationCanceledException)
        {
            return (false, true, optimizedCount, freedBytes / (1024.0 * 1024.0));
        }
    }

    private async void PauseWindowsUpdate_Click(object sender, RoutedEventArgs e)
    {
        if (_isOperating)
        {
            LogService.Instance.Warning("正在执行其他操作，请稍候...");
            return;
        }

        _isOperating = true;
        LogService.Instance.Info("开始执行: 暂停Windows更新...");

        try
        {
            var result = await Task.Run(() =>
            {
                try
                {
                    string winUpdateRegPath = @"SOFTWARE\Microsoft\WindowsUpdate\UX\Settings";
                    int pauseDays = 7000;
                    string startTime = "2023-07-07T10:00:52Z";
                    string endTime = "2052-05-02T05:21:52Z";

                    using (RegistryKey key = Registry.LocalMachine.OpenSubKey(winUpdateRegPath, true) ??
                                            Registry.LocalMachine.CreateSubKey(winUpdateRegPath))
                    {
                        key.SetValue("FlightSettingsMaxPauseDays", pauseDays, RegistryValueKind.DWord);
                        key.SetValue("PauseFeatureUpdatesStartTime", startTime, RegistryValueKind.String);
                        key.SetValue("PauseFeatureUpdatesEndTime", endTime, RegistryValueKind.String);
                        key.SetValue("PauseQualityUpdatesStartTime", startTime, RegistryValueKind.String);
                        key.SetValue("PauseQualityUpdatesEndTime", endTime, RegistryValueKind.String);
                        key.SetValue("PauseUpdatesStartTime", startTime, RegistryValueKind.String);
                        key.SetValue("PauseUpdatesExpiryTime", endTime, RegistryValueKind.String);
                    }

                    return (true, "");
                }
                catch (UnauthorizedAccessException)
                {
                    return (false, "需要管理员权限，请以管理员身份运行程序");
                }
                catch (System.Security.SecurityException)
                {
                    return (false, "需要管理员权限，请以管理员身份运行程序");
                }
                catch (Exception ex)
                {
                    return (false, ex.Message);
                }
            });

            if (result.Item1)
            {
                LogService.Instance.Success("暂停Windows更新执行成功，已延期至2052年");
            }
            else
            {
                LogService.Instance.Error($"暂停Windows更新执行失败: {result.Item2}");
            }
        }
        catch (Exception ex)
        {
            LogService.Instance.Error($"暂停Windows更新执行失败: {ex.Message}");
        }
        finally
        {
            _isOperating = false;
        }
    }
}
