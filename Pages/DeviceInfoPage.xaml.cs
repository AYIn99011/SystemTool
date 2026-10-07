using System.Windows;
using System.Windows.Controls;
using System.Windows.Threading;
using SystemTool.Helpers;
using SystemTool.Services;

namespace SystemTool.Pages;

public partial class DeviceInfoPage : Page
{
    private DispatcherTimer? _refreshTimer;
    private bool _isStaticInfoLoaded = false;
    private bool _isLoading = false;

    public DeviceInfoPage()
    {
        InitializeComponent();
        Loaded += DeviceInfoPage_Loaded;
        Unloaded += DeviceInfoPage_Unloaded;
    }

    private void DeviceInfoPage_Loaded(object sender, RoutedEventArgs e)
    {
        StartRefreshTimer();
        
        if (!_isStaticInfoLoaded && !_isLoading)
        {
            _isLoading = true;
            _ = System.Threading.Tasks.Task.Run(() => LoadStaticSystemInfoAsync());
        }
    }

    private void DeviceInfoPage_Unloaded(object sender, RoutedEventArgs e)
    {
        StopRefreshTimer();
    }

    private void StartRefreshTimer()
    {
        if (_refreshTimer == null)
        {
            _refreshTimer = new DispatcherTimer
            {
                Interval = TimeSpan.FromSeconds(1)
            };
            _refreshTimer.Tick += RefreshTimer_Tick;
        }
        _refreshTimer.Start();
    }

    private void StopRefreshTimer()
    {
        _refreshTimer?.Stop();
    }

    private void RefreshTimer_Tick(object? sender, EventArgs e)
    {
        _ = System.Threading.Tasks.Task.Run(() => LoadDynamicSystemInfoAsync());
    }

    private void LoadStaticSystemInfoAsync()
    {
        LogService.Instance.Info("开始加载静态设备信息");
        
        try
        {
            SystemInfoHelper.CacheStaticInfo();
            
            Dispatcher.BeginInvoke(() =>
            {
                try
                {
                    OsVersionText.Text = SystemInfoHelper.GetWindowsVersion();
                    OsBuildText.Text = SystemInfoHelper.GetWindowsBuild();
                    OsArchitectureText.Text = SystemInfoHelper.GetSystemArchitecture();
                    OsInstallDateText.Text = SystemInfoHelper.GetInstallDate();
                    LogService.Instance.Info($"操作系统: {OsVersionText.Text}");

                    CpuNameText.Text = SystemInfoHelper.GetCpuName();
                    CpuCoresText.Text = SystemInfoHelper.GetCpuCores();
                    LogService.Instance.Info($"处理器: {CpuNameText.Text}");

                    MemoryTotalText.Text = SystemInfoHelper.GetTotalMemory();
                    MemoryTypeText.Text = SystemInfoHelper.GetMemoryType();
                    MemorySpeedText.Text = SystemInfoHelper.GetMemorySpeed();
                    LogService.Instance.Info($"内存: {MemoryTotalText.Text}");

                    GpuNameText.Text = SystemInfoHelper.GetGpuName();
                    GpuMemoryText.Text = SystemInfoHelper.GetGpuMemory();
                    GpuDriverText.Text = SystemInfoHelper.GetGpuDriverVersion();
                    LogService.Instance.Info($"显卡: {GpuNameText.Text}");

                    DiskTotalText.Text = SystemInfoHelper.GetTotalDiskSize();
                    LogService.Instance.Info($"存储: {DiskTotalText.Text}");

                    MotherboardText.Text = SystemInfoHelper.GetMotherboardName();
                    BiosText.Text = SystemInfoHelper.GetBiosVersion();

                    MonitorNameText.Text = SystemInfoHelper.GetMonitorName();
                    MonitorResolutionText.Text = SystemInfoHelper.GetMonitorResolution();
                    MonitorRefreshRateText.Text = SystemInfoHelper.GetMonitorRefreshRate();
                    LogService.Instance.Info($"显示器: {MonitorNameText.Text}");

                    LogService.Instance.Success("静态设备信息加载完成");
                    _isStaticInfoLoaded = true;
                    _isLoading = false;
                }
                catch (System.Exception ex)
                {
                    LogService.Instance.Error($"更新界面时出错: {ex.Message}");
                    _isLoading = false;
                }
            });
        }
        catch (System.Exception ex)
        {
            LogService.Instance.Error($"读取静态系统信息时出错: {ex.Message}");
            _isLoading = false;
        }
    }

