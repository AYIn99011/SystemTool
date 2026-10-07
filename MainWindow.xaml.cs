using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using SystemTool.Pages;
using SystemTool.Services;

namespace SystemTool;

public partial class MainWindow : Window
{
    private DeviceInfoPage? _deviceInfoPage;
    private CleanerPage? _cleanerPage;
    private OptimizerPage? _optimizerPage;
    private RepairPage? _repairPage;
    private ToolsPage? _toolsPage;
    private LogPage? _logPage;
    
    private string _currentPageTag = "";

    [DllImport("user32.dll")]
    private static extern IntPtr SendMessage(IntPtr hWnd, int Msg, IntPtr wParam, IntPtr lParam);

    [DllImport("user32.dll")]
    private static extern bool ReleaseCapture();

    private const int WM_NCLBUTTONDOWN = 0xA1;
    private const int HTLEFT = 10;
    private const int HTRIGHT = 11;
    private const int HTTOP = 12;
    private const int HTTOPLEFT = 13;
    private const int HTTOPRIGHT = 14;
    private const int HTBOTTOM = 15;
    private const int HTBOTTOMLEFT = 16;
    private const int HTBOTTOMRIGHT = 17;

    public MainWindow()
    {
        InitializeComponent();
        Loaded += MainWindow_Loaded;
        SourceInitialized += MainWindow_SourceInitialized;
        InitUpdateLogs();
        ApplyFontScale(); // 默认 110%，启动即生效
    }

    private void InitUpdateLogs()
    {
        // 注意：Insert(0) 插到最前，所以按从旧到新调用，最终新的显示在上面
        LogService.Instance.AddUpdateLog("v1.0.0", "2026-08-20", "• 此版本为第一个正式版：修复窗口拖动问题 字体渲染问题");
        LogService.Instance.AddUpdateLog("v1.1.0", "2026-10-05", "• 设备信息页面大改：添加CPU/内存/GPU使用率进度条显示\n• 添加CPU和GPU温度实时监控\n• 添加内存频率和类型信息\n• 添加显示器信息\n• 多线程并行读取硬件数据\n• 卡片布局优化，左右对齐\n• 更换设备信息页面图标");
        LogService.Instance.AddUpdateLog("v1.2.0", "2026-10-07", "• 清理页面重做：顶部汇总卡片（一键扫描/清理、上次清理时间）、11 项清理范围可选、每项显示可清理大小\n• 扫描逻辑完善：微信/抖音/QQ音乐/网易云/酷狗多盘符自适应查找，浏览器多 Profile，补全缩略图与图标缓存路径\n• 修复：窗口缩放后部分页面文字发糊（统一 Ideal 文本渲染）\n• 修复：CPU 温度曾误显示主板热区温度，现读不到显示 --；改用 PawnIO 驱动，缺失时启动提示安装（内置安装包，无需联网）\n• 修复：WMI 温度查询失败每秒刷屏日志\n• 扫描跳过的无权限目录现记录具体名称，并跳过软链接\n• 修复：更新日志日期全部显示当天的问题\n• 新增 README 项目说明");
    }

    private void MainWindow_SourceInitialized(object? sender, EventArgs e)
    {
        var handle = new WindowInteropHelper(this).Handle;
        var hwndSource = HwndSource.FromHwnd(handle);
        hwndSource?.AddHook(WndProc);

        // 应用系统主题（深浅色自适应 + Acrylic/Mica 背景）
        Helpers.ThemeManager.ApplyTheme(this);
    }

    private void ResizeWindow(int edge)
    {
        ReleaseCapture();
        SendMessage(new WindowInteropHelper(this).Handle, WM_NCLBUTTONDOWN, (IntPtr)edge, IntPtr.Zero);
    }

    private void WindowResize_Left(object sender, MouseEventArgs e)
    {
        if (e.LeftButton == MouseButtonState.Pressed)
            ResizeWindow(HTLEFT);
    }

    private void WindowResize_Right(object sender, MouseEventArgs e)
    {
        if (e.LeftButton == MouseButtonState.Pressed)
            ResizeWindow(HTRIGHT);
    }

    private void WindowResize_Top(object sender, MouseEventArgs e)
    {
        if (e.LeftButton == MouseButtonState.Pressed)
            ResizeWindow(HTTOP);
    }

    private void WindowResize_Bottom(object sender, MouseEventArgs e)
    {
        if (e.LeftButton == MouseButtonState.Pressed)
            ResizeWindow(HTBOTTOM);
    }

