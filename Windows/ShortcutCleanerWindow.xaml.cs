using System;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.IO;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using SystemTool.Services;

namespace SystemTool.Windows
{
    public class ShortcutItem : INotifyPropertyChanged
    {
        private bool _isSelected;
        public string? Name { get; set; }
        public string? FullPath { get; set; }
        public string? TargetPath { get; set; }
        public ImageSource? Icon { get; set; }

        public bool IsSelected
        {
            get => _isSelected;
            set
            {
                _isSelected = value;
                OnPropertyChanged(nameof(IsSelected));
            }
        }

        public event PropertyChangedEventHandler? PropertyChanged;
        protected void OnPropertyChanged(string name) =>
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
    }

    public partial class ShortcutCleanerWindow : Window
    {
        public ObservableCollection<ShortcutItem> Shortcuts { get; set; } = new ObservableCollection<ShortcutItem>();

        [DllImport("shell32.dll", CharSet = CharSet.Auto)]
        private static extern IntPtr SHGetFileInfo(string pszPath, uint dwFileAttributes, ref SHFILEINFO psfi, uint cbFileInfo, uint uFlags);

        [DllImport("user32.dll")]
        private static extern int DestroyIcon(IntPtr hIcon);

        [DllImport("shell32.dll", CharSet = CharSet.Auto)]
        private static extern IntPtr ExtractAssociatedIcon(IntPtr hInst, string lpIconPath, ref ushort lpiIcon);

