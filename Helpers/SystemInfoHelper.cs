using System;
using System.Diagnostics;
using System.Text;
using System.Linq;
using System.Runtime.InteropServices;
using System.Threading;
using Microsoft.Win32;
using LibreHardwareMonitor.Hardware;
using SystemTool.Services;

namespace SystemTool.Helpers;

public static class SystemInfoHelper
{
    private static readonly object _lock = new();
    private static Computer? _computer;
    private static bool _computerInitialized = false;
    
    private static PerformanceCounter? _cpuCounter;
    private static bool _cpuCounterInitialized = false;
    
    private static bool _staticInfoCached = false;
    private static string _cachedCpuName = "";
    private static string _cachedCpuCores = "";
    private static string _cachedGpuName = "";
    private static string _cachedGpuMemory = "";
    private static string _cachedGpuDriver = "";
    private static string _cachedMotherboard = "";
    private static string _cachedBios = "";
    private static string _cachedMemorySpeed = "";
    private static string _cachedMemoryType = "";
    private static string _cachedMonitorName = "";
    private static string _cachedMonitorResolution = "";
    private static string _cachedMonitorRefreshRate = "";

    private static Computer GetComputer()
    {
        if (!_computerInitialized)
        {
            lock (_lock)
            {
                if (!_computerInitialized)
                {
                    _computer = new Computer
                    {
                        IsCpuEnabled = true,
                        IsGpuEnabled = true,
                        IsMemoryEnabled = false,
                        IsStorageEnabled = true,
                        IsMotherboardEnabled = true,
                        IsNetworkEnabled = false,
                        IsBatteryEnabled = false,
                        IsPsuEnabled = false,
                        IsControllerEnabled = false
                    };
                    _computer.Open();
                    _computerInitialized = true;
                }
            }
        }
        return _computer!;
    }

    private static PerformanceCounter GetCpuCounter()
    {
        if (!_cpuCounterInitialized)
        {
            lock (_lock)
            {
                if (!_cpuCounterInitialized)
                {
                    _cpuCounter = new PerformanceCounter("Processor", "% Processor Time", "_Total");
                    _cpuCounter.NextValue();
                    _cpuCounterInitialized = true;
                }
            }
        }
        return _cpuCounter!;
    }

    [StructLayout(LayoutKind.Sequential)]
    private class MEMORYSTATUSEX
    {
        public uint dwLength;
        public uint dwMemoryLoad;
        public ulong ullTotalPhys;
        public ulong ullAvailPhys;
        public ulong ullTotalPageFile;
        public ulong ullAvailPageFile;
        public ulong ullTotalVirtual;
        public ulong ullAvailVirtual;
        public ulong ullAvailExtendedVirtual;

        public MEMORYSTATUSEX()
        {
            dwLength = (uint)Marshal.SizeOf(typeof(MEMORYSTATUSEX));
        }
    }

    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GlobalMemoryStatusEx([In, Out] MEMORYSTATUSEX lpBuffer);

    public static void CacheStaticInfo()
    {
        if (_staticInfoCached) return;

        // 各组相互独立，并行加载；组内把同类 WMI 查询合并为一次
        System.Threading.Tasks.Parallel.Invoke(
            () =>
            {
                var (name, cores) = GetCpuInfoInternal();
                _cachedCpuName = name;
                _cachedCpuCores = cores;
            },
            () =>
            {
                var (type, speed) = GetMemoryModulesInternal();
                _cachedMemoryType = type;
                _cachedMemorySpeed = speed;
            },
            () =>
            {
                var (names, drivers, resolution, refresh) = GetVideoControllerInternal();
                _cachedGpuName = names;
                _cachedGpuDriver = drivers;
                _cachedMonitorResolution = resolution;
                _cachedMonitorRefreshRate = refresh;
            },
            () => _cachedGpuMemory = GetGpuMemoryInternal(),
            () => _cachedMotherboard = GetMotherboardInternal(),
            () => _cachedBios = GetBiosInternal(),
            () => _cachedMonitorName = GetMonitorNameInternal(),
            () => GetDiskDrives() // 内部缓存型号/容量，并顺带算好总容量
        );
        _staticInfoCached = true;
    }

    public static string GetWindowsVersion()
    {
        try
        {
            using var key = Registry.LocalMachine.OpenSubKey(@"SOFTWARE\Microsoft\Windows NT\CurrentVersion");
            if (key != null)
            {
                var productName = key.GetValue("ProductName")?.ToString() ?? "Windows";
                var displayVersion = key.GetValue("DisplayVersion")?.ToString() ?? "";
                var currentBuildStr = key.GetValue("CurrentBuild")?.ToString() ?? "0";

                if (int.TryParse(currentBuildStr, out int currentBuild) && currentBuild >= 22000)
                {
                    productName = productName.Replace("Windows 10", "Windows 11");
                }

                return $"{productName} {displayVersion}".Trim();
            }
        }
        catch (Exception ex)
        {
            LogService.Instance.Warning("[SystemInfoHelper.GetWindowsVersion] 执行失败", ex);
        }
        return Environment.OSVersion.VersionString;
    }

