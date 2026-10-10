using System.Windows;
using SystemTool.Services;

namespace SystemTool.Windows;

/// <summary>
/// PawnIO 测温驱动安装提示窗：用户同意后自动下载、校验、静默安装。
/// </summary>
public partial class PawnIoInstallWindow : Window
{
    private readonly CancellationTokenSource _installCts = new();
    private bool _closed;

    public PawnIoInstallWindow()
    {
        InitializeComponent();
    }

    protected override void OnClosed(EventArgs e)
    {
        _closed = true;
        try { _installCts.Cancel(); } catch { }
        try { _installCts.Dispose(); } catch { }
        base.OnClosed(e);
    }

    private void CancelButton_Click(object sender, RoutedEventArgs e)
    {
        if (NeverRemindCheckBox.IsChecked == true)
            PawnIoDriverService.SetPromptDeclined();
        DialogResult = false;
        Close();
    }

    private async void InstallButton_Click(object sender, RoutedEventArgs e)
    {
        InstallButton.IsEnabled = false;
        CancelButton.IsEnabled = false;
        NeverRemindCheckBox.IsEnabled = false;
        InstallProgress.Visibility = Visibility.Visible;
        StatusText.Visibility = Visibility.Visible;

        var progress = new Progress<string>(msg =>
        {
            if (_closed) return;
            StatusText.Text = msg;
        });
        var (result, message) = await PawnIoDriverService.InstallAsync(progress, _installCts.Token);

        // 窗口已在安装期间被关闭：不再碰 UI、DialogResult，也不再弹重启确认
        if (_closed || Dispatcher.HasShutdownStarted || Dispatcher.HasShutdownFinished)
            return;

        InstallProgress.Visibility = Visibility.Collapsed;
        StatusText.Text = message;
        StatusText.Foreground = result == PawnIoDriverService.InstallResult.Failed
            ? (System.Windows.Media.Brush)FindResource("ErrorBrush")
            : (System.Windows.Media.Brush)FindResource("SuccessBrush");

        if (result == PawnIoDriverService.InstallResult.Failed)
        {
            InstallButton.IsEnabled = true;
            CancelButton.IsEnabled = true;
            NeverRemindCheckBox.IsEnabled = true;
            return;
        }

        // 成功：标记文件的处理由 PawnIoDriverService 内部完成
        //（成功删标记，仅 3010 且注册表尚未读到驱动时保留）

        if (result == PawnIoDriverService.InstallResult.SuccessRebootRequired)
        {
            var reboot = MessageBox.Show("驱动安装完成，需要重启电脑后才能读取 CPU 温度。\n\n是否现在重启？",
                "安装完成", MessageBoxButton.YesNo, MessageBoxImage.Question);
            if (reboot == MessageBoxResult.Yes)
            {
                try
                {
                    using var shutdownProcess = System.Diagnostics.Process.Start(
                        new System.Diagnostics.ProcessStartInfo("shutdown", "/r /t 5")
                        {
                            UseShellExecute = false,
                            CreateNoWindow = true,
                        });
                    Application.Current.Shutdown();
                    return;
                }
                catch (Exception ex)
                {
                    LogService.Instance.Warning("[PawnIoInstallWindow] 重启失败", ex);
                }
            }
        }

        DialogResult = true;
        Close();
    }
}
