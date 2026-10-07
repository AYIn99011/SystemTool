using System;
using System.Diagnostics;
using System.Linq;
using System.Runtime.InteropServices;
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
                        IsStorageEnabled = false,
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
        
        _cachedCpuName = GetCpuNameInternal();
        _cachedCpuCores = GetCpuCoresInternal();
        _cachedGpuName = GetGpuNameInternal();
        _cachedGpuMemory = GetGpuMemoryInternal();
        _cachedGpuDriver = GetGpuDriverInternal();
        _cachedMotherboard = GetMotherboardInternal();
        _cachedBios = GetBiosInternal();
        _cachedMemorySpeed = GetMemorySpeedInternal();
        _cachedMemoryType = GetMemoryTypeInternal();
        _cachedMonitorName = GetMonitorNameInternal();
        _cachedMonitorResolution = GetMonitorResolutionInternal();
        _cachedMonitorRefreshRate = GetMonitorRefreshRateInternal();
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
        return _staticInfoCached ? _cachedCpuName : GetCpuNameInternal();
    }

    private static string GetCpuNameInternal()
    {
        try
        {
            using var searcher = new System.Management.ManagementObjectSearcher("SELECT Name FROM Win32_Processor");
            foreach (var obj in searcher.Get())
            {
                return obj["Name"]?.ToString()?.Trim() ?? "未知";
            }
        }
        catch (Exception ex)
        {
            LogService.Instance.Warning("[SystemInfoHelper.GetCpuNameInternal] 执行失败", ex);
        }
        return "未知";
    }

    public static string GetCpuCores()
    {
        return _staticInfoCached ? _cachedCpuCores : GetCpuCoresInternal();
    }

    private static string GetCpuCoresInternal()
    {
        try
        {
            using var searcher = new System.Management.ManagementObjectSearcher("SELECT NumberOfCores, NumberOfLogicalProcessors FROM Win32_Processor");
            foreach (var obj in searcher.Get())
            {
                var cores = obj["NumberOfCores"]?.ToString() ?? "0";
                var threads = obj["NumberOfLogicalProcessors"]?.ToString() ?? "0";
                return $"{cores}核 / {threads}线程";
            }
        }
        catch (Exception ex)
        {
            LogService.Instance.Warning("[SystemInfoHelper.GetCpuCoresInternal] 执行失败", ex);
        }
        return "未知";
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

        double temp2 = GetCpuTemperatureFromWmi();
        if (temp2 > 0) return temp2;

        temp2 = GetCpuTemperatureFromMsaAcpi();
        if (temp2 > 0) return temp2;

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
            return 0;
        }
        catch (Exception ex)
        {
            LogService.Instance.Warning("[SystemInfoHelper.GetCpuTemperatureFromLibreHardwareMonitorInternal] 执行失败", ex);
        }
        return 0;
    }

    private static double GetCpuTemperatureFromWmi()
    {
        try
        {
            using var searcher = new System.Management.ManagementObjectSearcher(
                "SELECT Name, Temperature FROM Win32_PerformanceFormattedData_Counters_ThermalZoneInformation");
            
            foreach (var obj in searcher.Get())
            {
                var name = obj["Name"]?.ToString()?.ToLower() ?? "";
                var temp = obj["Temperature"];
                
                if (temp != null && (name.Contains("cpu") || name.Contains("processor") || name.Contains("thermal")))
                {
                    var tempValue = Convert.ToDouble(temp);
                    return tempValue - 273.15;
                }
            }
        }
        catch (Exception ex)
        {
            LogService.Instance.Warning("[SystemInfoHelper.GetCpuTemperatureFromWmi] 执行失败", ex);
        }
        return 0;
    }

    private static double GetCpuTemperatureFromMsaAcpi()
    {
        try
        {
            using var searcher = new System.Management.ManagementObjectSearcher(
                "root\\WMI", "SELECT CurrentTemperature FROM MSAcpi_ThermalZoneTemperature");
            
            foreach (var obj in searcher.Get())
            {
                var temp = obj["CurrentTemperature"];
                if (temp != null)
                {
                    var tempValue = Convert.ToDouble(temp);
                    return (tempValue - 2732) / 10.0;
                }
            }
        }
        catch (Exception ex)
        {
            LogService.Instance.Warning("[SystemInfoHelper.GetCpuTemperatureFromMsaAcpi] 执行失败", ex);
        }
        return 0;
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
        return _staticInfoCached ? _cachedMemorySpeed : GetMemorySpeedInternal();
    }

    private static string GetMemorySpeedInternal()
    {
        try
        {
            using var searcher = new System.Management.ManagementObjectSearcher("SELECT ConfiguredClockSpeed FROM Win32_PhysicalMemory");
            ulong totalSpeed = 0;
            int count = 0;
            foreach (var obj in searcher.Get())
            {
                var speed = obj["ConfiguredClockSpeed"];
                if (speed != null)
                {
                    totalSpeed += Convert.ToUInt32(speed);
                    count++;
                }
            }
            if (count > 0)
            {
                var avgSpeed = totalSpeed / (ulong)count;
                return $"{avgSpeed} MHz";
            }
        }
        catch (Exception ex)
        {
            LogService.Instance.Warning("[SystemInfoHelper.GetMemorySpeedInternal] 执行失败", ex);
        }
        return "未知";
    }

    public static string GetMemoryType()
    {
        return _staticInfoCached ? _cachedMemoryType : GetMemoryTypeInternal();
    }

    private static string GetMemoryTypeInternal()
    {
        try
        {
            using var searcher = new System.Management.ManagementObjectSearcher("SELECT SMBIOSMemoryType FROM Win32_PhysicalMemory");
            foreach (var obj in searcher.Get())
            {
                var type = obj["SMBIOSMemoryType"];
                if (type != null)
                {
                    var typeId = Convert.ToInt32(type);
                    return typeId switch
                    {
                        20 => "DDR",
                        21 => "DDR2",
                        22 => "DDR2 FB-DIMM",
                        24 => "DDR3",
                        26 => "DDR4",
                        34 => "DDR5",
                        _ => $"Unknown ({typeId})"
                    };
                }
            }
        }
        catch (Exception ex)
        {
            LogService.Instance.Warning("[SystemInfoHelper.GetMemoryTypeInternal] 执行失败", ex);
        }
        return "未知";
    }

    public static string GetGpuName()
    {
        return _staticInfoCached ? _cachedGpuName : GetGpuNameInternal();
    }

    private static string GetGpuNameInternal()
    {
        try
        {
            using var searcher = new System.Management.ManagementObjectSearcher("SELECT Name FROM Win32_VideoController");
            var gpus = new System.Collections.Generic.List<string>();
            foreach (var obj in searcher.Get())
            {
                var name = obj["Name"]?.ToString();
                if (!string.IsNullOrEmpty(name) &&
                    !name.Contains("Microsoft Basic Render") &&
                    !name.Contains("Remote Display") &&
                    !name.Contains("RemoteFX") &&
                    !name.Contains("Virtual") &&
                    !name.Contains("DDA") &&
                    !name.Contains("RDP"))
                {
                    gpus.Add(name);
                }
            }
            return gpus.Count > 0 ? string.Join("\n", gpus) : "未知";
        }
        catch (Exception ex)
        {
            LogService.Instance.Warning("[SystemInfoHelper.GetGpuNameInternal] 执行失败", ex);
        }
        return "未知";
    }

    public static string GetGpuMemory()
    {
        return _staticInfoCached ? _cachedGpuMemory : GetGpuMemoryInternal();
    }

    private static string GetGpuMemoryInternal()
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

    public static string GetGpuDriverVersion()
    {
        return _staticInfoCached ? _cachedGpuDriver : GetGpuDriverInternal();
    }

    private static string GetGpuDriverInternal()
    {
        try
        {
            using var searcher = new System.Management.ManagementObjectSearcher("SELECT Name, DriverVersion FROM Win32_VideoController");
            var drivers = new System.Collections.Generic.List<string>();
            foreach (var obj in searcher.Get())
            {
                var name = obj["Name"]?.ToString() ?? "";
                if (name.Contains("Microsoft Basic Render") ||
                    name.Contains("Remote Display") ||
                    name.Contains("RemoteFX") ||
                    name.Contains("Virtual") ||
                    name.Contains("DDA") ||
                    name.Contains("RDP"))
                {
                    continue;
                }
                var driver = obj["DriverVersion"]?.ToString();
                if (!string.IsNullOrEmpty(driver))
                {
                    drivers.Add(driver);
                }
            }
            return drivers.Count > 0 ? string.Join("\n", drivers) : "未知";
        }
        catch (Exception ex)
        {
            LogService.Instance.Warning("[SystemInfoHelper.GetGpuDriverInternal] 执行失败", ex);
        }
        return "未知";
    }

    public static string GetTotalDiskSize()
    {
        try
        {
            ulong totalBytes = 0;
            using var searcher = new System.Management.ManagementObjectSearcher("SELECT Size, MediaType FROM Win32_DiskDrive");
            foreach (var obj in searcher.Get())
            {
                var mediaType = obj["MediaType"]?.ToString() ?? "";
                if (mediaType.Contains("Fixed") || mediaType.Contains("SSD") || string.IsNullOrEmpty(mediaType))
                {
                    var size = obj["Size"];
                    if (size != null)
                    {
                        totalBytes += Convert.ToUInt64(size);
                    }
                }
            }
            var totalGB = totalBytes / 1024.0 / 1024.0 / 1024.0;
            return $"{totalGB:F0} GB";
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

    public static string GetMonitorResolution()
    {
        return _staticInfoCached ? _cachedMonitorResolution : GetMonitorResolutionInternal();
    }

    private static string GetMonitorResolutionInternal()
    {
        try
        {
            using var searcher = new System.Management.ManagementObjectSearcher("SELECT CurrentHorizontalResolution, CurrentVerticalResolution FROM Win32_VideoController");
            foreach (var obj in searcher.Get())
            {
                var width = obj["CurrentHorizontalResolution"];
                var height = obj["CurrentVerticalResolution"];
                if (width != null && height != null)
                {
                    var w = Convert.ToInt32(width);
                    var h = Convert.ToInt32(height);
                    if (w > 0 && h > 0)
                    {
                        return $"{w} x {h}";
                    }
                }
            }
        }
        catch (Exception ex)
        {
            LogService.Instance.Warning("[SystemInfoHelper.GetMonitorResolutionInternal] 执行失败", ex);
        }
        return "未知";
    }

    public static string GetMonitorRefreshRate()
    {
        return _staticInfoCached ? _cachedMonitorRefreshRate : GetMonitorRefreshRateInternal();
    }

    private static string GetMonitorRefreshRateInternal()
    {
        try
        {
            using var searcher = new System.Management.ManagementObjectSearcher("SELECT CurrentRefreshRate FROM Win32_VideoController");
            foreach (var obj in searcher.Get())
            {
                var refreshRate = obj["CurrentRefreshRate"];
                if (refreshRate != null)
                {
                    var rate = Convert.ToInt32(refreshRate);
                    if (rate > 0)
                    {
                        return $"{rate} Hz";
                    }
                }
            }
        }
        catch (Exception ex)
        {
            LogService.Instance.Warning("[SystemInfoHelper.GetMonitorRefreshRateInternal] 执行失败", ex);
        }
        
        try
        {
            using var searcher = new System.Management.ManagementObjectSearcher("SELECT DisplayFrequency FROM Win32_DisplayConfiguration");
            foreach (var obj in searcher.Get())
            {
                var freq = obj["DisplayFrequency"];
                if (freq != null)
                {
                    var rate = Convert.ToInt32(freq);
                    if (rate > 0)
                    {
                        return $"{rate} Hz";
                    }
                }
            }
        }
        catch (Exception ex)
        {
            LogService.Instance.Warning("[SystemInfoHelper.GetMonitorRefreshRateInternal] 执行失败", ex);
        }
        return "未知";
    }

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
