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
                            Description = "Intel、AMD以及Nvidia驱动在安装时留下的解压目录",
                            Level = 3,
                            Paths = new List<string>
                            {
                                Path.Combine(Path.GetPathRoot(Environment.SystemDirectory) ?? "C:\\", "AMD"),
                                Path.Combine(Path.GetPathRoot(Environment.SystemDirectory) ?? "C:\\", "Intel"),
                                Path.Combine(Path.GetPathRoot(Environment.SystemDirectory) ?? "C:\\", "NVIDIA")
                            }
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
                            Name = "Windows下载缓存",
                            Description = "Windows在下载更新以及更新Metro应用时产生的临时安装包",
                            Level = 2,
                            Paths = new List<string>
                            {
                                Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Windows), "SoftwareDistribution", "Download")
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
                            Name = "Windows更新清理",
                            Description = "清理Windows Update组件缓存",
                            Warning = "清理后可能无法卸载某些更新",
                            Level = 2,
                            Paths = new List<string>
                            {
                                Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Windows), "SoftwareDistribution", "Download")
                            }
                        },
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

        private List<string> GetChromeCachePaths()
        {
            var paths = new List<string>();
            var userProfile = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
            var localAppData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);

            paths.Add(Path.Combine(userProfile, "AppData", "Local", "Google", "Chrome", "User Data", "Default", "Cache"));
            paths.Add(Path.Combine(userProfile, "AppData", "Local", "Google", "Chrome", "User Data", "Default", "Code Cache"));
            paths.Add(Path.Combine(userProfile, "AppData", "Local", "Google", "Chrome", "User Data", "Default", "GPUCache"));
            paths.Add(Path.Combine(userProfile, "AppData", "Local", "Google", "Chrome", "User Data", "Default", "ShaderCache"));

            return paths;
        }

        private List<string> GetEdgeCachePaths()
        {
            var paths = new List<string>();
            var userProfile = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);

            paths.Add(Path.Combine(userProfile, "AppData", "Local", "Microsoft", "Edge", "User Data", "Default", "Cache"));
            paths.Add(Path.Combine(userProfile, "AppData", "Local", "Microsoft", "Edge", "User Data", "Default", "Code Cache"));
            paths.Add(Path.Combine(userProfile, "AppData", "Local", "Microsoft", "Edge", "User Data", "Default", "GPUCache"));
            paths.Add(Path.Combine(userProfile, "AppData", "Local", "Microsoft", "Edge", "User Data", "Default", "ShaderCache"));

            return paths;
        }

        public (long Size, int FileCount, int DirCount) CleanPaths(List<string> paths)
        {
            long totalSize = 0;
            int fileCount = 0;
            int dirCount = 0;

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

            return (totalSize, fileCount, dirCount);
        }

        private (long Size, int Count, int DirCount) DeleteDirectoryContents(string path)
        {
            long size = 0;
            int fileCount = 0;
            int dirCount = 0;

            try
            {
                var dirInfo = new DirectoryInfo(path);

                Parallel.ForEach(dirInfo.EnumerateFiles("*", SearchOption.AllDirectories), file =>
                {
                    try
                    {
                        Interlocked.Add(ref size, file.Length);
                        file.Delete();
                        Interlocked.Increment(ref fileCount);
                    }
                    catch (Exception ex)
                    {
                        LogService.Instance.Warning("[CleanService.DeleteDirectoryContents] 删除失败", ex);
                    }
                });

                foreach (var dir in dirInfo.EnumerateDirectories("*", SearchOption.AllDirectories))
                {
                    try
                    {
                        dir.Delete(true);
                        dirCount++;
                    }
                    catch (Exception ex)
                    {
                        LogService.Instance.Warning("[CleanService.DeleteDirectoryContents] 删除失败", ex);
                    }
                }
            }
            catch (Exception ex)
            {
                LogService.Instance.Warning("[CleanService.DeleteDirectoryContents] 删除失败", ex);
            }

            return (size, fileCount, dirCount);
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
