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

    private static ResourceDictionary? _liveThemeDict;

    /// <summary>
    /// 应用启动时调用（必须在主窗口创建之前）：
    /// 用全新加载的主题字典替换 App.xaml 预置的那个——预置字典的画刷会被 WPF
    /// 冻结，原地换肤改 Color 会抛 InvalidOperationException，导致整个 ApplyTheme
    /// 中止（换肤、DWM 背景、窗口背景全都不生效，窗口一片白）。
    /// 新字典的所有画刷逐个确保可写；此时尚无界面元素，不存在旧引用残留问题。
    /// </summary>
    public static void InitializeTheme()
    {
        try
        {
            bool light = IsLightTheme();
            var fresh = LoadThemeDict(light ? "Theme.Light.xaml" : "Theme.Dark.xaml");
            // BAML 加载的画刷可能被冻结，Clone() 返回未冻结的可写副本
            foreach (var key in fresh.Keys.OfType<object>().ToList())
            {
                if (fresh[key] is SolidColorBrush b && b.IsFrozen)
                    fresh[key] = b.Clone();
            }

            var dicts = Application.Current.Resources.MergedDictionaries;
            foreach (var d in dicts.Where(d => d.Source != null &&
                (d.Source.OriginalString.Contains("Theme.Dark.xaml") ||
                 d.Source.OriginalString.Contains("Theme.Light.xaml"))).ToList())
            {
                dicts.Remove(d);
            }
            dicts.Insert(0, fresh);
            _liveThemeDict = fresh;
        }
        catch (Exception ex)
        {
            LogService.Instance.Warning("[ThemeManager] 初始化主题失败", ex);
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
        // 优先用 InitializeTheme 登记的实例（LoadComponent 加载的字典 Source 可能为 null）
        if (_liveThemeDict != null) return _liveThemeDict;
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
                        {
                            // 正常情况直接原地改色（已创建的界面引用同一对象，实时生效）；
                            // 兜底：万一画刷被冻结则替换条目（已创建元素保持旧色，好过整个换肤中止）
                            if (liveBrush.IsFrozen)
                                live[key] = new SolidColorBrush(targetBrush.Color);
                            else
                                liveBrush.Color = targetBrush.Color;
                        }
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