    private void WindowResize_TopLeft(object sender, MouseEventArgs e)
    {
        if (e.LeftButton == MouseButtonState.Pressed)
            ResizeWindow(HTTOPLEFT);
    }

    private void WindowResize_TopRight(object sender, MouseEventArgs e)
    {
        if (e.LeftButton == MouseButtonState.Pressed)
            ResizeWindow(HTTOPRIGHT);
    }

    private void WindowResize_BottomLeft(object sender, MouseEventArgs e)
    {
        if (e.LeftButton == MouseButtonState.Pressed)
            ResizeWindow(HTBOTTOMLEFT);
    }

    private void WindowResize_BottomRight(object sender, MouseEventArgs e)
    {
        if (e.LeftButton == MouseButtonState.Pressed)
            ResizeWindow(HTBOTTOMRIGHT);
    }

    private IntPtr WndProc(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam, ref bool handled)
    {
        const int WM_GETMINMAXINFO = 0x0024;
        const int WM_SETTINGCHANGE = 0x001A;

        // 系统主题（深色/浅色）切换时跟随换肤
        if (msg == WM_SETTINGCHANGE)
        {
            Helpers.ThemeManager.ApplyTheme(this);
        }

        if (msg == WM_GETMINMAXINFO)
        {
            var mmi = (MINMAXINFO)Marshal.PtrToStructure(lParam, typeof(MINMAXINFO))!;

            var monitor = MonitorFromWindow(hwnd, 2);
            if (monitor != IntPtr.Zero)
            {
                var monitorInfo = new MONITORINFO();
                GetMonitorInfo(monitor, monitorInfo);

                var rcWorkArea = monitorInfo.rcWork;
                var rcMonitorArea = monitorInfo.rcMonitor;

                mmi.ptMaxPosition.x = rcWorkArea.left - rcMonitorArea.left;
                mmi.ptMaxPosition.y = rcWorkArea.top - rcMonitorArea.top;
                mmi.ptMaxSize.x = rcWorkArea.right - rcWorkArea.left;
                mmi.ptMaxSize.y = rcWorkArea.bottom - rcWorkArea.top;

                mmi.ptMinTrackSize.x = 800;
                mmi.ptMinTrackSize.y = 600;
            }

            Marshal.StructureToPtr(mmi, lParam, true);
            handled = true;
        }

        return IntPtr.Zero;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct POINT
    {
        public int x;
        public int y;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct MINMAXINFO
    {
        public POINT ptReserved;
        public POINT ptMaxSize;
        public POINT ptMaxPosition;
        public POINT ptMinTrackSize;
        public POINT ptMaxTrackSize;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct RECT
    {
        public int left;
        public int top;
        public int right;
        public int bottom;
    }

    [StructLayout(LayoutKind.Sequential)]
    private class MONITORINFO
    {
        public int cbSize = Marshal.SizeOf(typeof(MONITORINFO));
        public RECT rcMonitor;
        public RECT rcWork;
        public int dwFlags;
    }

    [DllImport("user32.dll")]
    private static extern IntPtr MonitorFromWindow(IntPtr hwnd, int dwFlags);

    [DllImport("user32.dll")]
    private static extern bool GetMonitorInfo(IntPtr hMonitor, MONITORINFO lpmi);

    private void MainWindow_Loaded(object sender, RoutedEventArgs e)
    {
        Dispatcher.BeginInvoke(new Action(() =>
        {
            NavigateToPage("DeviceInfo");
        }), System.Windows.Threading.DispatcherPriority.ApplicationIdle);

        // PawnIO 测温驱动缺失时提示安装（只在用户未拒绝过时弹一次）
        Dispatcher.BeginInvoke(new Action(CheckPawnIoDriver), System.Windows.Threading.DispatcherPriority.ApplicationIdle);
    }

    private void CheckPawnIoDriver()
    {
        try
        {
            if (Services.PawnIoDriverService.IsDriverInstalled
                || Services.PawnIoDriverService.IsPromptDeclined())
                return;

            var dlg = new Windows.PawnIoInstallWindow { Owner = this };
            dlg.ShowDialog();
        }
        catch (Exception ex)
        {
            Services.LogService.Instance.Warning("[MainWindow.CheckPawnIoDriver] 执行失败", ex);
        }
    }

    private void TitleBar_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (e.ClickCount == 2)
        {
            if (WindowState == WindowState.Maximized)
                WindowState = WindowState.Normal;
            else
                WindowState = WindowState.Maximized;
        }
        else
        {
            DragMove();
        }
    }

    private void MinimizeButton_Click(object sender, RoutedEventArgs e)
    {
        WindowState = WindowState.Minimized;
    }

    private void MaximizeButton_Click(object sender, RoutedEventArgs e)
    {
        if (WindowState == WindowState.Maximized)
            WindowState = WindowState.Normal;
        else
            WindowState = WindowState.Maximized;
    }

    private void CloseButton_Click(object sender, RoutedEventArgs e)
    {
        Helpers.SystemInfoHelper.Cleanup();
        Pages.ToolsPage.CleanupExtractedFiles();
        Application.Current.Shutdown();
    }

    private void NavigateToPage(string tag)
    {
        if (_currentPageTag == tag)
            return;

        UnloadCurrentPage();

        _currentPageTag = tag;

        switch (tag)
        {
            case "DeviceInfo":
                _deviceInfoPage ??= new DeviceInfoPage();
                MainFrame.Navigate(_deviceInfoPage);
                break;
            case "Cleaner":
                _cleanerPage ??= new CleanerPage();
                MainFrame.Navigate(_cleanerPage);
                break;
            case "Optimizer":
                _optimizerPage ??= new OptimizerPage();
                MainFrame.Navigate(_optimizerPage);
                break;
            case "Repair":
                _repairPage ??= new RepairPage();
                MainFrame.Navigate(_repairPage);
                break;
            case "Tools":
                _toolsPage ??= new ToolsPage();
                MainFrame.Navigate(_toolsPage);
                break;
            case "Log":
                _logPage ??= new LogPage();
                MainFrame.Navigate(_logPage);
                break;
        }
    }

    private void UnloadCurrentPage()
    {
        if (MainFrame.Content is Page currentPage)
        {
            if (currentPage is DeviceInfoPage devicePage)
            {
            }
        }
    }

    private void NavRadioButton_Checked(object sender, RoutedEventArgs e)
    {
        if (sender is RadioButton rb && rb.Tag != null)
        {
            // 默认选中的 RadioButton 会在 InitializeComponent 期间触发 Checked，
            // 此时 PageTitle 等控件尚未创建；初始导航由 MainWindow_Loaded 负责
            if (PageTitle == null) return;
            string tag = rb.Tag.ToString()!;
            PageTitle.Text = rb.Content?.ToString() ?? tag;
            NavigateToPage(tag);
        }
    }

    private void JoinGroup_Click(object sender, System.Windows.Input.MouseButtonEventArgs e)
    {
        try
        {
            System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo
            {
                FileName = "https://qm.qq.com/q/VQeAVeacUe",
                UseShellExecute = true
            });
        }
        catch
        {
            MessageBox.Show("无法打开链接，请手动访问：https://qm.qq.com/q/VQeAVeacUe", "错误", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    private void JoinQQGroupButton_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo
            {
                FileName = "https://qm.qq.com/q/B8H1n4lah",
                UseShellExecute = true
            });
        }
        catch
        {
            MessageBox.Show("无法打开链接", "错误", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    // 字体缩放档位：90% ~ 130%，默认 110%
    private static readonly double[] FontScales = { 0.9, 1.0, 1.1, 1.2, 1.3 };
    private int _fontScaleIndex = 2;

    private void FontDecrease_Click(object sender, RoutedEventArgs e)
    {
        if (_fontScaleIndex > 0)
        {
            _fontScaleIndex--;
            ApplyFontScale();
        }
    }

    private void FontIncrease_Click(object sender, RoutedEventArgs e)
    {
        if (_fontScaleIndex < FontScales.Length - 1)
        {
            _fontScaleIndex++;
            ApplyFontScale();
        }
    }

    /// <summary>
    /// 应用字体缩放：界面整体缩放 + 窗口按相同比例缩放。
    /// </summary>
    private void ApplyFontScale()
    {
        double newScale = FontScales[_fontScaleIndex];
        double oldScale = (RootGrid.LayoutTransform as ScaleTransform)?.ScaleX ?? 1.0;
        if (System.Math.Abs(newScale - oldScale) < 0.001) return;

        RootGrid.LayoutTransform = new ScaleTransform(newScale, newScale);

        // 窗口等比缩放（最大化时不调整，避免与系统窗口管理冲突）
        if (WindowState == WindowState.Normal && oldScale > 0)
        {
            double ratio = newScale / oldScale;
            Width *= ratio;
            Height *= ratio;
            MinWidth *= ratio;
            MinHeight *= ratio;
        }

        FontScaleText.Text = $"{(int)(newScale * 100)}%";
        FontDecreaseButton.IsEnabled = _fontScaleIndex > 0;
        FontIncreaseButton.IsEnabled = _fontScaleIndex < FontScales.Length - 1;
    }
}