    public static string GetWindowsBuild()
    {
        try
        {
            using var key = Registry.LocalMachine.OpenSubKey(@"SOFTWARE\Microsoft\Windows NT\CurrentVersion");
            if (key != null)
            {
                var displayVersion = key.GetValue("DisplayVersion")?.ToString() ?? "";
                var currentBuild = key.GetValue("CurrentBuild")?.ToString() ?? "";
                var ubr = key.GetValue("UBR")?.ToString() ?? "";
                if (!string.IsNullOrEmpty(displayVersion))
                {
                    return $"{displayVersion} (Build {currentBuild}.{ubr})";
                }
                return $"Build {currentBuild}.{ubr}";
            }
        }
        catch (Exception ex)
        {
            LogService.Instance.Warning("[SystemInfoHelper.GetWindowsBuild] 执行失败", ex);
        }
        return "未知";
    }

    public static string GetSystemArchitecture()
    {
        return Environment.Is64BitOperatingSystem ? "64位 (x64)" : "32位 (x86)";
    }

    public static string GetInstallDate()
    {
        try
        {
            using var key = Registry.LocalMachine.OpenSubKey(@"SOFTWARE\Microsoft\Windows NT\CurrentVersion");
            if (key != null)
            {
                var installDate = key.GetValue("InstallDate");
                if (installDate != null)
                {
                    var timestamp = Convert.ToInt32(installDate);
                    var date = DateTimeOffset.FromUnixTimeSeconds(timestamp).LocalDateTime;
                    return date.ToString("yyyy-MM-dd");
                }
            }
        }
        catch (Exception ex)
        {
            LogService.Instance.Warning("[SystemInfoHelper.GetInstallDate] 执行失败", ex);
        }
        return "未知";
    }

    public static string GetCpuName()
    {
        return _staticInfoCached ? _cachedCpuName : GetCpuInfoInternal().name;
    }

    public static string GetCpuCores()
    {
        return _staticInfoCached ? _cachedCpuCores : GetCpuInfoInternal().cores;
    }

    /// <summary>一次 WMI 查询同时拿 CPU 型号与核心数（原来是两次查询）。</summary>
    private static (string name, string cores) GetCpuInfoInternal()
    {
        try
        {
            using var searcher = new System.Management.ManagementObjectSearcher(
                "SELECT Name, NumberOfCores, NumberOfLogicalProcessors FROM Win32_Processor");
            foreach (var obj in searcher.Get())
            {
                var name = obj["Name"]?.ToString()?.Trim() ?? "未知";
                var cores = obj["NumberOfCores"]?.ToString() ?? "0";
                var threads = obj["NumberOfLogicalProcessors"]?.ToString() ?? "0";
                return (name, $"{cores}核 / {threads}线程");
            }
        }
        catch (Exception ex)
        {
            LogService.Instance.Warning("[SystemInfoHelper.GetCpuInfoInternal] 执行失败", ex);
        }
        return ("未知", "未知");
    }

    public static double GetCpuUsagePercent()
    {
        lock (_lock)
        {
            try
            {
                return GetCpuCounter().NextValue();
            }
            catch (Exception ex)
            {
                LogService.Instance.Warning("[SystemInfoHelper.GetCpuUsagePercent] 执行失败", ex);
            }
            return 0;
        }
    }

    public static double GetCpuTemperature()
    {
        lock (_lock)
        {
            double temp = 0;

            temp = GetCpuTemperatureFromLibreHardwareMonitorInternal();
            if (temp > 0) return temp;
        }

        // 注意：Win32_PerformanceFormattedData_Counters_ThermalZoneInformation
        // 是主板热区温度，不是 CPU 温度，曾被误标为"处理器温度"导致与 AIDA64 对不上。
        // 读不到真实 CPU 温度就返回 0（界面显示 --），不再拿错数充数。
        return 0;
    }

    private static bool? _isLaptop;

    /// <summary>
    /// 通过机箱类型判断是否为笔记本。笔记本的 SuperIO/EC 经常被误识别，
    /// 温度读数不可靠，调用方据此决定是否采信 SuperIO 传感器。
    /// </summary>
    private static bool IsLaptop()
    {
        if (_isLaptop.HasValue) return _isLaptop.Value;
        try
        {
            using var searcher = new System.Management.ManagementObjectSearcher(
                "SELECT ChassisTypes FROM Win32_SystemEnclosure");
            foreach (System.Management.ManagementObject obj in searcher.Get())
            {
                var types = obj["ChassisTypes"] as ushort[];
                if (types == null) continue;
                foreach (var t in types)
                {
                    // 8=Portable 9=Laptop 10=Notebook 11=HandHeld 12=DockingStation 14=SubNotebook
                    if (t == 8 || t == 9 || t == 10 || t == 11 || t == 12 || t == 14)
                    {
                        _isLaptop = true;
                        return true;
                    }
                }
            }
        }
        catch (Exception ex)
        {
            LogService.Instance.Warning("[SystemInfoHelper.IsLaptop] 执行失败", ex);
        }
        _isLaptop = false;
        return false;
    }

    /// <summary>野值过滤：正常工作的 CPU 温度不可能超出该范围</summary>
    private static bool IsPlausibleCpuTemp(double v) => v > 0 && v <= 115;

