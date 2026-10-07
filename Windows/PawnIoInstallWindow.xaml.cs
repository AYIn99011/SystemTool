using System.Windows;
using SystemTool.Services;

namespace SystemTool.Windows;

/// <summary>
/// PawnIO 测温驱动安装提示窗：用户同意后自动下载、校验、静默安装。
/// </summary>
public partial class PawnIoInstallWindow : Window
{
    public PawnIoInstallWindow()
    {
        InitializeComponent();
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

        var progress = new Progress<string>(msg => StatusText.Text = msg);
        var (result, message) = await PawnIoDriverService.InstallAsync(progress);

        InstallProgress.Visibility = Visibility.Collapsed;
        StatusText.Text = message;
        StatusText.Foreground = result == PawnIoDriverService.InstallResult.Failed
            ? (System.Windows.Media.Brush)FindResource("ErrorBrush")
            : (System.Windows.Media.Brush)FindResource("SuccessBrush");

        if (result == PawnIoDriverService.InstallResult.Failed)
        {
            InstallButton.IsEnabled = true;
            CancelButton.IsEnabled = true;
            return;
        }

        // 成功：不再需要提示
        PawnIoDriverService.SetPromptDeclined();

        if (result == PawnIoDriverService.InstallResult.SuccessRebootRequired)
        {
            var reboot = MessageBox.Show("驱动安装完成，需要重启电脑后才能读取 CPU 温度。\n\n是否现在重启？",
                "安装完成", MessageBoxButton.YesNo, MessageBoxImage.Question);
            if (reboot == MessageBoxResult.Yes)
            {
                try
                {
                    System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo("shutdown", "/r /t 5")
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