        [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Auto)]
        private struct SHFILEINFO
        {
            public IntPtr hIcon;
            public int iIcon;
            public uint dwAttributes;
            [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 260)]
            public string szDisplayName;
            [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 80)]
            public string szTypeName;
        }

        private const uint SHGFI_ICON = 0x100;
        private const uint SHGFI_LARGEICON = 0x0;

        public ShortcutCleanerWindow()
        {
            InitializeComponent();
            ShortcutsList.ItemsSource = Shortcuts;
            // 每条目 SHGetFileInfo 取图标耗时大，后台加载避免阻塞窗口显示
            StatusText.Text = "正在加载快捷方式...";
            _ = System.Threading.Tasks.Task.Run(() =>
            {
                var items = ScanShortcutEntries();
                Dispatcher.Invoke(() =>
                {
                    foreach (var item in items) Shortcuts.Add(item);
                    UpdateStatus();
                });
            });
        }

        private void LoadShortcuts()
        {
            StatusText.Text = "正在加载快捷方式...";
            Shortcuts.Clear();
            _ = System.Threading.Tasks.Task.Run(() =>
            {
                var items = ScanShortcutEntries();
                Dispatcher.Invoke(() =>
                {
                    foreach (var item in items) Shortcuts.Add(item);
                    UpdateStatus();
                });
            });
        }

        private System.Collections.Generic.List<ShortcutItem> ScanShortcutEntries()
        {
            var result = new System.Collections.Generic.List<ShortcutItem>();
            var systemItems = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
            {
                "本地磁盘", "Local Disk", "硬盘", "CD 驱动器", "DVD 驱动器", "CD-ROM", "DVD-ROM",
                "可移动磁盘", "Removable Disk", "USB 驱动器", "USB Drive",
                "文档", "Documents", "图片", "Pictures", "视频", "Videos",
                "下载", "Downloads", "桌面", "Desktop", "3D 对象", "3D Objects",
                "网络", "Network", "控制面板", "Control Panel", "回收站", "Recycle Bin",
                "Music", "Videos", "Pictures", "Documents", "Downloads", "Desktop"
            };

            var exactSystemItems = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
            {
                "音乐", "Music", "视频", "Videos", "图片", "Pictures", "文档", "Documents"
            };

            try
            {
                using (var key = Microsoft.Win32.Registry.LocalMachine.OpenSubKey(@"SOFTWARE\Microsoft\Windows\CurrentVersion\Explorer\MyComputer\NameSpace"))
                {
                    if (key != null)
                    {
                        foreach (var subKeyName in key.GetSubKeyNames())
                        {
                            using (var subKey = key.OpenSubKey(subKeyName))
                            {
                                if (subKey != null)
                                {
                                    var name = subKey.GetValue(null) as string;
                                    if (!string.IsNullOrEmpty(name) && !IsSystemItem(name, systemItems, exactSystemItems))
                                    {
                                        result.Add(new ShortcutItem
                                        {
                                            Name = name,
                                            FullPath = $"Registry::{subKeyName}",
                                            TargetPath = $"CLSID: {subKeyName}",
                                            IsSelected = false,
                                            Icon = GetSystemIcon(subKeyName)
                                        });
                                    }
                                }
                            }
                        }
                    }
                }

                using (var key = Microsoft.Win32.Registry.CurrentUser.OpenSubKey(@"SOFTWARE\Microsoft\Windows\CurrentVersion\Explorer\MyComputer\NameSpace"))
                {
                    if (key != null)
                    {
                        foreach (var subKeyName in key.GetSubKeyNames())
                        {
                            using (var subKey = key.OpenSubKey(subKeyName))
                            {
                                if (subKey != null)
                                {
                                    var name = subKey.GetValue(null) as string;
                                    if (!string.IsNullOrEmpty(name) && !IsSystemItem(name, systemItems, exactSystemItems))
                                    {
                                        result.Add(new ShortcutItem
                                        {
                                            Name = name,
                                            FullPath = $"Registry::{subKeyName}",
                                            TargetPath = $"CLSID: {subKeyName}",
                                            IsSelected = false,
                                            Icon = GetSystemIcon(subKeyName)
                                        });
                                    }
                                }
                            }
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                LogService.Instance.Warning("[ShortcutCleanerWindow.ScanShortcutEntries] 执行失败", ex);
            }
            return result;
        }

        private bool IsSystemItem(string name, HashSet<string> systemItems, HashSet<string> exactSystemItems)
        {
            if (exactSystemItems.Contains(name))
                return true;

            foreach (var item in systemItems)
            {
                if (name.Contains(item, StringComparison.OrdinalIgnoreCase))
                    return true;
            }
            return false;
        }

        private ImageSource? GetSystemIcon(string clsid)
        {
            try
            {
                var shfi = new SHFILEINFO();
                var path = $"::{clsid}";
                var hImg = SHGetFileInfo(path, 0, ref shfi, (uint)Marshal.SizeOf(shfi), SHGFI_ICON | SHGFI_LARGEICON);

                if (shfi.hIcon != IntPtr.Zero)
                {
                    var imageSource = Imaging.CreateBitmapSourceFromHIcon(shfi.hIcon, Int32Rect.Empty, BitmapSizeOptions.FromEmptyOptions());
                    DestroyIcon(shfi.hIcon);
                    return imageSource;
                }
            }
            catch (Exception ex)
            {
                LogService.Instance.Warning("[ShortcutCleanerWindow.GetSystemIcon] 执行失败", ex);
            }

            return null;
        }

        private void UpdateStatus()
        {
            StatusText.Text = $"共发现 {Shortcuts.Count} 个快捷方式";
        }

        private void SelectAll_Click(object sender, RoutedEventArgs e)
        {
            foreach (var item in Shortcuts)
                item.IsSelected = true;
        }

        private void DeselectAll_Click(object sender, RoutedEventArgs e)
        {
            foreach (var item in Shortcuts)
                item.IsSelected = false;
        }

        private void DeleteSelected_Click(object sender, RoutedEventArgs e)
        {
            var selectedItems = new System.Collections.Generic.List<ShortcutItem>();
            foreach (var item in Shortcuts)
            {
                if (item.IsSelected)
                    selectedItems.Add(item);
            }

            if (selectedItems.Count == 0)
            {
                MessageBox.Show("请先选择要删除的快捷方式", "提示", MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }

            var result = MessageBox.Show($"确定要删除选中的 {selectedItems.Count} 个快捷方式吗？", "确认删除", 
                MessageBoxButton.YesNo, MessageBoxImage.Question);

            if (result != MessageBoxResult.Yes)
                return;

            int deleted = 0;
            int failed = 0;

            foreach (var item in selectedItems)
            {
                try
                {
                    if (item.FullPath != null && item.FullPath.StartsWith("Registry::"))
                    {
                        var clsid = item.FullPath.Substring("Registry::".Length);
                        if (DeleteRegistryEntry(clsid))
                        {
                            deleted++;
                            Services.LogService.Instance.Info($"已删除此电脑快捷方式: {item.Name}");
                        }
                        else
                        {
                            failed++;
                            Services.LogService.Instance.Warning($"删除快捷方式未成功（注册表项不存在或无权限）: {item.Name}");
                        }
                    }
                }
                catch (Exception ex)
                {
                    failed++;
                    Services.LogService.Instance.Error($"删除快捷方式失败: {item.Name} - {ex.Message}");
                }
            }

            LoadShortcuts();
            UpdateStatus();

            if (failed > 0)
                MessageBox.Show($"成功删除 {deleted} 个快捷方式\n失败 {failed} 个", "删除完成", MessageBoxButton.OK, MessageBoxImage.Warning);
            else
                MessageBox.Show($"成功删除 {deleted} 个快捷方式", "删除完成", MessageBoxButton.OK, MessageBoxImage.Information);
        }

        /// <returns>实际删除了至少一个注册表项返回 true，否则 false</returns>
        private bool DeleteRegistryEntry(string clsid)
        {
            var paths = new[]
            {
                $@"SOFTWARE\Microsoft\Windows\CurrentVersion\Explorer\MyComputer\NameSpace\{clsid}",
            };

            bool deleted = false;
            foreach (var path in paths)
            {
                try
                {
                    // 项存在才删；不存在（已删或未注册）不算删除成功
                    using (var existing = Microsoft.Win32.Registry.LocalMachine.OpenSubKey(path))
                    {
                        if (existing != null)
                        {
                            Microsoft.Win32.Registry.LocalMachine.DeleteSubKey(path, false);
                            deleted = true;
                        }
                    }
                }
                catch (Exception ex)
                {
                    LogService.Instance.Error("[ShortcutCleanerWindow.DeleteRegistryEntry] 注册表删除失败", ex);
                }

                try
                {
                    using (var existing = Microsoft.Win32.Registry.CurrentUser.OpenSubKey(path))
                    {
                        if (existing != null)
                        {
                            Microsoft.Win32.Registry.CurrentUser.DeleteSubKey(path, false);
                            deleted = true;
                        }
                    }
                }
                catch (Exception ex)
                {
                    LogService.Instance.Error("[ShortcutCleanerWindow.DeleteRegistryEntry] 注册表删除失败", ex);
                }
            }
            return deleted;
        }
    }
}