    private static double GetCpuTemperatureFromLibreHardwareMonitorInternal()
    {
        try
        {
            var computer = GetComputer();
            bool laptop = IsLaptop();

            double? packageTemp = null;
            double coreMax = 0;
            bool coreFound = false;
            double superIoMax = 0;
            bool superIoFound = false;

            foreach (var hardware in computer.Hardware)
            {
                hardware.Update();

                // 第一优先级：CPU 自带的封装温度（片上数字传感器，最准）
                if (hardware.HardwareType == HardwareType.Cpu)
                {
                    foreach (var sensor in hardware.Sensors)
                    {
                        if (sensor.SensorType != SensorType.Temperature || !sensor.Value.HasValue)
                            continue;
                        var v = sensor.Value.Value;
                        if (!IsPlausibleCpuTemp(v)) continue;
                        var name = sensor.Name?.ToLower() ?? "";
                        if (name.Contains("package"))
                        {
                            if (!packageTemp.HasValue || v > packageTemp.Value)
                                packageTemp = v;
                        }
                        else if (name.Contains("core"))
                        {
                            coreFound = true;
                            if (v > coreMax) coreMax = v;
                        }
                    }
                }

                // 第二优先级：SuperIO —— 笔记本上经常认错芯片，直接跳过
                if (!laptop && (hardware.HardwareType == HardwareType.SuperIO ||
                               hardware.HardwareType == HardwareType.Motherboard))
                {
                    foreach (var hw in new[] { hardware }.Concat(hardware.SubHardware))
                    {
                        if (hw.HardwareType != HardwareType.SuperIO) continue;
                        if (!ReferenceEquals(hw, hardware)) hw.Update();
                        foreach (var sensor in hw.Sensors)
                        {
                            if (sensor.SensorType != SensorType.Temperature || !sensor.Value.HasValue)
                                continue;
                            var v = sensor.Value.Value;
                            if (!IsPlausibleCpuTemp(v)) continue;
                            var name = sensor.Name?.ToLower() ?? "";
                            if (name.Contains("cpu"))
                            {
                                superIoFound = true;
                                if (v > superIoMax) superIoMax = v;
                            }
                        }
                    }
                }
            }

            if (packageTemp.HasValue) return packageTemp.Value;
            if (coreFound) return coreMax;
            if (superIoFound) return superIoMax;
            DumpLhmSensorsOnce(computer, laptop);
            return 0;
        }
        catch (Exception ex)
        {
            LogService.Instance.Warning("[SystemInfoHelper.GetCpuTemperatureFromLibreHardwareMonitorInternal] 执行失败", ex);
        }
        return 0;
    }

    private static bool _lhmDumped;

    /// <summary>LibreHardwareMonitor 读不到 CPU 温度时，一次性记录硬件/传感器清单，便于诊断是没枚举到还是名字没匹配上。</summary>
    private static void DumpLhmSensorsOnce(Computer computer, bool laptop)
    {
        if (_lhmDumped) return;
        _lhmDumped = true;
        try
        {
            var sb = new StringBuilder("[SystemInfoHelper] LHM 未读到CPU温度，硬件清单(laptop=").Append(laptop).Append("): ");
            bool cpuFound = false;
            bool cpuAllNull = true;
            foreach (var hardware in computer.Hardware)
            {
                sb.Append($"[{hardware.HardwareType}:{hardware.Name} sensors={hardware.Sensors.Length}] ");
                foreach (var s in hardware.Sensors)
                {
                    if (s.SensorType == SensorType.Temperature)
                        sb.Append($"{s.Name}={(s.Value.HasValue ? s.Value.Value.ToString("F1") : "null")}; ");
                }
                if (hardware.HardwareType == HardwareType.Cpu)
                {
                    cpuFound = true;
                    foreach (var s in hardware.Sensors)
                    {
                        if (s.SensorType == SensorType.Temperature && s.Value.HasValue)
                        {
                            cpuAllNull = false;
                            break;
                        }
                    }
                }
            }
            // CPU 硬件在但温度全 null：0.9.6 已改用 PawnIO 驱动读 MSR，但 PawnIO 需单独安装；
            // 未安装时 \\?\GLOBALROOT\Device\PawnIO 不存在，所有读数静默失败。GPU 走 NVAPI 不受影响。
            if (cpuFound && cpuAllNull)
            {
                bool pawnIoInstalled = false;
                try { pawnIoInstalled = LibreHardwareMonitor.PawnIo.PawnIo.IsInstalled; } catch { }
                object? hvci = Registry.GetValue(
                    @"HKEY_LOCAL_MACHINE\SYSTEM\CurrentControlSet\Control\DeviceGuard\Scenarios\HypervisorEnforcedCodeIntegrity",
                    "Enabled", null);
                sb.Append($"PawnIO驱动={(pawnIoInstalled ? "已安装" : "未安装")}; ");
                sb.Append($"HVCI内存完整性={(hvci?.ToString() == "1" ? "开启" : "关闭/未知")}; ");
                if (!pawnIoInstalled)
                    sb.Append("解决: 安装 PawnIO 2.2.0 (https://github.com/namazso/PawnIO.Setup/releases) 后重启，HVCI 可保持开启; ");
                else
                    sb.Append("排查: 1)以管理员运行 2)关闭冲突的测温软件后重试; ");
            }
            LogService.Instance.Warning(sb.ToString());
        }
        catch (Exception ex)
        {
            LogService.Instance.Warning("[SystemInfoHelper.DumpLhmSensorsOnce] 执行失败", ex);
        }
    }

    public static string GetTotalMemory()
    {
        try
        {
            var memStatus = new MEMORYSTATUSEX();
            if (GlobalMemoryStatusEx(memStatus))
            {
                var totalGB = memStatus.ullTotalPhys / 1024.0 / 1024.0 / 1024.0;
                return $"{totalGB:F0} GB";
            }
        }
        catch (Exception ex)
        {
            LogService.Instance.Warning("[SystemInfoHelper.GetTotalMemory] 执行失败", ex);
        }
        return "未知";
    }

