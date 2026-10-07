using System;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;
using SystemTool.Services;

namespace SystemTool.Helpers;

/// <summary>
/// Win11 Mica 云母背景 / 窗口圆角 / 深色标题栏。
/// Win10 或调用失败时返回 false，调用方应使用纯色背景兜底。
/// </summary>
public static class MicaHelper
{
    private const int DWMWA_USE_IMMERSIVE_DARK_MODE = 20;
    private const int DWMWA_WINDOW_CORNER_PREFERENCE = 33;
    private const int DWMWA_SYSTEMBACKDROP_TYPE = 38;

    private const int DWMWCP_ROUND = 2;
    private const int DWMSBT_MAINWINDOW = 2; // Mica（柔和）
    private const int DWMSBT_TRANSIENTWINDOW = 3; // Acrylic（强模糊）

    [DllImport("dwmapi.dll")]
    private static extern int DwmSetWindowAttribute(IntPtr hwnd, int dwAttribute, ref int pvAttribute, int cbAttribute);

    public static bool IsWindows11OrGreater() => Environment.OSVersion.Version.Build >= 22000;

    /// <summary>
    /// 应用系统背景。优先 Acrylic 强模糊，失败回退 Mica，再失败返回 false。
    /// </summary>
    /// <param name="darkMode">深色模式标题栏（浅色主题传 false）</param>
    public static bool TryApplyBackdrop(Window window, bool darkMode)
    {
        try
        {
            var hwnd = new WindowInteropHelper(window).Handle;
            if (hwnd == IntPtr.Zero) return false;

            int dark = darkMode ? 1 : 0;
            DwmSetWindowAttribute(hwnd, DWMWA_USE_IMMERSIVE_DARK_MODE, ref dark, sizeof(int));

            int corner = DWMWCP_ROUND;
            DwmSetWindowAttribute(hwnd, DWMWA_WINDOW_CORNER_PREFERENCE, ref corner, sizeof(int));

            if (!IsWindows11OrGreater()) return false;

            // 先试 Acrylic（参考图那种强模糊），不行再 Mica
            int acrylic = DWMSBT_TRANSIENTWINDOW;
            if (DwmSetWindowAttribute(hwnd, DWMWA_SYSTEMBACKDROP_TYPE, ref acrylic, sizeof(int)) == 0)
                return true;

            int mica = DWMSBT_MAINWINDOW;
            return DwmSetWindowAttribute(hwnd, DWMWA_SYSTEMBACKDROP_TYPE, ref mica, sizeof(int)) == 0;
        }
        catch (Exception ex)
        {
            // 背景是纯视觉增强，失败不影响功能，记一条日志即可
            LogService.Instance.Warning("[MicaHelper] 应用系统背景失败，已降级为纯色背景", ex);
            return false;
        }
    }

    /// <summary>兼容旧调用：默认深色 Mica。</summary>
    public static bool TryApplyMica(Window window) => TryApplyBackdrop(window, darkMode: true);
}
