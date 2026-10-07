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
        // 主题画刷解冻：必须在主窗口（StartupUri）创建之前，否则原地换肤会因画刷只读而整体失败
        Helpers.ThemeManager.PrepareTheme();

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
