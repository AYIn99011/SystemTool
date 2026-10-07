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
            LoadShortcuts();
            ShortcutsList.ItemsSource = Shortcuts;
            UpdateStatus();
        }

        private void LoadShortcuts()
        {
            Shortcuts.Clear();
            ScanRegistryNamespace();
        }

        private void ScanRegistryNamespace()
        {
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
                                        Shortcuts.Add(new ShortcutItem
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
                                        Shortcuts.Add(new ShortcutItem
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
                LogService.Instance.Warning("[ShortcutCleanerWindow.ScanRegistryNamespace] 执行失败", ex);
            }
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

        private void TitleBar_MouseLeftButtonDown(object sender, System.Windows.Input.MouseButtonEventArgs e)
        {
            if (e.LeftButton == System.Windows.Input.MouseButtonState.Pressed)
                DragMove();
        }

        private void CloseButton_Click(object sender, RoutedEventArgs e)
        {
            Close();
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
                        DeleteRegistryEntry(clsid);
                        deleted++;
                        Services.LogService.Instance.Info($"已删除此电脑快捷方式: {item.Name}");
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

        private void DeleteRegistryEntry(string clsid)
        {
            var paths = new[]
            {
                $@"SOFTWARE\Microsoft\Windows\CurrentVersion\Explorer\MyComputer\NameSpace\{clsid}",
            };

            foreach (var path in paths)
            {
                try
                {
                    Microsoft.Win32.Registry.LocalMachine.DeleteSubKey(path, false);
                }
                catch (Exception ex)
                {
                    LogService.Instance.Error("[ShortcutCleanerWindow.DeleteRegistryEntry] 注册表删除失败", ex);
                }

                try
                {
                    Microsoft.Win32.Registry.CurrentUser.DeleteSubKey(path, false);
                }
                catch (Exception ex)
                {
                    LogService.Instance.Error("[ShortcutCleanerWindow.DeleteRegistryEntry] 注册表删除失败", ex);
                }
            }
        }
    }
}
