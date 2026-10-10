using System;
using System.Diagnostics;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Threading;
using System.Security.Principal;
using SystemTool.Services;

namespace SystemTool;

public partial class App : Application
{
    protected override void OnStartup(StartupEventArgs e)
    {
        // 主题初始化：必须在主窗口（StartupUri）创建之前，用可写的主题字典替换预置字典
        Helpers.ThemeManager.InitializeTheme();

        // 全局异常兜底：任何未被 catch 的异常都记入应用日志，而不是静默消失
        DispatcherUnhandledException += OnDispatcherUnhandledException;
        TaskScheduler.UnobservedTaskException += OnUnobservedTaskException;
        AppDomain.CurrentDomain.UnhandledException += OnUnhandledException;

        if (!IsRunningAsAdministrator())
        {
            MessageBox.Show("此程序需要管理员权限才能运行。\n请右键点击程序并选择\"以管理员身份运行\"。", 
                "权限不足", MessageBoxButton.OK, MessageBoxImage.Error);
            Shutdown();
            return;
        }

        base.OnStartup(e);
    }

    protected override void OnExit(ExitEventArgs e)
    {
        // 全局保底清理：无论应用经由何种途径退出（Alt+F4、任务栏关闭、注销/关机），
        // 解压工具残留与硬件监控资源都必须释放。
        // 两个方法均为静态、幂等、内部抑制异常；此处再加一层保护，确保退出流程永不卡死。
        try { Pages.ToolsPage.CleanupExtractedFiles(); } catch { }
        try { Helpers.SystemInfoHelper.Cleanup(); } catch { }

        base.OnExit(e);
    }

    private void OnDispatcherUnhandledException(object sender, DispatcherUnhandledExceptionEventArgs e)
    {
        LogService.Instance.Error("未处理的 UI 线程异常", e.Exception);
        e.Handled = true; // 已记录，阻止程序直接崩溃
    }

    private void OnUnobservedTaskException(object? sender, UnobservedTaskExceptionEventArgs e)
    {
        LogService.Instance.Error("未观察到的后台任务异常", e.Exception);
        e.SetObserved();
    }

    private void OnUnhandledException(object sender, UnhandledExceptionEventArgs e)
    {
        LogService.Instance.Error("未处理的非 UI 线程异常", e.ExceptionObject as Exception);
    }

    private static bool IsRunningAsAdministrator()
    {
        using WindowsIdentity identity = WindowsIdentity.GetCurrent();
        WindowsPrincipal principal = new(identity);
        return principal.IsInRole(WindowsBuiltInRole.Administrator);
    }
}
