using System.Windows;

namespace SystemTool.Windows;

/// <summary>
/// MAS 激活中文引导窗：说明英文菜单各选项含义，用户确认后才启动激活脚本。
/// </summary>
public partial class MasGuideWindow : Window
{
    public MasGuideWindow()
    {
        InitializeComponent();
    }

    private void CancelButton_Click(object sender, RoutedEventArgs e)
    {
        DialogResult = false;
        Close();
    }

    private void StartButton_Click(object sender, RoutedEventArgs e)
    {
        DialogResult = true;
        Close();
    }
}
