using System;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using Microsoft.Win32;
using SystemTool.Services;

namespace SystemTool.Helpers;

/// <summary>
/// 跟随 Windows 系统深浅色主题。
/// 原理：深/浅两套字典 Key 完全相同，切换时原地修改已加载画刷的颜色，
/// StaticResource 引用的界面无需重载即可实时生效。
/// </summary>
public static class ThemeManager
{
    private const string PersonalizeKey =
        @"Software\Microsoft\Windows\CurrentVersion\Themes\Personalize";

    private static ResourceDictionary? _darkDict;
    private static ResourceDictionary? _lightDict;

    /// <summary>
    /// 应用启动时调用（必须在主窗口创建之前）：
    /// Application 级资源字典中的 SolidColorBrush 会被 WPF 自动冻结，
    /// 这里把冻结的画刷替换为可写的克隆，否则原地换肤改 Color 时会抛
    /// InvalidOperationException，导致整个 ApplyTheme 中止（换肤、DWM 背景都不生效）。
    /// </summary>
    public static void PrepareTheme()
    {
        try
        {
            var live = FindLiveThemeDict();
            if (live == null) return;
            foreach (var key in live.Keys.OfType<object>().ToList())
            {
                // Clone() 返回未冻结的可写副本；此时尚无界面元素引用旧画刷，直接替换即可
                if (live[key] is SolidColorBrush b && b.IsFrozen)
                    live[key] = b.Clone();
            }
        }
        catch (Exception ex)
        {
            LogService.Instance.Warning("[ThemeManager] 预处理主题画刷失败", ex);
        }
    }

    /// <summary>读取系统设置：当前是否为浅色主题</summary>
    public static bool IsLightTheme()
    {
        try
        {
            using var key = Registry.CurrentUser.OpenSubKey(PersonalizeKey);
            var v = key?.GetValue("AppsUseLightTheme");
            if (v is int i) return i == 1;
        }
        catch (Exception ex)
        {
            LogService.Instance.Warning("[ThemeManager] 读取系统主题失败，默认使用深色", ex);
        }
        return false;
    }

    private static ResourceDictionary LoadThemeDict(string file)
    {
        return (ResourceDictionary)Application.LoadComponent(
            new Uri($"/SystemTool;component/Styles/{file}", UriKind.Relative));
    }

    private static ResourceDictionary? FindLiveThemeDict()
    {
        return Application.Current.Resources.MergedDictionaries
            .FirstOrDefault(d => d.Source != null &&
                (d.Source.OriginalString.Contains("Theme.Dark.xaml") ||
                 d.Source.OriginalString.Contains("Theme.Light.xaml")));
    }

    /// <summary>
    /// 应用当前系统主题：换肤 + 标题栏深浅 + Acrylic/Mica 背景。
    /// 在窗口 SourceInitialized、Loaded 以及系统主题切换时调用。
    /// </summary>
    public static void ApplyTheme(Window window)
    {
        try
        {
            bool light = IsLightTheme();
            var live = FindLiveThemeDict();
            var target = light
                ? _lightDict ??= LoadThemeDict("Theme.Light.xaml")
                : _darkDict ??= LoadThemeDict("Theme.Dark.xaml");

            if (live != null)
            {
                // 原地换肤
                foreach (var key in target.Keys.OfType<object>().ToList())
                {
                    if (!live.Contains(key)) continue;
                    if (live[key] is SolidColorBrush liveBrush &&
                        target[key] is SolidColorBrush targetBrush)
                    {
                        if (liveBrush.Color != targetBrush.Color)
                            liveBrush.Color = targetBrush.Color;
                    }
                    else if (live[key] is Color && target[key] is Color targetColor)
                    {
                        live[key] = targetColor;
                    }
                }
            }

            // 系统背景（Acrylic 强模糊优先）+ 标题栏深浅跟随主题
            bool ok = MicaHelper.TryApplyBackdrop(window, darkMode: !light);

            if (window.FindName("RootGrid") is Panel root)
            {
                if (ok && live?["WindowBackgroundBrush"] is Brush tint)
                {
                    // 半透明罩染：Acrylic 模糊照透，但任何壁纸下文字都有对比度。
                    // 引用主题字典里的画刷对象：换肤时原地改色自动跟随深浅。
                    root.Background = tint;
                }
                else if (live?["FallbackBackgroundBrush"] is Brush fallback)
                {
                    // 引用主题字典里的画刷：换肤时自动跟随深浅
                    root.Background = fallback;
                }
            }
        }
        catch (Exception ex)
        {
            LogService.Instance.Warning("[ThemeManager] 应用主题失败", ex);
        }
    }
}
