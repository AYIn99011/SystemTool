# SystemTool · 系统工具箱

Windows 桌面工具箱（WPF / .NET 10），集成设备信息、系统清理、系统优化、系统修复、实用工具与应用日志。

## 功能

- **设备信息**：CPU / 内存 / 显卡 / 磁盘 / 温度实时监控
- **系统清理**：浏览器、QQ、微信、音乐应用缓存清理，支持自定义清理范围
- **系统优化**：常用优化项一键应用
- **修复系统**：系统修复工具入口
- **其他工具**：集成 GEEK 卸载工具、Win11Debloat 等第三方工具
- **应用日志**：运行日志查看与导出

## 运行要求

- Windows 10 / 11（x64）
- **需要管理员权限运行**（硬件监控与系统清理需要）
- .NET 10 运行时（发布包为单文件自包含，无需单独安装）

## CPU 温度说明

CPU 温度通过 [LibreHardwareMonitor](https://github.com/LibreHardwareMonitor/LibreHardwareMonitor)
读取，它在 0.9.5+ 版本改用 **PawnIO** 内核驱动（已替代旧的 WinRing0）。

- PawnIO 驱动拥有正规数字签名（namazso.eu），**兼容 Windows 11 内存完整性（HVCI），无需关闭任何安全功能**
- 程序启动时若检测到驱动缺失，会弹窗询问；经你同意后自动完成安装（使用程序内置的官方安装包并校验哈希与签名，全程无需联网）
- 也可以手动安装：https://github.com/namazso/PawnIO.Setup/releases
- 不安装驱动不影响其他功能，只是 CPU 温度会显示为 `--`

## 第三方组件

| 组件 | 用途 | 说明 |
|------|------|------|
| PawnIO_setup.exe | CPU 温度读取驱动 | 官方原版内嵌，仅在用户同意后安装，见 `PawnIO_NOTICE.txt` |
| GEEK.exe | 软件卸载工具 | 内嵌资源，运行时解包到临时目录 |
| Win11Debloat.zip | Win11 精简脚本包 | 内嵌资源，运行时解包使用 |

## 从源码构建

```bash
dotnet build -c Release -p:EnableWindowsTargeting=true
```

产物在 `bin/Release/net10.0-windows/`。
