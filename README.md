# 系统工具箱

Windows 桌面系统维护工具（WPF / .NET 10），专注轻量、透明、安全：设备信息监控、系统垃圾清理、系统优化、系统修复、实用工具。

## 界面预览

### 设备信息

![设备信息](docs/screenshots/device-info.png?v=2)

### 清理垃圾

![清理垃圾](docs/screenshots/cleaner.png)

### 系统优化

![系统优化](docs/screenshots/optimizer.png)

## 功能介绍

- **设备信息**：CPU 型号 / 核心线程 / 使用率 / 温度 / 整包功耗，内存 / 显卡 / 主板 / 显示器 / 硬盘（型号、容量、健康度）实时监控；笔记本电池状态与健康度检测
- **清理垃圾**：一键清理（系统缓存、浏览器、QQ、微信、主流音乐应用缓存等，可自定义范围）+ 高级清理（15 项深度清理可单独勾选），清理前可预估可释放空间；主流音乐应用缓存已加入用户下载与本地音频白名单保护，不会误删本地歌曲
- **系统优化**：内存优化、大系统缓存、卓越性能电源计划、VBS 与内核隔离开关、快速启动等 15 项优化
- **修复系统**：SFC / DISM 系统文件修复、网络修复（重置 Winsock / DNS / IP / 关闭代理）、桌面图标异常修复
- **其他工具**：内嵌 GEEK 卸载工具、Win11Debloat 精简脚本包、Windows 激活（中文引导）
- **应用日志**：运行日志按级别筛选查看、导出，附带版本更新记录

界面跟随 Windows 深浅色主题自动切换，支持字体缩放。系统级背景：Windows 11 上优先使用 Acrylic 强模糊（失败回退 Mica）；Windows 10 自动降级为主题纯色背景。

## 运行要求

- Windows 10 / 11（x64）
- **需要管理员权限运行**（硬件监控与系统清理需要）
- 发布包为单文件（`系统工具箱.exe`），无需单独安装 .NET 运行时

## CPU 温度说明