    public static string GetUsedMemory()
    {
        try
        {
            var memStatus = new MEMORYSTATUSEX();
            if (GlobalMemoryStatusEx(memStatus))
            {
                var totalGB = memStatus.ullTotalPhys / 1024.0 / 1024.0 / 1024.0;
                var availGB = memStatus.ullAvailPhys / 1024.0 / 1024.0 / 1024.0;
                var usedGB = totalGB - availGB;
                return $"{usedGB:F1} GB";
            }
        }
        catch (Exception ex)
        {
            LogService.Instance.Warning("[SystemInfoHelper.GetUsedMemory] 执行失败", ex);
        }
        return "未知";
    }

    public static double GetMemoryUsagePercent()
    {
        try
        {
            var memStatus = new MEMORYSTATUSEX();
            if (GlobalMemoryStatusEx(memStatus))
            {
                return memStatus.dwMemoryLoad;
            }
        }
        catch (Exception ex)
        {
            LogService.Instance.Warning("[SystemInfoHelper.GetMemoryUsagePercent] 执行失败", ex);
        }
        return 0;
    }

    public static string GetMemorySpeed()
    {
        return _staticInfoCached ? _cachedMemorySpeed : GetMemoryModulesInternal().speed;
    }

    public static string GetMemoryType()
    {
        return _staticInfoCached ? _cachedMemoryType : GetMemoryModulesInternal().type;
    }

    /// <summary>一次 WMI 查询同时拿内存类型与频率（原来是两次查询）。</summary>
    private static (string type, string speed) GetMemoryModulesInternal()
    {
        try
        {
            using var searcher = new System.Management.ManagementObjectSearcher(
                "SELECT SMBIOSMemoryType, ConfiguredClockSpeed FROM Win32_PhysicalMemory");
            ulong totalSpeed = 0;
            int count = 0;
            string type = "未知";
            foreach (var obj in searcher.Get())
            {
                if (type == "未知")
                {
                    var typeObj = obj["SMBIOSMemoryType"];
                    if (typeObj != null)
                    {
                        type = Convert.ToInt32(typeObj) switch
                        {
                            20 => "DDR",
                            21 => "DDR2",
                            22 => "DDR2 FB-DIMM",
                            24 => "DDR3",
                            26 => "DDR4",
                            34 => "DDR5",
                            var id => $"Unknown ({id})"
                        };
                    }
                }
                var speed = obj["ConfiguredClockSpeed"];
                if (speed != null)
                {
                    totalSpeed += Convert.ToUInt32(speed);
                    count++;
                }
            }
            var speedText = count > 0 ? $"{totalSpeed / (ulong)count} MHz" : "未知";
            return (type, speedText);
        }
        catch (Exception ex)
        {
            LogService.Instance.Warning("[SystemInfoHelper.GetMemoryModulesInternal] 执行失败", ex);
        }
        return ("未知", "未知");
    }

    public static string GetGpuName()
    {
        return _staticInfoCached ? _cachedGpuName : GetVideoControllerInternal().names;
    }

    public static string GetGpuDriverVersion()
    {
        return _staticInfoCached ? _cachedGpuDriver : GetVideoControllerInternal().drivers;
    }

    public static string GetMonitorResolution()
    {
        return _staticInfoCached ? _cachedMonitorResolution : GetVideoControllerInternal().resolution;
    }

    public static string GetMonitorRefreshRate()
    {
        return _staticInfoCached ? _cachedMonitorRefreshRate : GetVideoControllerInternal().refresh;
    }

    private static bool IsVirtualGpu(string name) =>
        name.Contains("Microsoft Basic Render") ||
        name.Contains("Remote Display") ||
        name.Contains("RemoteFX") ||
        name.Contains("Virtual") ||
        name.Contains("DDA") ||
        name.Contains("RDP");

    /// <summary>一次 WMI 查询同时拿显卡型号/驱动/分辨率/刷新率（原来是四次查询）。</summary>
    private static (string names, string drivers, string resolution, string refresh) GetVideoControllerInternal()
    {
        try
        {
            using var searcher = new System.Management.ManagementObjectSearcher(
                "SELECT Name, DriverVersion, CurrentHorizontalResolution, CurrentVerticalResolution, CurrentRefreshRate FROM Win32_VideoController");
            var names = new System.Collections.Generic.List<string>();
            var drivers = new System.Collections.Generic.List<string>();
            string resolution = "未知";
            string refresh = "未知";
            foreach (var obj in searcher.Get())
            {
                var name = obj["Name"]?.ToString() ?? "";
                bool isVirtual = IsVirtualGpu(name);
                if (!isVirtual)
                {
                    if (!string.IsNullOrEmpty(name)) names.Add(name);
                    var driver = obj["DriverVersion"]?.ToString();
                    if (!string.IsNullOrEmpty(driver)) drivers.Add(driver);
                }

                // 分辨率/刷新率优先取非虚拟显卡
                var w = obj["CurrentHorizontalResolution"];
                var h = obj["CurrentVerticalResolution"];
                if (w != null && h != null)
                {
                    int wi = Convert.ToInt32(w);
                    int hi = Convert.ToInt32(h);
                    if (wi > 0 && hi > 0 && (resolution == "未知" || !isVirtual))
                    {
                        resolution = $"{wi} x {hi}";
                        var rate = obj["CurrentRefreshRate"];
                        if (rate != null && Convert.ToInt32(rate) > 0)
                            refresh = $"{Convert.ToInt32(rate)} Hz";
                        else
                            refresh = "未知";
                    }
                }
            }

            if (refresh == "未知")
            {
                try
                {
                    using var searcher2 = new System.Management.ManagementObjectSearcher("SELECT DisplayFrequency FROM Win32_DisplayConfiguration");
                    foreach (var obj in searcher2.Get())
                    {
                        var freq = obj["DisplayFrequency"];
                        if (freq != null && Convert.ToInt32(freq) > 0)
                        {
                            refresh = $"{Convert.ToInt32(freq)} Hz";
                            break;
                        }
                    }
                }
                catch (Exception ex)
                {
                    LogService.Instance.Warning("[SystemInfoHelper.GetVideoControllerInternal] 刷新率兜底查询失败", ex);
                }
            }

            return (
                names.Count > 0 ? string.Join("\n", names) : "未知",
                drivers.Count > 0 ? string.Join("\n", drivers) : "未知",
                resolution,
                refresh
            );
        }
        catch (Exception ex)
        {
            LogService.Instance.Warning("[SystemInfoHelper.GetVideoControllerInternal] 执行失败", ex);
        }
        return ("未知", "未知", "未知", "未知");
    }

