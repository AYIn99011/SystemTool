using System.Diagnostics;
using System.IO;
using SystemTool.Models;

namespace SystemTool.Services
{
    public class CleanService
    {
        public List<CleanGroup> GetCleanGroups()
        {
            return new List<CleanGroup>
            {
                new CleanGroup
                {
                    Name = "临时文件",
                    Items = new List<CleanItem>
                    {
                        new CleanItem
                        {
                            Name = "Windows临时文件",
                            Description = "系统和应用程序产生的临时文件",
                            Level = 2,
                            Paths = new List<string>
                            {
                                Environment.ExpandEnvironmentVariables("%TEMP%"),
                                Environment.ExpandEnvironmentVariables("%SystemRoot%\\Temp"),
                                Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Windows), "ServiceProfiles", "NetworkService", "AppData", "Local", "Temp"),
                                Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Windows), "ServiceProfiles", "LocalService", "AppData", "Local", "Temp")
                            }
                        },
                        new CleanItem
                        {
                            Name = "Windows临时安装文件",
                            Description = "系统升级或还原后留下的临时安装文件",
                            Warning = "如果正在系统升级请勿清理",
                            Level = 2,
                            Paths = new List<string>
                            {
                                Path.Combine(Path.GetPathRoot(Environment.SystemDirectory) ?? "C:\\", "$Windows.~BT"),
                                Path.Combine(Path.GetPathRoot(Environment.SystemDirectory) ?? "C:\\", "$Windows.~WS"),
                                Path.Combine(Path.GetPathRoot(Environment.SystemDirectory) ?? "C:\\", "$Windows.~LS")
                            }
                        },
                        new CleanItem
                        {
                            Name = "驱动临时解压目录",
                            Description = "Intel、AMD以及Nvidia驱动在安装时留下的解压目录（仅清理明确属于安装包的特征子目录，C:\\AMD、C:\\Intel、C:\\NVIDIA 根目录本身永不删除）",
                            Level = 3,
                            Paths = GetDriverTempExtractionPaths()
                        }
                    }
                },
                new CleanGroup
                {
                    Name = "缓存文件",
                    Items = new List<CleanItem>
                    {
                        new CleanItem
                        {
                            Name = "Windows 更新下载缓存",
                            Description = "Windows 在下载更新以及更新 Metro 应用时产生的临时安装包",
                            Warning = "清理后可能无法卸载某些更新",
                            Level = 2,
                            Paths = new List<string>
                            {
                                WindowsUpdateDownloadPath
                            }
                        },
                        new CleanItem
                        {
                            Name = "传递优化缓存",
                            Description = "Win10开始使用传递优化服务下载Windows更新和UWP应用产生的缓存文件",
                            Level = 2,
                            Paths = new List<string>
                            {
                                Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Windows), "SoftwareDistribution", "DeliveryOptimization"),
                                Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Windows), "DeliveryOptimization")
                            }
                        },
                        new CleanItem
                        {
                            Name = "NuGet包缓存",
                            Description = ".NET应用程序使用Visual Studio生成时产生的缓存",
                            Warning = "如果未在Visual Studio中打开允许NuGet下载缺少的程序包，则应用生成将会失败",
                            Level = 2,
                            Paths = new List<string>
                            {
                                Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".nuget"),
                                Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "NuGet", "v3-cache")
                            }
                        },
                        new CleanItem
                        {
                            Name = "Windows升级备份",
                            Description = "系统升级后留下的旧版本备份",
                            Warning = "删除后无法回滚到以前版本",
                            Level = 2,
                            Paths = new List<string>
                            {
                                Path.Combine(Path.GetPathRoot(Environment.SystemDirectory) ?? "C:\\", "Windows.old")
                            }
                        },
                        new CleanItem
                        {
                            Name = "Windows错误报告",
                            Description = "Windows错误报告和内存转储文件",
                            Level = 2,
                            Paths = new List<string>
                            {
                                Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData), "Microsoft", "Windows", "WER"),
                                Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "CrashDumps"),
                                Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Windows), "Minidump")
                            }
                        }
                    }
                },
                new CleanGroup
                {
                    Name = "日志文件",
                    Items = new List<CleanItem>
                    {
                        new CleanItem
                        {
                            Name = "Windows日志",
                            Description = "Windows在运行时产生的日志文件",
                            Level = 3,
                            Paths = new List<string>
                            {
                                Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Windows), "Logs"),
                                Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Windows), "debug"),
                                Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Windows), "Performance", "WinSAT", "DataStore")
                            }
                        },
                        new CleanItem
                        {
                            Name = "CBS日志",
                            Description = "Windows组件基于服务的日志文件",
                            Level = 3,
                            Paths = new List<string>
                            {
                                Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Windows), "Logs", "CBS")
                            }
                        },
                        new CleanItem
                        {
                            Name = "DISM日志",
                            Description = "Windows部署映像服务和管理日志",
                            Level = 3,
                            Paths = new List<string>
                            {
                                Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Windows), "Logs", "DISM")
                            }
                        }
                    }
                },
                new CleanGroup
                {
                    Name = "浏览器缓存",
                    Items = new List<CleanItem>
                    {
                        new CleanItem
                        {
                            Name = "Chrome缓存",
                            Description = "Google Chrome浏览器缓存数据",
                            Level = 2,
                            Paths = GetChromeCachePaths()
                        },
                        new CleanItem
                        {
                            Name = "Edge缓存",
                            Description = "Microsoft Edge浏览器缓存数据",
                            Level = 2,
                            Paths = GetEdgeCachePaths()
                        },
                        new CleanItem
                        {
                            Name = "IE缓存",
                            Description = "Internet Explorer临时文件",
                            Level = 3,
                            Paths = new List<string>
                            {
                                Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Microsoft", "Windows", "INetCache"),
                                Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Microsoft", "Windows", "Temporary Internet Files")
                            }
                        }
                    }
                },
                new CleanGroup
                {
                    Name = "应用程序缓存",
                    Items = new List<CleanItem>
                    {
                        new CleanItem
                        {
                            Name = "Windows Defender扫描历史",
                            Description = "Windows Defender扫描历史记录",
                            Level = 3,
                            Paths = new List<string>
                            {
                                Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData), "Microsoft", "Windows Defender", "Scans", "History")
                            }
                        }
                    }
                }
            };
        }

        /// <summary>
        /// 驱动解压包特征子目录白名单：仅清理这些明确属于解压安装包的目录。
        /// 未知子目录一律跳过；C:\AMD、C:\Intel、C:\NVIDIA 根目录本身永不删除。
        /// </summary>
        private static readonly string[] DriverExtractionFolderWhitelist =
        {
            "DisplayDriver", "Setup", "Logs", "Packages",
            "Installer", "Install", "Extract", "Extraction",
            "Temp", "Tmp", "Drivers", "Driver", "Update"
        };

        /// <summary>驱动根目录下视为临时文件的扩展名特征（仅顶层）。</summary>
        private static readonly string[] DriverTempFilePatterns = { "*.tmp", "*.log" };

        /// <summary>
        /// 扫描驱动解压根目录（C:\AMD、C:\Intel、C:\NVIDIA），仅返回白名单内的
        /// 特征子目录及根目录下的临时文件。根目录本身永不返回、永不删除。
        /// </summary>
        private static List<string> GetDriverTempExtractionPaths()
        {
            var paths = new List<string>();
            var systemRoot = Path.GetPathRoot(Environment.SystemDirectory) ?? "C:\\";

            foreach (var rootName in new[] { "AMD", "Intel", "NVIDIA" })
            {
                var root = Path.Combine(systemRoot, rootName);
                if (!Directory.Exists(root))
                    continue;

                string[] subDirs;
                try { subDirs = Directory.GetDirectories(root); }
                catch { continue; } // 根目录无权限：整个跳过，绝不强删

                foreach (var subDir in subDirs)
                {
                    string name;
                    try { name = Path.GetFileName(subDir); }
                    catch { continue; }

                    var whitelisted = false;
                    foreach (var w in DriverExtractionFolderWhitelist)
                    {
                        if (string.Equals(w, name, StringComparison.OrdinalIgnoreCase))
                        {
                            whitelisted = true;
                            break;
                        }
                    }
                    if (whitelisted)
                        paths.Add(subDir);
                    // 未知子目录：一律跳过
                }

                foreach (var pattern in DriverTempFilePatterns)
                {
                    string[] files;
                    try { files = Directory.GetFiles(root, pattern, SearchOption.TopDirectoryOnly); }
                    catch { continue; }
                    paths.AddRange(files);
                }
            }

            return paths;
        }

        /// <summary>
        /// Windows 更新下载缓存的标准路径。清理项与免占用判断都以它为准，
        /// 避免多处硬编码导致重复定义。
        /// </summary>
        private static readonly string WindowsUpdateDownloadPath =
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Windows), "SoftwareDistribution", "Download");

        /// <summary>判断一批清理路径中是否包含 Windows 更新下载缓存。</summary>
        private static bool ContainsWindowsUpdateDownloadPath(List<string> paths)
        {
            var target = WindowsUpdateDownloadPath.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
            foreach (var p in paths)
            {
                if (string.IsNullOrWhiteSpace(p))
                    continue;
                var cur = p.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
                if (string.Equals(cur, target, StringComparison.OrdinalIgnoreCase))
                    return true;
            }
            return false;
        }

        /// <summary>查询服务是否处于 RUNNING 状态（sc query，失败视为未运行）。</summary>
        private static bool IsServiceRunning(string serviceName)
        {
            try
            {
                using var p = Process.Start(new ProcessStartInfo
                {
                    FileName = "sc.exe",
                    Arguments = $"query {serviceName}",
                    UseShellExecute = false,
                    CreateNoWindow = true,
                    RedirectStandardOutput = true,
                    RedirectStandardError = true
                });
                if (p == null)
                    return false;
                var output = p.StandardOutput.ReadToEnd();
                p.WaitForExit(10000);
                return output.Contains("RUNNING", StringComparison.OrdinalIgnoreCase);
            }
            catch
            {
                return false;
            }
        }

        /// <summary>
        /// 尝试停止 wuauserv（Windows Update）服务，避免清理时文件被占用。
        /// 返回 true 表示"服务原本在运行且已被我们停止"，调用方清理完成后必须恢复；
        /// 无权限/超时/失败一律返回 false，调用方降级为跳过占用文件。
        /// </summary>
        private static bool TryStopWindowsUpdateService()
        {
            try
            {
                if (!IsServiceRunning("wuauserv"))
                    return false; // 本来就没跑，无需恢复

                using var p = Process.Start(new ProcessStartInfo
                {
                    FileName = "sc.exe",
                    Arguments = "stop wuauserv",
                    UseShellExecute = false,
                    CreateNoWindow = true,
                    RedirectStandardOutput = true,
                    RedirectStandardError = true
                });
                if (p == null)
                    return false;
                p.WaitForExit(15000);

                // 等待服务真正进入停止状态（最多约 30 秒）；超时则降级，不强行恢复
                for (int i = 0; i < 30; i++)
                {
                    if (!IsServiceRunning("wuauserv"))
                        return true;
                    Thread.Sleep(1000);
                }
                LogService.Instance.Warning("[CleanService] wuauserv 停止超时，降级为跳过占用文件");
                return false;
            }
            catch (Exception ex)
            {
                LogService.Instance.Warning("[CleanService] 停止 wuauserv 失败，降级为跳过占用文件", ex);
                return false;
            }
        }

        /// <summary>尝试恢复 wuauserv 服务。失败不抛异常（服务为按需启动，系统会在需要时拉起）。</summary>
        private static void TryStartWindowsUpdateService()
        {
            try
            {
                using var p = Process.Start(new ProcessStartInfo
                {
                    FileName = "sc.exe",
                    Arguments = "start wuauserv",
                    UseShellExecute = false,
                    CreateNoWindow = true,
                    RedirectStandardOutput = true,
                    RedirectStandardError = true
                });
                p?.WaitForExit(15000);
            }
            catch (Exception ex)
            {
                LogService.Instance.Warning("[CleanService] 恢复 wuauserv 失败", ex);
            }
        }

        /// <summary>
        /// Chromium 系浏览器各 Profile 下的常见缓存子目录（相对于 Profile 文件夹）。
        /// </summary>
        private static readonly string[] ChromiumProfileCacheSubDirs =
        {
            Path.Combine("Cache", "Cache_Data"), // 新版 Chromium 网络缓存位置
            "Cache",                             // 旧版/兼容
            "Code Cache",
            "GPUCache",
            "ShaderCache"
        };

        /// <summary>
        /// 枚举 Chromium 浏览器 User Data 根目录下的所有配置文件文件夹
        /// （Default 及 Profile 1、Profile 2……），返回各 Profile 的缓存路径。
        /// 未知文件夹一律跳过；无权限则返回空列表。
        /// </summary>
        private List<string> GetChromiumCachePaths(string userDataRoot)
        {
            var paths = new List<string>();
            if (string.IsNullOrEmpty(userDataRoot) || !Directory.Exists(userDataRoot))
                return paths;

            string[] profileDirs;
            try { profileDirs = Directory.GetDirectories(userDataRoot); }
            catch { return paths; }

            foreach (var profileDir in profileDirs)
            {
                string name;
                try { name = Path.GetFileName(profileDir); }
                catch { continue; }

                var isDefault = string.Equals(name, "Default", StringComparison.OrdinalIgnoreCase);
                var isProfile = name.StartsWith("Profile ", StringComparison.OrdinalIgnoreCase);
                if (!isDefault && !isProfile)
                    continue;

                foreach (var sub in ChromiumProfileCacheSubDirs)
                {
                    var cachePath = Path.Combine(profileDir, sub);
                    if (Directory.Exists(cachePath))
                        paths.Add(cachePath);
                }
            }

            return paths;
        }

        private List<string> GetChromeCachePaths()
        {
            var userProfile = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
            return GetChromiumCachePaths(Path.Combine(userProfile, "AppData", "Local", "Google", "Chrome", "User Data"));
        }

        private List<string> GetEdgeCachePaths()
        {
            var userProfile = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
            return GetChromiumCachePaths(Path.Combine(userProfile, "AppData", "Local", "Microsoft", "Edge", "User Data"));
        }

        public (long Size, int FileCount, int DirCount) CleanPaths(List<string> paths)
        {
            long totalSize = 0;
            int fileCount = 0;
            int dirCount = 0;

            // Windows 更新下载缓存：先尝试停止 wuauserv，避免清理时文件被占用；
            // 停止失败则优雅降级为跳过占用文件。清理完成后恢复服务。
            var needStopService = ContainsWindowsUpdateDownloadPath(paths);
            var serviceStopped = false;
            try
            {
                if (needStopService)
                    serviceStopped = TryStopWindowsUpdateService();

                foreach (var path in paths)
                {
                    try
                    {
                        if (File.Exists(path))
                        {
                            try
                            {
                                var fileInfo = new FileInfo(path);
                                totalSize += fileInfo.Length;
                                File.Delete(path);
                                fileCount++;
                            }
                            catch (Exception ex)
                            {
                                LogService.Instance.Warning("[CleanService.CleanPaths] 删除失败", ex);
                            }
                        }
                        else if (Directory.Exists(path))
                        {
                            var result = DeleteDirectoryContents(path);
                            totalSize += result.Size;
                            fileCount += result.Count;
                            dirCount += result.DirCount;
                        }
                    }
                    catch (Exception ex)
                    {
                        LogService.Instance.Warning("[CleanService.CleanPaths] 删除失败", ex);
                    }
                }
            }
            finally
            {
                if (serviceStopped)
                    TryStartWindowsUpdateService();
            }

            return (totalSize, fileCount, dirCount);
        }

        /// <summary>
        /// 删除目录内容（保留传入的目录本身）。
        /// 栈式逐目录遍历：单个文件被占用/无权限/已不存在仅静默计数，不中止整体；
        /// 跳过软链接；最后自底向上删除已清空的子目录。
        /// 被锁定的文件与目录分别计数（skippedLockedFilesCount / skippedLockedDirsCount），
        /// 结束时汇总记一条日志，禁止逐条刷屏。
        /// </summary>
        private (long Size, int Count, int DirCount, int SkippedFiles, int SkippedDirs) DeleteDirectoryContents(string path)
        {
            long size = 0;
            int fileCount = 0;
            int dirCount = 0;
            int skippedLockedFilesCount = 0;
            int skippedLockedDirsCount = 0;

            try
            {
                var root = new DirectoryInfo(path);
                if (!root.Exists)
                    return (0, 0, 0, 0, 0);

                var stack = new Stack<DirectoryInfo>();
                var visited = new List<DirectoryInfo>(); // 访问顺序，用于自底向上删空目录
                stack.Push(root);

                while (stack.Count > 0)
                {
                    var dir = stack.Pop();
                    visited.Add(dir);

                    FileInfo[] files;
                    try { files = dir.GetFiles(); }
                    catch { skippedLockedDirsCount++; continue; } // 目录不可枚举：跳过整个目录

                    foreach (var file in files)
                    {
                        try
                        {
                            if (!file.Exists)
                                continue; // 并发/外部已删除：视为已清理，不计数
                            var fileLen = file.Length;
                            file.Delete();
                            size += fileLen; // 仅统计删除成功的
                            fileCount++;
                        }
                        catch (UnauthorizedAccessException) { skippedLockedFilesCount++; }
                        catch (FileNotFoundException) { skippedLockedFilesCount++; }      // 删除竞态
                        catch (DirectoryNotFoundException) { skippedLockedFilesCount++; } // 父目录已被删
                        catch (IOException) { skippedLockedFilesCount++; }                // 被占用
                        catch { skippedLockedFilesCount++; }
                    }

                    DirectoryInfo[] subDirs;
                    try { subDirs = dir.GetDirectories(); }
                    catch { skippedLockedDirsCount++; continue; }

                    foreach (var sub in subDirs)
                    {
                        try
                        {
                            // 跳过软链接/junction：不跟随，只处理本体
                            if (sub.Attributes.HasFlag(FileAttributes.ReparsePoint))
                                continue;
                        }
                        catch { skippedLockedDirsCount++; continue; }
                        stack.Push(sub);
                    }
                }

                // 自底向上删除已清空的子目录；传入的根目录本身保留
                for (int i = visited.Count - 1; i >= 0; i--)
                {
                    var dir = visited[i];
                    if (dir.FullName.Equals(root.FullName, StringComparison.OrdinalIgnoreCase))
                        continue;
                    try
                    {
                        if (!dir.Exists)
                            continue; // 已被删：静默跳过，避免 DirectoryNotFoundException
                        if (dir.GetFileSystemInfos().Length == 0)
                        {
                            dir.Delete();
                            dirCount++;
                        }
                        // 非空（有被跳过的文件）：保留，不强删
                    }
                    catch (UnauthorizedAccessException) { skippedLockedDirsCount++; }
                    catch (DirectoryNotFoundException) { skippedLockedDirsCount++; }
                    catch (IOException) { skippedLockedDirsCount++; }
                    catch { skippedLockedDirsCount++; }
                }
            }
            catch (Exception ex)
            {
                LogService.Instance.Warning("[CleanService.DeleteDirectoryContents] 清理异常", ex);
            }

            if (skippedLockedFilesCount > 0 || skippedLockedDirsCount > 0)
                LogService.Instance.Warning($"[CleanService.DeleteDirectoryContents] 跳过 {skippedLockedFilesCount} 个被系统占用的文件、{skippedLockedDirsCount} 个文件夹：{path}");

            return (size, fileCount, dirCount, skippedLockedFilesCount, skippedLockedDirsCount);
        }

        public string FormatSize(long bytes)
        {
            string[] sizes = { "B", "KB", "MB", "GB", "TB" };
            int order = 0;
            double size = bytes;

            while (size >= 1024 && order < sizes.Length - 1)
            {
                order++;
                size = size / 1024;
            }

            return $"{size:0.##} {sizes[order]}";
        }

        /// <summary>
        /// 只读统计目录大小（不删除）。用于清理前预估可清理空间。
        /// 逐目录捕获异常，跳过无权限/被占用的路径。
        /// </summary>
        public static long GetDirectorySize(string path)
        {
            long size = 0;
            try
            {
                if (File.Exists(path))
                {
                    try { return new FileInfo(path).Length; } catch { return 0; }
                }
                if (!Directory.Exists(path)) return 0;

                var stack = new Stack<string>();
                stack.Push(path);
                while (stack.Count > 0)
                {
                    var dir = stack.Pop();
                    try
                    {
                        foreach (var file in Directory.EnumerateFiles(dir))
                        {
                            try { size += new FileInfo(file).Length; } catch { }
                        }
                        foreach (var sub in Directory.EnumerateDirectories(dir))
                            stack.Push(sub);
                    }
                    catch { }
                }
            }
            catch { }
            return size;
        }
    }
}