CPU 温度通过 [LibreHardwareMonitor](https://github.com/LibreHardwareMonitor/LibreHardwareMonitor) 读取。自 0.9.5 起，LibreHardwareMonitor 改用 **PawnIO** 内核驱动（取代旧的 WinRing0 方案）。

- 基于官方开源的 PawnIO 内核驱动（namazso / PawnIO，2.2.0），采用 SHA-256 固化校验
- 程序内置官方原版安装包：启动时若检测到驱动缺失会弹窗询问，经你同意后自动完成静默安装；安装包在执行前经过内置 SHA-256 完整性哈希校验，防止二进制被恶意替换，全程无需联网
- 也可以手动安装：https://github.com/namazso/PawnIO.Setup/releases
- 不安装驱动不影响其他功能，只是 CPU 温度会显示为 `--`

## 安全机制与风险告知（Security & Safety Boundaries）

### 为什么需要管理员权限

本工具需要以管理员身份运行，原因如下，均为功能所必需，不做任何多余提权操作：

- **系统映像修复**：SFC / DISM 需要调用 Windows 底层映像服务
- **驱动管理**：PawnIO 内核驱动的安装涉及系统服务管理
- **系统级清理**：`C:\Windows\Temp`、HKLM 注册表项等系统位置的读写
- **系统配置**：powercfg、bcdedit 等系统配置命令的执行

### 清理安全白名单原则

- **系统核心根目录保护政策**：`C:\AMD`、`C:\Intel`、`C:\NVIDIA` 等驱动根目录**永不整目录删除**，仅清理白名单内的驱动安装解压缓存子目录（如 DisplayDriver、Setup、Logs、Packages），未知子目录一律跳过
- **用户资产零触碰原则**：用户下载目录、音乐 / 视频 / 图片等媒体库不参与缓存扫描；主流音乐应用（QQ 音乐 / 网易云音乐 / 酷狗）的下载目录与歌词目录不在清理范围内，已下载歌曲与本地音频受白名单保护
- **浏览器数据保护**：Firefox 仅清理各配置文件的 `cache2` / `startupCache`，不触碰书签、密码、扩展
- **失败安全**：被占用的文件跳过并计数，不强删；清理前可预估空间，预估口径与实际清理一致

### 工具生命周期

- 内嵌的便携工具（GEEK 卸载工具、Win11Debloat 脚本包）在运行时解包到临时目录，应用退出时（正常关闭、Alt+F4、任务栏关闭、系统注销）自动清理，不残留
- 工具不驻留后台，不写入开机启动项，不创建计划任务；关闭即退出，无后台进程

## 技术栈与第三方致谢（Tech Stack & Acknowledgments）

### 核心技术栈

- .NET 10（WPF / XAML），C# 14
- P/Invoke Win32 API（DWM 系统背景、窗口消息、系统服务管理等）

### 第三方组件与上游项目

| 组件 | 上游项目 | 开源协议 | 说明 |
|------|----------|----------|------|
| LibreHardwareMonitor | [LibreHardwareMonitor/LibreHardwareMonitor](https://github.com/LibreHardwareMonitor/LibreHardwareMonitor) | MPL-2.0 | 硬件监控库，CPU 温度等传感器数据来源 |
| PawnIO 驱动安装包 | [namazso/PawnIO](https://github.com/namazso/PawnIO) | GPL-2.0 | CPU 温度读取内核驱动，官方原版内嵌，SHA-256 固化校验，仅在用户同意后安装 |
| Win11Debloat | [Raphire/Win11Debloat](https://github.com/Raphire/Win11Debloat) | MIT | Win11 精简脚本包，内嵌 zip，运行前校验哈希 |
| Microsoft-Activation-Scripts | [massgravel/Microsoft-Activation-Scripts](https://github.com/massgravel/Microsoft-Activation-Scripts) | GPL-3.0 | 激活脚本，工具页按需从上游获取执行，不内嵌 |
| Geek Uninstaller | [geekuninstaller.com](https://geekuninstaller.com) | 免费软件（闭源，个人使用免费）| 软件卸载工具，内嵌便携版，运行时解包到临时目录，退出时自动清理 |

以上组件均为各自上游项目的作品，本工具仅做集成调用；内嵌的二进制均为官方原版，运行前校验哈希，未做任何修改。

## 项目架构与构建指南（Architecture & Build）

### 项目结构概览

```
SystemTool/
├── App.xaml(.cs)        应用入口：主题初始化、全局异常兜底、退出时资源清理
├── MainWindow.xaml(.cs) 主窗口：自定义标题栏、页面导航、系统消息 / DPI 处理
├── Models/              数据模型（清理项、硬件信息等）
├── Services/            业务服务：CleanService（清理）、LogService（日志）、PawnIoDriverService（驱动安装）
├── Pages/               功能页面：设备信息 / 清理 / 优化 / 修复 / 工具 / 日志
├── Windows/             弹窗：驱动安装确认、激活中文引导等
├── Helpers/             工具类：ThemeManager（换肤）、MicaHelper（系统背景）、SystemInfoHelper（WMI / 硬件）
├── Converters/          XAML 值转换器
├── Styles/              主题资源字典（深色 / 浅色 / 按钮样式）
├── Properties/          发布配置（FolderProfile）
├── docs/                文档与截图
└── icon/                应用图标
```

欢迎提交 PR：请保持代码风格一致，清理 / 优化类改动请附带说明，涉及系统级操作（注册表、服务、驱动）请在 PR 描述中注明影响范围。

### 构建指南

环境要求：Windows 10 1809+ / Windows 11，.NET 10 SDK。

```bash
dotnet build -c Release
```

发布单文件包（VS 中可用 `FolderProfile` 发布配置）：

```bash
dotnet publish -p:PublishProfile=FolderProfile -p:EnableWindowsTargeting=true
```

产物为 `系统工具箱.exe`。
