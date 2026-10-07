# 系统工具箱 SystemTool

WPF Windows 系统工具箱：清理 / 优化 / 修复 / 设备信息 / 日志，.NET 10 + Win11 Fluent UI（Acrylic 强模糊，自动跟随系统深浅色）。

## 构建

需要 .NET 10 SDK（Windows）：

```powershell
dotnet build -c Release
```

或推送 `v*` tag 触发 GitHub Actions 自动构建混淆单文件包。

## 目录

- `Pages/`：各功能页面（清理 / 优化 / 修复 / 设备信息 / 工具 / 日志）
- `Styles/`：Fluent 主题与控件样式（`Theme.Dark.xaml` / `Theme.Light.xaml`）
- `Helpers/`：`MicaHelper`（Acrylic/Mica 背景）、`ThemeManager`（系统主题跟随）
- `Services/`：`LogService` 等
- `GEEK.exe` / `Win11Debloat.zip`：内置第三方工具（随包调用）