    public static string GetGpuMemory()
    {
        return _staticInfoCached ? _cachedGpuMemory : GetGpuMemoryInternal();
    }

    private static string GetGpuMemoryInternal()
    {
        lock (_lock)
        {
        try
        {
            var computer = GetComputer();
            foreach (var hardware in computer.Hardware)
            {
                if (hardware.HardwareType == HardwareType.GpuNvidia ||
                    hardware.HardwareType == HardwareType.GpuAmd ||
                    hardware.HardwareType == HardwareType.GpuIntel)
                {
                    hardware.Update();
                    foreach (var sensor in hardware.Sensors)
                    {
                        if (sensor.SensorType == SensorType.SmallData &&
                            sensor.Name != null &&
                            sensor.Name.Contains("Memory Total") &&
                            sensor.Value.HasValue)
                        {
                            var memMB = sensor.Value.Value;
                            var memGB = memMB / 1024.0;
                            return memGB >= 1 ? $"{memGB:F0} GB" : $"{memMB:F0} MB";
                        }
                    }
                }
            }
        }
        catch (Exception ex)
        {
            LogService.Instance.Warning("[SystemInfoHelper.GetGpuMemoryInternal] 执行失败", ex);
        }
        return "未知";
        }
    }

    public static double GetGpuMemoryUsagePercent()
    {
        lock (_lock)
        {
            try
            {
                var computer = GetComputer();
                foreach (var hardware in computer.Hardware)
                {
                    if (hardware.HardwareType == HardwareType.GpuNvidia ||
                        hardware.HardwareType == HardwareType.GpuAmd ||
                        hardware.HardwareType == HardwareType.GpuIntel)
                    {
                        hardware.Update();
                        double? totalMemory = null;
                        double? usedMemory = null;

                        foreach (var sensor in hardware.Sensors)
                        {
                            if (sensor.SensorType == SensorType.SmallData && sensor.Name != null && sensor.Value.HasValue)
                            {
                                if (sensor.Name.Contains("Memory Total"))
                                    totalMemory = sensor.Value.Value;
                                else if (sensor.Name.Contains("Memory Used") || sensor.Name.Contains("GPU Memory Used"))
                                    usedMemory = sensor.Value.Value;
                            }
                        }

                        if (totalMemory.HasValue && usedMemory.HasValue && totalMemory > 0)
                        {
                            return (usedMemory.Value / totalMemory.Value) * 100.0;
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                LogService.Instance.Warning("[SystemInfoHelper.GetGpuMemoryUsagePercent] 执行失败", ex);
            }
            return 0;
        }
    }

    public static double GetGpuTemperature()
    {
        lock (_lock)
        {
            try
            {
                var computer = GetComputer();
                foreach (var hardware in computer.Hardware)
                {
                    if (hardware.HardwareType == HardwareType.GpuNvidia ||
                        hardware.HardwareType == HardwareType.GpuAmd ||
                        hardware.HardwareType == HardwareType.GpuIntel)
                    {
                        hardware.Update();
                        foreach (var sensor in hardware.Sensors)
                        {
                            if (sensor.SensorType == SensorType.Temperature && 
                                sensor.Name != null && 
                                sensor.Value.HasValue)
                            {
                                if (sensor.Name.Contains("Core") || 
                                    sensor.Name.Contains("GPU") ||
                                    sensor.Name.Contains("Hotspot"))
                                {
                                    return sensor.Value.Value;
                                }
                            }
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                LogService.Instance.Warning("[SystemInfoHelper.GetGpuTemperature] 执行失败", ex);
            }
            return 0;
        }
    }

    public static string GetTotalDiskSize()
    {
        // 复用 GetDiskDrives 的 WMI 缓存，不再单独查一次 Win32_DiskDrive
        try
        {
            var drives = GetDiskDrives();
            ulong totalBytes = 0;
            foreach (var d in drives)
                totalBytes += d.SizeBytes;
            if (totalBytes > 0)
                return $"{totalBytes / 1024.0 / 1024.0 / 1024.0:F0} GB";
        }
        catch (Exception ex)
        {
            LogService.Instance.Warning("[SystemInfoHelper.GetTotalDiskSize] 执行失败", ex);
        }
        return "未知";
    }

    public static string GetTotalDiskUsage()
    {
        try
        {
            ulong totalBytes = 0;
            ulong freeBytes = 0;

            var drives = System.IO.DriveInfo.GetDrives();
            foreach (var drive in drives)
            {
                if (drive.IsReady && drive.DriveType == System.IO.DriveType.Fixed)
                {
                    totalBytes += (ulong)drive.TotalSize;
                    freeBytes += (ulong)drive.TotalFreeSpace;
                }
            }

            if (totalBytes > 0)
            {
                var usage = (1 - (double)freeBytes / totalBytes) * 100;
                return $"{usage:F0}%";
            }
        }
        catch (Exception ex)
        {
            LogService.Instance.Warning("[SystemInfoHelper.GetTotalDiskUsage] 执行失败", ex);
        }
        return "未知";
    }

    /// <summary>CPU 整包功耗（RAPL），单位瓦。读不到返回 0。</summary>
    public static double GetCpuPower()
    {
        lock (_lock)
        {
            try
            {
                var computer = GetComputer();
                foreach (var hardware in computer.Hardware)
                {
                    if (hardware.HardwareType != HardwareType.Cpu) continue;
                    hardware.Update();
                    foreach (var sensor in hardware.Sensors)
                    {
                        if (sensor.SensorType != SensorType.Power || !sensor.Value.HasValue)
                            continue;
                        var name = sensor.Name?.ToLower() ?? "";
                        if (name.Contains("package"))
                            return sensor.Value.Value;
                    }
                }
            }
            catch (Exception ex)
            {
                LogService.Instance.Warning("[SystemInfoHelper.GetCpuPower] 执行失败", ex);
            }
            return 0;
        }
    }

    /// <summary>硬盘详情（WMI 型号/容量 + LHM 健康度/温度），WMI 部分只查一次并缓存。</summary>
    public class DiskDriveDetail
    {
        public string Model { get; set; } = "未知";
        public string Size { get; set; } = "";
        public ulong SizeBytes { get; set; }
        public double? HealthPercent { get; set; }
        public string HealthText => HealthPercent.HasValue ? $"{HealthPercent.Value:F0}%" : "--";
    }

    private static List<DiskDriveDetail>? _cachedDiskDrives;

    public static List<DiskDriveDetail> GetDiskDrives()
    {
        if (_cachedDiskDrives != null) return _cachedDiskDrives;
        var result = new List<DiskDriveDetail>();
        try
        {
            using var searcher = new System.Management.ManagementObjectSearcher(
                "SELECT Model, Size, MediaType FROM Win32_DiskDrive");
            foreach (var obj in searcher.Get())
            {
                var mediaType = obj["MediaType"]?.ToString() ?? "";
                if (!mediaType.Contains("Fixed") && !mediaType.Contains("SSD") && !string.IsNullOrEmpty(mediaType))
                    continue;
                var model = obj["Model"]?.ToString()?.Trim();
                if (string.IsNullOrEmpty(model)) continue;
                var size = obj["Size"];
                ulong sizeBytes = 0;
                if (size != null && ulong.TryParse(size.ToString(), out var parsed))
                    sizeBytes = parsed;
                result.Add(new DiskDriveDetail
                {
                    Model = model,
                    Size = sizeBytes > 0 ? $"{sizeBytes / 1024.0 / 1024.0 / 1024.0:F0} GB" : "",
                    SizeBytes = sizeBytes
                });
            }
        }
        catch (Exception ex)
        {
            LogService.Instance.Warning("[SystemInfoHelper.GetDiskDrives] 执行失败", ex);
        }
        _cachedDiskDrives = result;
        UpdateDiskDriveSensors(result);
        return result;
    }

    /// <summary>刷新硬盘健康度（只走 LHM，不查 WMI，可频繁调用）。按名字模糊匹配，匹配不上时按顺序兜底。</summary>
    public static void UpdateDiskDriveSensors(List<DiskDriveDetail> drives)
    {
        if (drives == null || drives.Count == 0) return;
        lock (_lock)
        {
            try
            {
                var computer = GetComputer();
                var storages = new List<(string name, double? health)>();
                foreach (var hardware in computer.Hardware)
                {
                    if (hardware.HardwareType != HardwareType.Storage) continue;
                    hardware.Update();
                    double? health = null;
                    foreach (var sensor in hardware.Sensors)
                    {
                        if (!sensor.Value.HasValue) continue;
                        if (sensor.SensorType == SensorType.Level)
                        {
                            var sname = sensor.Name?.ToLower() ?? "";
                            if (sname.Contains("life") || sname.Contains("health") || sname.Contains("wear"))
                            {
                                var v = sensor.Value.Value;
                                if (v >= 0 && v <= 100) health = v;
                            }
                        }
                    }
                    storages.Add((hardware.Name ?? "", health));
                }

                for (int i = 0; i < drives.Count; i++)
                {
                    var d = drives[i];
                    var normModel = d.Model.Replace(" ", "").ToLower();
                    (string name, double? health)? match = null;
                    foreach (var s in storages)
                    {
                        var normName = s.name.Replace(" ", "").ToLower();
                        if (normName.Contains(normModel) || normModel.Contains(normName))
                        {
                            match = s;
                            break;
                        }
                    }
                    // 名字匹配不上但数量一致时按顺序兜底
                    if (!match.HasValue && storages.Count == drives.Count)
                        match = storages[i];
                    if (match.HasValue)
                        d.HealthPercent = match.Value.health;
                }
            }
            catch (Exception ex)
            {
                LogService.Instance.Warning("[SystemInfoHelper.UpdateDiskDriveSensors] 执行失败", ex);
            }
        }
    }

    public static string GetMotherboardName()
    {
        return _staticInfoCached ? _cachedMotherboard : GetMotherboardInternal();
    }

    private static string GetMotherboardInternal()
    {
        try
        {
            using var searcher = new System.Management.ManagementObjectSearcher("SELECT Product FROM Win32_BaseBoard");
            foreach (var obj in searcher.Get())
            {
                return obj["Product"]?.ToString() ?? "未知";
            }
        }
        catch (Exception ex)
        {
            LogService.Instance.Warning("[SystemInfoHelper.GetMotherboardInternal] 执行失败", ex);
        }
        return "未知";
    }

    public static string GetBiosVersion()
    {
        return _staticInfoCached ? _cachedBios : GetBiosInternal();
    }

    private static string GetBiosInternal()
    {
        try
        {
            using var searcher = new System.Management.ManagementObjectSearcher("SELECT SMBIOSBIOSVersion FROM Win32_BIOS");
            foreach (var obj in searcher.Get())
            {
                return obj["SMBIOSBIOSVersion"]?.ToString() ?? "未知";
            }
        }
        catch (Exception ex)
        {
            LogService.Instance.Warning("[SystemInfoHelper.GetBiosInternal] 执行失败", ex);
        }
        return "未知";
    }

    public static string GetMonitorName()
    {
        return _staticInfoCached ? _cachedMonitorName : GetMonitorNameInternal();
    }

    private static string GetMonitorNameInternal()
    {
        try
        {
            using var searcher = new System.Management.ManagementObjectSearcher("root\\wmi", "SELECT InstanceName, UserFriendlyName, ManufacturerName FROM WmiMonitorID");
            foreach (var obj in searcher.Get())
            {
                var userFriendlyName = obj["UserFriendlyName"];
                if (userFriendlyName != null)
                {
                    var name = GetStringFromUInt16Array(userFriendlyName);
                    if (!string.IsNullOrEmpty(name))
                    {
                        return name;
                    }
                }
                
                var manufacturerName = obj["ManufacturerName"];
                if (manufacturerName != null)
                {
                    var mfr = GetStringFromUInt16Array(manufacturerName);
                    if (!string.IsNullOrEmpty(mfr))
                    {
                        var instanceName = obj["InstanceName"]?.ToString() ?? "";
                        var modelPart = "";
                        var parts = instanceName.Split('\\');
                        if (parts.Length > 1)
                        {
                            modelPart = parts[1].Split('&')[0];
                        }
                        return string.IsNullOrEmpty(modelPart) ? mfr : $"{mfr} {modelPart}";
                    }
                }
                
                var instanceName2 = obj["InstanceName"]?.ToString();
                if (!string.IsNullOrEmpty(instanceName2))
                {
                    var parts = instanceName2.Split('\\');
                    if (parts.Length > 1)
                    {
                        var model = parts[1].Split('&')[0];
                        if (!string.IsNullOrEmpty(model))
                        {
                            return model;
                        }
                    }
                }
            }
        }
        catch (Exception ex)
        {
            LogService.Instance.Warning("[SystemInfoHelper.GetMonitorNameInternal] 执行失败", ex);
        }
        
        try
        {
            using var searcher = new System.Management.ManagementObjectSearcher("SELECT Name, MonitorManufacturer, MonitorType FROM Win32_DesktopMonitor");
            foreach (var obj in searcher.Get())
            {
                var name = obj["Name"]?.ToString();
                var manufacturer = obj["MonitorManufacturer"]?.ToString();
                var type = obj["MonitorType"]?.ToString();
                
                if (!string.IsNullOrEmpty(name) && !name.Contains("Default") && !name.Contains("Generic"))
                {
                    return name;
                }
                
                if (!string.IsNullOrEmpty(type) && !type.Contains("Default") && !type.Contains("Generic"))
                {
                    return type;
                }
            }
        }
        catch (Exception ex)
        {
            LogService.Instance.Warning("[SystemInfoHelper.GetMonitorNameInternal] 执行失败", ex);
        }
        
        try
        {
            using var key = Microsoft.Win32.Registry.LocalMachine.OpenSubKey(@"SYSTEM\CurrentControlSet\Enum\DISPLAY");
            if (key != null)
            {
                foreach (var subKeyName in key.GetSubKeyNames())
                {
                    using var vendorKey = key.OpenSubKey(subKeyName);
                    if (vendorKey != null)
                    {
                        foreach (var deviceName in vendorKey.GetSubKeyNames())
                        {
                            using var deviceKey = vendorKey.OpenSubKey(deviceName);
                            if (deviceKey != null)
                            {
                                foreach (var instance in deviceKey.GetSubKeyNames())
                                {
                                    using var instanceKey = deviceKey.OpenSubKey(instance);
                                    if (instanceKey != null)
                                    {
                                        var friendlyName = instanceKey.GetValue("DeviceDesc")?.ToString();
                                        if (!string.IsNullOrEmpty(friendlyName))
                                        {
                                            var parts = friendlyName.Split(';');
                                            var displayName = parts.Length > 1 ? parts[^1] : friendlyName;
                                            if (!displayName.Contains("Default") && !displayName.Contains("Generic"))
                                            {
                                                return displayName;
                                            }
                                        }
                                    }
                                }
                            }
                        }
                    }
                }
            }
        }
        catch (Exception ex)
        {
            LogService.Instance.Warning("[SystemInfoHelper.未知方法] 执行失败", ex);
        }
        
        return "未知";
    }

    private static string GetStringFromUInt16Array(object obj)
    {
        if (obj is ushort[] arr)
        {
            return new string(arr.Where(c => c != 0).Select(c => (char)c).ToArray());
        }
        return "";
    }

    /// <summary>
    /// 重启资源管理器。先结束所有 explorer 进程再启动；启动失败时重试一次，
    /// 仍失败返回 false，调用方应提示用户按 Ctrl+Shift+Esc 手动启动 explorer.exe。
    /// </summary>
    public static bool RestartExplorer(string logTag)
    {
        try
        {
            foreach (var proc in System.Diagnostics.Process.GetProcessesByName("explorer"))
            {
                using (proc)
                {
                    try { proc.Kill(); }
                    catch (Exception ex)
                    {
                        LogService.Instance.Warning($"[{logTag}] 结束 explorer 失败", ex);
                    }
                }
            }
            Thread.Sleep(500);
            for (int attempt = 0; attempt < 2; attempt++)
            {
                try
                {
                    using var p = System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo
                    {
                        FileName = "explorer.exe",
                        UseShellExecute = true
                    });
                    if (p != null) return true;
                }
                catch (Exception ex)
                {
                    LogService.Instance.Warning($"[{logTag}] 启动 explorer 失败（第 {attempt + 1} 次）", ex);
                }
                Thread.Sleep(1000);
            }
            return false;
        }
        catch (Exception ex)
        {
            LogService.Instance.Warning($"[{logTag}] 重启资源管理器失败", ex);
            return false;
        }
    }

    #region 电池

    public sealed class BatteryInfo
    {
        public bool HasBattery { get; set; }
        public string Name { get; set; } = "--";
        public int ChargePercent { get; set; } = -1;
        public int Status { get; set; }
        public int EstimatedRunTimeMinutes { get; set; } = -1;
        public int TimeToFullChargeMinutes { get; set; } = -1;
        public uint DesignCapacity { get; set; }
        public uint FullChargeCapacity { get; set; }
        public double? HealthPercent =>
            DesignCapacity > 0 && FullChargeCapacity > 0
                ? Math.Round(FullChargeCapacity * 100.0 / DesignCapacity, 1)
                : null;
        /// <summary>BatteryStatus 6/7/8/9 为充电中。</summary>
        public bool IsCharging => Status is 6 or 7 or 8 or 9;
    }

    /// <summary>读取电池信息；无电池（台式机）时 HasBattery=false。</summary>
    public static BatteryInfo GetBatteryInfo()
    {
        var info = new BatteryInfo();
        try
        {
            using var searcher = new System.Management.ManagementObjectSearcher(
                "SELECT Name, BatteryStatus, EstimatedChargeRemaining, EstimatedRunTime, TimeToFullCharge, DesignCapacity, FullChargeCapacity FROM Win32_Battery");
            foreach (System.Management.ManagementObject obj in searcher.Get())
            {
                info.HasBattery = true;
                info.Name = Convert.ToString(obj["Name"]) ?? "--";
                info.ChargePercent = ToIntOr(obj["EstimatedChargeRemaining"], -1);
                info.Status = ToIntOr(obj["BatteryStatus"], 0);
                info.EstimatedRunTimeMinutes = ToMinutesOrUnknown(obj["EstimatedRunTime"]);
                info.TimeToFullChargeMinutes = ToMinutesOrUnknown(obj["TimeToFullCharge"]);
                info.DesignCapacity = ToUIntOr(obj["DesignCapacity"]);
                info.FullChargeCapacity = ToUIntOr(obj["FullChargeCapacity"]);
                break; // 只取第一块电池
            }
        }
        catch (Exception ex)
        {
            LogService.Instance.Warning("[SystemInfoHelper.GetBatteryInfo] 读取电池信息失败", ex);
        }
        return info;
    }

    /// <summary>WMI 时间字段为 0xFFFFFFFF 时表示未知。</summary>
    private static int ToMinutesOrUnknown(object? value)
    {
        try
        {
            if (value == null) return -1;
            ulong v = Convert.ToUInt64(value);
            if (v >= 0xFFFFFFFE) return -1;
            return (int)Math.Min(v, int.MaxValue);
        }
        catch { return -1; }
    }

    private static int ToIntOr(object? value, int fallback)
    {
        try { return value == null ? fallback : Convert.ToInt32(value); }
        catch { return fallback; }
    }

    private static uint ToUIntOr(object? value)
    {
        try { return value == null ? 0 : Convert.ToUInt32(value); }
        catch { return 0; }
    }

    public static string GetBatteryStatusText(int status) => status switch
    {
        3 => "已充满",
        4 => "电量低",
        5 => "电量严重不足",
        6 or 7 or 8 or 9 => "充电中",
        11 => "部分充电",
        _ => "使用电池",
    };

    /// <summary>分钟数格式化为"X小时Y分"；&lt;0 显示"--"。</summary>
    public static string FormatMinutes(int minutes)
    {
        if (minutes < 0) return "--";
        if (minutes < 60) return $"{minutes}分";
        return $"{minutes / 60}小时{minutes % 60}分";
    }

    #endregion

    public static void Cleanup()
    {
        try
        {
            _cpuCounter?.Dispose();
            _cpuCounter = null;
            _cpuCounterInitialized = false;

            if (_computer != null)
            {
                _computer.Close();
                _computer = null;
                _computerInitialized = false;
            }
        }
        catch (Exception ex)
        {
            LogService.Instance.Warning("[SystemInfoHelper.Cleanup] 执行失败", ex);
        }
    }
}