    private void LoadDynamicSystemInfoAsync()
    {
        try
        {
            double cpuUsage = 0;
            double cpuTemp = 0;
            string memoryUsed = "";
            string memoryTotal = "";
            double memoryUsage = 0;
            double gpuMemoryUsage = 0;
            double gpuTemp = 0;
            string diskUsage = "";

            System.Threading.Tasks.Parallel.Invoke(
                () => cpuUsage = SystemInfoHelper.GetCpuUsagePercent(),
                () => cpuTemp = SystemInfoHelper.GetCpuTemperature(),
                () => { memoryUsed = SystemInfoHelper.GetUsedMemory(); memoryTotal = SystemInfoHelper.GetTotalMemory(); memoryUsage = SystemInfoHelper.GetMemoryUsagePercent(); },
                () => gpuMemoryUsage = SystemInfoHelper.GetGpuMemoryUsagePercent(),
                () => gpuTemp = SystemInfoHelper.GetGpuTemperature(),
                () => diskUsage = SystemInfoHelper.GetTotalDiskUsage()
            );

            Dispatcher.BeginInvoke(() =>
            {
                try
                {
                    CpuUsageText.Text = $"{cpuUsage:F0}%";
                    CpuUsageBar.Value = cpuUsage;
                    UpdateUsageColor(CpuUsageText, CpuUsageBar, cpuUsage, "#4ade80", "#fbbf24", "#ef4444");

                    CpuTempText.Text = cpuTemp > 0 ? $"{cpuTemp:F0}°C" : "--°C";
                    UpdateTempColor(CpuTempText, cpuTemp);

                    MemoryUsageText.Text = $"{memoryUsage:F0}%";
                    MemoryUsedText.Text = $"{memoryUsed} / {memoryTotal}";
                    MemoryUsageBar.Value = memoryUsage;
                    UpdateUsageColor(MemoryUsageText, MemoryUsageBar, memoryUsage, "#fbbf24", "#f97316", "#ef4444");

                    GpuMemoryUsageText.Text = $"{gpuMemoryUsage:F0}%";
                    GpuMemoryUsageBar.Value = gpuMemoryUsage;
                    UpdateUsageColor(GpuMemoryUsageText, GpuMemoryUsageBar, gpuMemoryUsage, "#a78bfa", "#f97316", "#ef4444");

                    GpuTempText.Text = gpuTemp > 0 ? $"{gpuTemp:F0}°C" : "--°C";
                    UpdateTempColor(GpuTempText, gpuTemp);

                    DiskUsageText.Text = diskUsage;
                }
                catch (Exception ex)
                {
                    LogService.Instance.Warning("[DeviceInfoPage.LoadDynamicSystemInfoAsync] 执行失败", ex);
                }
            });
        }
        catch (System.Exception ex)
        {
            LogService.Instance.Error($"读取动态系统信息时出错: {ex.Message}");
        }
    }

    private void UpdateUsageColor(TextBlock textBlock, ProgressBar progressBar, 
                                   double usage, string normalColor, string warningColor, string dangerColor)
    {
        string color;
        if (usage >= 80)
        {
            color = dangerColor;
        }
        else if (usage >= 60)
        {
            color = warningColor;
        }
        else
        {
            color = normalColor;
        }

        var brushConverter = new System.Windows.Media.BrushConverter();
        var brush = brushConverter.ConvertFromString(color) as System.Windows.Media.Brush;
        if (brush != null)
        {
            textBlock.Foreground = brush;
            progressBar.Foreground = brush;
        }
    }

    private void UpdateTempColor(TextBlock textBlock, double temp)
    {
        string color;
        if (temp >= 80)
        {
            color = "#ef4444";
        }
        else if (temp >= 60)
        {
            color = "#f97316";
        }
        else if (temp > 0)
        {
            color = "#60a5fa";
        }
        else
        {
            color = "#7a7a9a";
        }

        var brushConverter = new System.Windows.Media.BrushConverter();
        var brush = brushConverter.ConvertFromString(color) as System.Windows.Media.Brush;
        if (brush != null)
        {
            textBlock.Foreground = brush;
        }
    }
}
