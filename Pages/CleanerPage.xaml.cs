using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using SystemTool.Models;
using SystemTool.Services;
using SystemTool.Windows;
using Microsoft.Win32;

namespace SystemTool.Pages
{
    public partial class CleanerPage : Page
    {
        private bool _isOperating;
        private readonly CleanService _cleanService = new();

        public CleanerPage()
        {
            InitializeComponent();
            InitCategories();
            LoadSelection();
            BuildCategoryCheckboxes();
            BuildAdvancedCheckboxes();
            Loaded += CleanerPage_Loaded;
        }

        #region 一键清理分类定义与选择

        /// <summary>一键清理/预估的一个分类：显示名、只读预估路径、清理实现。</summary>
        private sealed class CleanCategory
        {
            public string Key { get; }
            public string DisplayName { get; }
            public Func<List<string>> GetEstimatePaths { get; }
            public Func<Task<long>> CleanCoreAsync { get; }
            /// <summary>是否可预估大小（应用商店缓存走 WSReset，无法预估）。</summary>
            public bool HasEstimate { get; }

            public CleanCategory(string key, string displayName,
                Func<List<string>> getEstimatePaths, Func<Task<long>> cleanCoreAsync,
                bool hasEstimate = true)
            {
                Key = key;
                DisplayName = displayName;
                GetEstimatePaths = getEstimatePaths;
                CleanCoreAsync = cleanCoreAsync;
                HasEstimate = hasEstimate;
            }
        }

        private readonly List<CleanCategory> _categories = new();
        private readonly HashSet<string> _selectedKeys = new(StringComparer.OrdinalIgnoreCase);
        /// <summary>上次扫描的各分类大小（Key=分类Key）。</summary>
        private Dictionary<string, long> _lastEstimates = new(StringComparer.OrdinalIgnoreCase);
        private static readonly string SelectionFile = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "SystemTool", "clean_selection.txt");

        private void InitCategories()
        {
            _categories.Add(new CleanCategory("SystemCache", "系统缓存", GetSystemCacheEstimatePaths, CleanSystemCacheCoreAsync));
            _categories.Add(new CleanCategory("SystemLogs", "系统日志", GetSystemLogsEstimatePaths, CleanSystemLogsCoreAsync));
            _categories.Add(new CleanCategory("BrowserCache", "浏览器缓存", GetBrowserCacheEstimatePaths, CleanBrowserCacheCoreAsync));
            _categories.Add(new CleanCategory("StoreCache", "应用商店缓存", () => new List<string>(), CleanStoreCacheCoreAsync, hasEstimate: false));
            _categories.Add(new CleanCategory("QQ", "QQ缓存", GetQQCachePaths, CleanQQCacheCoreAsync));
            _categories.Add(new CleanCategory("WeChat", "微信缓存", GetWeChatCachePaths, CleanWeChatCacheCoreAsync));
            _categories.Add(new CleanCategory("QQMusic", "QQ音乐缓存", GetQQMusicCachePaths, CleanQQMusicCacheCoreAsync));
            _categories.Add(new CleanCategory("CloudMusic", "网易云音乐缓存", GetCloudMusicCachePaths, CleanCloudMusicCacheCoreAsync));
            _categories.Add(new CleanCategory("KuGou", "酷狗音乐缓存", GetKuGouCachePaths, CleanKuGouCacheCoreAsync));
            _categories.Add(new CleanCategory("Douyin", "抖音缓存", GetDouyinCachePaths, CleanDouyinCacheCoreAsync));
        }

        private void LoadSelection()
        {
            _selectedKeys.Clear();
            try
            {
                if (File.Exists(SelectionFile))
                {
                    foreach (var k in File.ReadAllText(SelectionFile)
                        .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
                        _selectedKeys.Add(k);
                }
            }
            catch (Exception ex)
            {
                LogService.Instance.Warning("[CleanerPage.LoadSelection] 读取清理选择失败", ex);
            }
            // 去掉未知 key；为空则默认全选
            _selectedKeys.IntersectWith(_categories.Select(c => c.Key));
            if (_selectedKeys.Count == 0)
                foreach (var c in _categories) _selectedKeys.Add(c.Key);
        }

        private void SaveSelection()
        {
            try
            {
                Directory.CreateDirectory(Path.GetDirectoryName(SelectionFile)!);
                File.WriteAllText(SelectionFile, string.Join(",", _selectedKeys));
            }
            catch (Exception ex)
            {
                LogService.Instance.Warning("[CleanerPage.SaveSelection] 保存清理选择失败", ex);
            }
        }

        private void BuildCategoryCheckboxes()
        {
            CategoryCheckPanel.Children.Clear();
            foreach (var cat in _categories)
            {
                var cb = new CheckBox
                {
                    Content = cat.DisplayName,
                    IsChecked = _selectedKeys.Contains(cat.Key),
                    Margin = new Thickness(0, 0, 24, 14),
                    FontSize = 13,
                    Tag = cat.Key,
                    Cursor = Cursors.Hand,
                };
                cb.Checked += CategoryCheckBox_Toggled;
                cb.Unchecked += CategoryCheckBox_Toggled;
                CategoryCheckPanel.Children.Add(cb);
            }
            UpdateCustomizeToggleText();
        }

        private bool _suppressCheckToggle;

        private void CategoryCheckBox_Toggled(object sender, RoutedEventArgs e)
        {
            if (_suppressCheckToggle) return;
            if (sender is not CheckBox cb || cb.Tag is not string key) return;
            if (cb.IsChecked == true) _selectedKeys.Add(key);
            else _selectedKeys.Remove(key);
            SaveSelection();
            UpdateCustomizeToggleText();
            // 增量更新：取消勾选直接用缓存重算，只有新勾选的缺失分类才补扫
            _ = RefreshEstimateOnSelectionChangedAsync();
        }

        private void SelectAllButton_Click(object sender, RoutedEventArgs e)
            => SetAllCategoriesSelected(true);

        private void ClearSelectionButton_Click(object sender, RoutedEventArgs e)
            => SetAllCategoriesSelected(false);

        private void SetAllCategoriesSelected(bool selected)
        {
            _suppressCheckToggle = true;
            try
            {
                _selectedKeys.Clear();
                if (selected)
                    foreach (var c in _categories) _selectedKeys.Add(c.Key);
                foreach (CheckBox cb in CategoryCheckPanel.Children)
                    cb.IsChecked = selected;
            }
            finally
            {
                _suppressCheckToggle = false;
            }
            SaveSelection();
            UpdateCustomizeToggleText();
            _ = RefreshEstimateOnSelectionChangedAsync();
        }

        private void UpdateCustomizeToggleText()
        {
            int n = _categories.Count(c => _selectedKeys.Contains(c.Key));
            bool expanded = CustomizePanel.Visibility == Visibility.Visible;
            CustomizeToggle.Content = expanded ? "收起 ▴" : "展开 ▾";
            SelectedSummaryText.Text = $"已选择 {n} / {_categories.Count} 项";
        }

        private void CustomizeToggle_Click(object sender, RoutedEventArgs e)
        {
            CustomizePanel.Visibility = CustomizePanel.Visibility == Visibility.Visible
                ? Visibility.Collapsed : Visibility.Visible;
            UpdateCustomizeToggleText();
        }

        #endregion

        #region 顶部汇总条：一键扫描 / 预估 / 上次清理

        private bool _estimateReady;
        private long _lastEstimate;
        private static readonly string LastCleanFile = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "SystemTool", "last_clean.txt");

        private void CleanerPage_Loaded(object sender, RoutedEventArgs e)
        {
            LoadLastCleanText();
            // 不再自动扫描：由用户点击「一键扫描」手动触发
        }

        /// <summary>更新圆环进度（0~1）。圆心(60,60)，半径54，起点在12点钟方向。</summary>
        private void UpdateRing(double fraction)
        {
            if (RingArc == null) return;
            if (fraction <= 0.001)
            {
                RingArc.Visibility = Visibility.Collapsed;
                return;
            }
            RingArc.Visibility = Visibility.Visible;
            const double cx = 60, cy = 60, r = 54;
            string data;
            if (fraction >= 0.999)
            {
                data = $"M {cx:F1},{cy - r:F1} A {r},{r} 0 1,1 {cx - 0.01:F1},{cy - r:F1} A {r},{r} 0 1,1 {cx:F1},{cy - r:F1}";
            }
            else
            {
                double rad = (fraction * 360.0 - 90.0) * Math.PI / 180.0;
                double ex = cx + r * Math.Cos(rad), ey = cy + r * Math.Sin(rad);
                int large = fraction > 0.5 ? 1 : 0;
                data = $"M {cx:F1},{cy - r:F1} A {r},{r} 0 {large},1 {ex:F1},{ey:F1}";
            }
            RingArc.Data = Geometry.Parse(data);
        }

        /// <summary>只读估算单个分类可清理大小。</summary>
        private long EstimateCategorySize(CleanCategory cat)
        {
            long s = 0;
            try
            {
                foreach (var p in cat.GetEstimatePaths())
                    s += CleanService.GetDirectorySize(p);
            }
            catch (Exception ex)
            {
                LogService.Instance.Warning($"[CleanerPage.EstimateCategorySize] 估算{cat.DisplayName}失败", ex);
            }
            return s;
        }

        /// <summary>只读估算已选分类可清理大小，返回各分类字节数，顺带推进圆环。</summary>
        private async Task<Dictionary<string, long>> EstimateAllAsync()
        {
            var selected = _categories.Where(c => _selectedKeys.Contains(c.Key)).ToList();
            var sizes = new Dictionary<string, long>(StringComparer.OrdinalIgnoreCase);
            for (int i = 0; i < selected.Count; i++)
            {
                var cat = selected[i];
                long sub = await Task.Run(() => EstimateCategorySize(cat));
                sizes[cat.Key] = sub;
                UpdateRing(selected.Count == 0 ? 0 : (double)(i + 1) / selected.Count * 0.95);
            }
            return sizes;
        }

        /// <summary>把上次扫描的各分类大小刷新到清理范围的复选框上。</summary>
        private void RefreshCategorySizes()
        {
            foreach (CheckBox cb in CategoryCheckPanel.Children)
            {
                if (cb.Tag is not string key) continue;
                var cat = _categories.FirstOrDefault(c => c.Key.Equals(key, StringComparison.OrdinalIgnoreCase));
                if (cat == null) continue;
                string label = cat.DisplayName;
                if (cat.HasEstimate && _lastEstimates.TryGetValue(key, out long size))
                    label += $"（{FormatSize(size)}）";
                cb.Content = label;
            }
        }

        private async Task RunEstimateAsync()
        {
            try
            {
                EstimateText.Text = "正在扫描…";
                UpdateRing(0.05);
                var fresh = await EstimateAllAsync();
                // 合并进缓存（保留未选中分类的旧扫描值，勾选变化时可直接复用）
                foreach (var kv in fresh)
                    _lastEstimates[kv.Key] = kv.Value;
                UpdateEstimateDisplay();
            }
            catch (Exception ex)
            {
                LogService.Instance.Warning("[CleanerPage.RunEstimateAsync] 扫描失败", ex);
                EstimateText.Text = "--";
            }
        }

        /// <summary>
        /// 勾选变化时的增量更新：已扫描过的分类直接用缓存重算，
        /// 只有新勾选且缓存缺失的分类才单独扫描，不再全量重扫。
        /// </summary>
        private async Task RefreshEstimateOnSelectionChangedAsync()
        {
            try
            {
                var missing = _categories
                    .Where(c => _selectedKeys.Contains(c.Key) && !_lastEstimates.ContainsKey(c.Key))
                    .ToList();
                if (missing.Count > 0)
                {
                    EstimateText.Text = "正在扫描…";
                    foreach (var cat in missing)
                    {
                        long sub = await Task.Run(() => EstimateCategorySize(cat));
                        _lastEstimates[cat.Key] = sub;
                    }
                }
                UpdateEstimateDisplay();
            }
            catch (Exception ex)
            {
                LogService.Instance.Warning("[CleanerPage.RefreshEstimateOnSelectionChangedAsync] 更新预估失败", ex);
            }
        }

        /// <summary>用缓存重算已选分类总额并刷新界面（不扫描）。</summary>
        private void UpdateEstimateDisplay()
        {
            long est = 0;
            foreach (var c in _categories)
            {
                if (_selectedKeys.Contains(c.Key) && _lastEstimates.TryGetValue(c.Key, out long size))
                    est += size;
            }
            _lastEstimate = est;
            _estimateReady = _lastEstimates.Count > 0;
            EstimateText.Text = _cleanService.FormatSize(est);
            // 圆环以 20GB 为满刻度，仅作视觉示意
            UpdateRing(Math.Min(est / (20.0 * 1024 * 1024 * 1024), 1.0));
            OneClickButton.Content = _estimateReady ? $"一键清理（{FormatSize(est)}）" : "一键扫描";
            RefreshCategorySizes();
        }

        private async void OneClickButton_Click(object sender, RoutedEventArgs e)
        {
            if (_isOperating) return;
            _isOperating = true;
            OneClickButton.IsEnabled = false;
            try
            {
                if (!_estimateReady)
                {
                    OneClickButton.Content = "扫描中…";
                    await RunEstimateAsync();
                    return;
                }

                // 一键清理：按顺序执行已选分类
                var selected = _categories.Where(c => _selectedKeys.Contains(c.Key)).ToList();
                if (selected.Count == 0)
                {
                    LogService.Instance.Warning("一键清理：未选择任何清理项");
                    return;
                }
                OneClickButton.Content = "清理中…";
                LogService.Instance.Info("======== 一键清理开始 ========");
                long freed = 0;
                foreach (var cat in selected)
                    freed += await cat.CleanCoreAsync();
                LogService.Instance.Info("======== 一键清理结束 ========");
                LogService.Instance.Success($"一键清理完成，共释放空间: {FormatSize(freed)}");
                MarkCleaned();

                _estimateReady = false;
                OneClickButton.Content = "一键扫描";
                await RunEstimateAsync();
            }
            finally
            {
                _isOperating = false;
                OneClickButton.IsEnabled = true;
            }
        }

        private void MarkCleaned()
        {
            try
            {
                Directory.CreateDirectory(Path.GetDirectoryName(LastCleanFile)!);
                File.WriteAllText(LastCleanFile, DateTime.Now.ToString("o"));
            }
            catch (Exception ex)
            {
                LogService.Instance.Warning("[CleanerPage.MarkCleaned] 记录清理时间失败", ex);
            }
            LoadLastCleanText();
        }

        private void LoadLastCleanText()
        {
            try
            {
                if (LastCleanText == null) return;
                if (!File.Exists(LastCleanFile))
                {
                    LastCleanText.Text = "上次清理：从未清理";
                    return;
                }
                var t = DateTime.Parse(File.ReadAllText(LastCleanFile).Trim());
                LastCleanText.Text = "上次清理：" + FormatLastClean(t);
            }
            catch
            {
                LastCleanText.Text = "上次清理：--";
            }
        }

        private static string FormatLastClean(DateTime t)
        {
            var span = DateTime.Now - t;
            if (span.TotalMinutes < 5) return "刚刚";
            if (span.TotalHours < 1) return $"{(int)span.TotalMinutes}分钟前";
            if (span.TotalHours < 24) return $"{(int)span.TotalHours}小时前";
            if (span.TotalDays < 2) return "昨天";
            return $"{(int)span.TotalDays}天前";
        }

        #region 各分类预估路径（只读，供扫描用）

        private List<string> GetSystemCacheEstimatePaths()
        {
            var paths = new List<string>
            {
                Path.GetTempPath(),
                Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Windows), "Prefetch"),
                Environment.GetFolderPath(Environment.SpecialFolder.Recent),
                Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Windows), "Temp"),
            };
            // 缩略图缓存：清理时会删 thumbcache_*.db，预估也要算上（与清理逻辑一致）
            try
            {
                var explorerDir = Path.Combine(
                    Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                    "Microsoft", "Windows", "Explorer");
                if (Directory.Exists(explorerDir))
                    paths.AddRange(Directory.GetFiles(explorerDir, "thumbcache_*.db"));
            }
            catch (Exception ex)
            {
                LogService.Instance.Warning("[CleanerPage.GetSystemCacheEstimatePaths] 枚举缩略图缓存失败", ex);
            }
            return paths;
        }

        private List<string> GetSystemLogsEstimatePaths()
        {
            var win = Environment.GetFolderPath(Environment.SpecialFolder.Windows);
            return new()
            {
                Path.Combine(win, "Logs"),
                Path.Combine(win, "Logs", "CBS"),
                Path.Combine(win, "Logs", "DISM"),
            };
        }

        /// <summary>枚举 Chromium 内核浏览器的全部配置文件缓存目录（Default + Profile * + Guest）。</summary>
        private List<string> GetChromiumProfileCachePaths(string userDataDir)
        {
            var paths = new List<string>();
            try
            {
                if (!Directory.Exists(userDataDir)) return paths;
                foreach (var profileDir in Directory.GetDirectories(userDataDir))
                {
                    var name = Path.GetFileName(profileDir);
                    if (!name.Equals("Default", StringComparison.OrdinalIgnoreCase)
                        && !name.StartsWith("Profile ", StringComparison.OrdinalIgnoreCase)
                        && !name.Equals("Guest Profile", StringComparison.OrdinalIgnoreCase))
                        continue;
                    foreach (var sub in new[] { "Cache", "Code Cache", "GPUCache", "ShaderCache" })
                    {
                        var p = Path.Combine(profileDir, sub);
                        if (Directory.Exists(p)) paths.Add(p);
                    }
                }
            }
            catch (Exception ex)
            {
                LogService.Instance.Warning("[CleanerPage.GetChromiumProfileCachePaths] 枚举浏览器配置失败", ex);
            }
            return paths;
        }

        private List<string> GetBrowserCacheEstimatePaths()
        {
            string up = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
            var paths = new List<string>();
            paths.AddRange(GetChromiumProfileCachePaths(Path.Combine(up, @"AppData\Local\Google\Chrome\User Data")));
            paths.AddRange(GetChromiumProfileCachePaths(Path.Combine(up, @"AppData\Local\Microsoft\Edge\User Data")));
            string ff = Path.Combine(up, @"AppData\Local\Mozilla\Firefox\Profiles");
            if (Directory.Exists(ff)) paths.Add(ff);
            string c360 = Path.Combine(up, @"AppData\Local\360Chrome\Chrome\User Data\Default\Cache");
            if (Directory.Exists(c360)) paths.Add(c360);
            string qq = Path.Combine(up, @"AppData\Local\Tencent\QQBrowser\User Data\Default\Cache");
            if (Directory.Exists(qq)) paths.Add(qq);
            return paths;
        }

        #endregion

        #endregion

        #region 高级清理：内嵌展开区（与一键清理范围同样式）

        private List<CleanItem> _advancedItems = new();
        private bool _suppressAdvancedToggle;

        private void BuildAdvancedCheckboxes()
        {
            _advancedItems = _cleanService.GetCleanGroups().SelectMany(g => g.Items).ToList();
            AdvancedCheckPanel.Children.Clear();
            foreach (var item in _advancedItems)
            {
                var tip = item.Description;
                if (!string.IsNullOrEmpty(item.Warning))
                    tip += "\n注意：" + item.Warning;
                var cb = new CheckBox
                {
                    Content = item.Name,
                    ToolTip = tip,
                    IsChecked = item.IsSelected,
                    Margin = new Thickness(0, 0, 24, 14),
                    FontSize = 13,
                    Tag = item,
                    Cursor = Cursors.Hand,
                };
                cb.Checked += AdvancedCheckBox_Toggled;
                cb.Unchecked += AdvancedCheckBox_Toggled;
                AdvancedCheckPanel.Children.Add(cb);
            }
            UpdateAdvancedSummaryText();
        }

        private void AdvancedCheckBox_Toggled(object sender, RoutedEventArgs e)
        {
            if (_suppressAdvancedToggle) return;
            if (sender is not CheckBox cb || cb.Tag is not CleanItem item) return;
            item.IsSelected = cb.IsChecked == true;
            UpdateAdvancedSummaryText();
        }

        private void AdvancedSelectAllButton_Click(object sender, RoutedEventArgs e)
            => SetAllAdvancedSelected(true);

        private void AdvancedClearButton_Click(object sender, RoutedEventArgs e)
            => SetAllAdvancedSelected(false);

        private void SetAllAdvancedSelected(bool selected)
        {
            _suppressAdvancedToggle = true;
            try
            {
                foreach (var item in _advancedItems)
                    item.IsSelected = selected;
                foreach (CheckBox cb in AdvancedCheckPanel.Children)
                    cb.IsChecked = selected;
            }
            finally
            {
                _suppressAdvancedToggle = false;
            }
            UpdateAdvancedSummaryText();
        }

        private void UpdateAdvancedSummaryText()
        {
            int n = _advancedItems.Count(i => i.IsSelected);
            bool expanded = AdvancedPanel.Visibility == Visibility.Visible;
            AdvancedToggle.Content = expanded ? "收起 ▴" : "展开 ▾";
            AdvancedSummaryText.Text = $"已选择 {n} / {_advancedItems.Count} 项";
        }

        private void AdvancedToggle_Click(object sender, RoutedEventArgs e)
        {
            AdvancedPanel.Visibility = AdvancedPanel.Visibility == Visibility.Visible
                ? Visibility.Collapsed : Visibility.Visible;
            UpdateAdvancedSummaryText();
        }

        private async void AdvancedStartButton_Click(object sender, RoutedEventArgs e)
        {
            if (_isOperating)
            {
                LogService.Instance.Warning("正在执行其他操作，请稍候...");
                return;
            }

            var selectedItems = _advancedItems.Where(i => i.IsSelected).ToList();
            if (selectedItems.Count == 0)
            {
                MessageBox.Show("请至少选择一个清理项目", "提示", MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }

            await ExecuteAdvancedCleanAsync(selectedItems);
        }

        #endregion

        private async Task ExecuteAdvancedCleanAsync(List<CleanItem> items)
        {
            _isOperating = true;
            LogService.Instance.Info($"开始高级清理，共选择 {items.Count} 个项目...");
            LogService.Instance.Info("----------------------------------------");

            long totalSize = 0;
            int totalFiles = 0;
            int totalDirs = 0;
            int successCount = 0;
            int failCount = 0;
            var failedItems = new List<string>();

            try
            {
                foreach (var item in items)
                {
                    LogService.Instance.Info($"正在清理: {item.Name}");
                    if (!string.IsNullOrEmpty(item.Description))
                    {
                        LogService.Instance.Info($"  描述: {item.Description}");
                    }

                    try
                    {
                        var cleanResult = await Task.Run(() => _cleanService.CleanPaths(item.Paths));

                        if (cleanResult.FileCount > 0 || cleanResult.DirCount > 0)
                        {
                            LogService.Instance.Info($"  清理路径:");
                            foreach (var path in item.Paths)
                            {
                                var exists = Directory.Exists(path) || File.Exists(path);
                                LogService.Instance.Info($"    - {path} {(exists ? "" : "[不存在]")}");
                            }
                            LogService.Instance.Info($"  删除文件: {cleanResult.FileCount} 个");
                            LogService.Instance.Info($"  删除文件夹: {cleanResult.DirCount} 个");
                            LogService.Instance.Info($"  释放空间: {_cleanService.FormatSize(cleanResult.Size)}");
                            LogService.Instance.Success($"  {item.Name} 清理完成");
                            successCount++;
                        }
                        else
                        {
                            LogService.Instance.Info($"  该项目无需清理或路径不存在");
                            LogService.Instance.Info($"  清理路径:");
                            foreach (var path in item.Paths)
                            {
                                var exists = Directory.Exists(path) || File.Exists(path);
                                LogService.Instance.Info($"    - {path} {(exists ? "[已清空]" : "[不存在]")}");
                            }
                            successCount++;
                        }

                        totalSize += cleanResult.Size;
                        totalFiles += cleanResult.FileCount;
                        totalDirs += cleanResult.DirCount;
                    }
                    catch (Exception ex)
                    {
                        LogService.Instance.Error($"  {item.Name} 清理失败: {ex.Message}");
                        failCount++;
                        failedItems.Add(item.Name);
                    }

                    LogService.Instance.Info("----------------------------------------");
                }

                LogService.Instance.Info($"高级清理汇总:");
                LogService.Instance.Info($"  成功: {successCount} 个项目");
                if (failCount > 0)
                {
                    LogService.Instance.Warning($"  失败: {failCount} 个项目 ({string.Join(", ", failedItems)})");
                }
                LogService.Instance.Info($"  总计删除: {totalFiles} 个文件, {totalDirs} 个文件夹");
                LogService.Instance.Success($"高级清理完成，共释放空间: {_cleanService.FormatSize(totalSize)}");
            }
            catch (Exception ex)
            {
                LogService.Instance.Error($"高级清理失败: {ex.Message}");
            }
            finally
            {
                _isOperating = false;
            }
        }

        private async void CleanStoreCache_Click(object sender, RoutedEventArgs e)
        {
            if (_isOperating) return;
            _isOperating = true;
            try
            {
                await CleanStoreCacheCoreAsync();
                MarkCleaned();
            }
            finally
            {
                _isOperating = false;
            }
        }

        private async Task<long> CleanStoreCacheCoreAsync()
        {
            LogService.Instance.Info("开始清理微软应用商店缓存...");
            try
            {
                await Task.Run(() =>
                {
                    Process.Start(new ProcessStartInfo
                    {
                        FileName = "WSReset.exe",
                        UseShellExecute = true,
                        WindowStyle = ProcessWindowStyle.Hidden
                    });
                });
                LogService.Instance.Success("微软应用商店缓存清理已启动");
            }
            catch (Exception ex)
            {
                LogService.Instance.Error($"微软应用商店缓存清理失败: {ex.Message}");
            }
            return 0;
        }

        private async void CleanSystemLogs_Click(object sender, RoutedEventArgs e)
        {
            if (_isOperating) return;
            _isOperating = true;
            try
            {
                await CleanSystemLogsCoreAsync();
                MarkCleaned();
            }
            finally
            {
                _isOperating = false;
            }
        }

        private async Task<long> CleanSystemLogsCoreAsync()
        {

            LogService.Instance.Info("开始清理系统日志...");
            LogService.Instance.Info("----------------------------------------");

            try
            {
                var result = await Task.Run(() =>
                {
                    long totalSize = 0;
                    int fileCount = 0;
                    int dirCount = 0;

                    string logPath = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Windows), "Logs");
                    LogService.Instance.Info($"检查Windows日志目录: {logPath}");
                    
                    if (Directory.Exists(logPath))
                    {
                        var dirResult = DeleteDirectoryContents(logPath);
                        totalSize += dirResult.Size;
                        fileCount += dirResult.Count;
                        dirCount += dirResult.DirCount;
                        LogService.Instance.Info($"  Windows日志: 删除 {dirResult.Count} 个文件, {dirResult.DirCount} 个文件夹, 释放 {FormatSize(dirResult.Size)}");
                    }
                    else
                    {
                        LogService.Instance.Info($"  Windows日志目录不存在");
                    }

                    string cbsLogPath = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Windows), "Logs", "CBS");
                    if (Directory.Exists(cbsLogPath))
                    {
                        var cbsResult = DeleteDirectoryContents(cbsLogPath);
                        totalSize += cbsResult.Size;
                        fileCount += cbsResult.Count;
                        dirCount += cbsResult.DirCount;
                        LogService.Instance.Info($"  CBS日志: 删除 {cbsResult.Count} 个文件, 释放 {FormatSize(cbsResult.Size)}");
                    }

                    string dismLogPath = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Windows), "Logs", "DISM");
                    if (Directory.Exists(dismLogPath))
                    {
                        var dismResult = DeleteDirectoryContents(dismLogPath);
                        totalSize += dismResult.Size;
                        fileCount += dismResult.Count;
                        LogService.Instance.Info($"  DISM日志: 删除 {dismResult.Count} 个文件, 释放 {FormatSize(dismResult.Size)}");
                    }

                    LogService.Instance.Info("正在清理Windows事件日志...");
                    Process.Start(new ProcessStartInfo
                    {
                        FileName = "cmd.exe",
                        Arguments = "/c wevtutil el | foreach { wevtutil cl $_ }",
                        UseShellExecute = true,
                        WindowStyle = ProcessWindowStyle.Hidden
                    });

                    return (totalSize, fileCount, dirCount);
                });

                LogService.Instance.Info("----------------------------------------");
                LogService.Instance.Info($"系统日志清理汇总:");
                LogService.Instance.Info($"  删除文件: {result.fileCount} 个");
                LogService.Instance.Info($"  删除文件夹: {result.dirCount} 个");
                LogService.Instance.Success($"系统日志清理完成，释放空间: {FormatSize(result.totalSize)}");
                return result.totalSize;
            }
            catch (Exception ex)
            {
                LogService.Instance.Error($"系统日志清理失败: {ex.Message}");
                return 0;
            }
        }

        private async void CleanSystemCache_Click(object sender, RoutedEventArgs e)
        {
            if (_isOperating) return;
            _isOperating = true;
            try
            {
                await CleanSystemCacheCoreAsync();
                MarkCleaned();
            }
            finally
            {
                _isOperating = false;
            }
        }

        private async Task<long> CleanSystemCacheCoreAsync()
        {

            LogService.Instance.Info("开始清理系统缓存...");
            LogService.Instance.Info("----------------------------------------");

            try
            {
                var result = await Task.Run(() =>
                {
                    long totalSize = 0;
                    int fileCount = 0;
                    int dirCount = 0;

                    string tempPath = Path.GetTempPath();
                    LogService.Instance.Info($"清理临时文件夹: {tempPath}");
                    var tempResult = DeleteDirectoryContents(tempPath);
                    totalSize += tempResult.Size;
                    fileCount += tempResult.Count;
                    dirCount += tempResult.DirCount;
                    LogService.Instance.Info($"  临时文件: 删除 {tempResult.Count} 个文件, {tempResult.DirCount} 个文件夹, 释放 {FormatSize(tempResult.Size)}");

                    string prefetchPath = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Windows), "Prefetch");
                    LogService.Instance.Info($"清理预读取文件: {prefetchPath}");
                    if (Directory.Exists(prefetchPath))
                    {
                        var prefetchResult = DeleteDirectoryContents(prefetchPath);
                        totalSize += prefetchResult.Size;
                        fileCount += prefetchResult.Count;
                        dirCount += prefetchResult.DirCount;
                        LogService.Instance.Info($"  预读取文件: 删除 {prefetchResult.Count} 个文件, 释放 {FormatSize(prefetchResult.Size)}");
                    }
                    else
                    {
                        LogService.Instance.Info($"  预读取目录不存在");
                    }

                    string recentPath = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Recent));
                    LogService.Instance.Info($"清理最近使用记录: {recentPath}");
                    if (Directory.Exists(recentPath))
                    {
                        var recentResult = DeleteDirectoryContents(recentPath);
                        totalSize += recentResult.Size;
                        fileCount += recentResult.Count;
                        LogService.Instance.Info($"  最近使用记录: 删除 {recentResult.Count} 个文件, 释放 {FormatSize(recentResult.Size)}");
                    }

                    string windowsTempPath = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Windows), "Temp");
                    LogService.Instance.Info($"清理Windows临时文件: {windowsTempPath}");
                    if (Directory.Exists(windowsTempPath))
                    {
                        var winTempResult = DeleteDirectoryContents(windowsTempPath);
                        totalSize += winTempResult.Size;
                        fileCount += winTempResult.Count;
                        dirCount += winTempResult.DirCount;
                        LogService.Instance.Info($"  Windows临时文件: 删除 {winTempResult.Count} 个文件, 释放 {FormatSize(winTempResult.Size)}");
                    }

                    string thumbnailCachePath = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Microsoft", "Windows", "Explorer");
                    LogService.Instance.Info($"清理缩略图缓存: {thumbnailCachePath}");
                    if (Directory.Exists(thumbnailCachePath))
                    {
                        int thumbCount = 0;
                        long thumbSize = 0;
                        foreach (var file in Directory.GetFiles(thumbnailCachePath, "thumbcache_*.db"))
                        {
                            try
                            {
                                var fileInfo = new FileInfo(file);
                                thumbSize += fileInfo.Length;
                                File.Delete(file);
                                thumbCount++;
                            }
                            catch (Exception ex)
                            {
                                LogService.Instance.Warning("[CleanerPage.CleanSystemCache_Click] 删除失败", ex);
                            }
                        }
                        totalSize += thumbSize;
                        fileCount += thumbCount;
                        LogService.Instance.Info($"  缩略图缓存: 删除 {thumbCount} 个文件, 释放 {FormatSize(thumbSize)}");
                    }

                    return (totalSize, fileCount, dirCount);
                });

                LogService.Instance.Info("----------------------------------------");
                LogService.Instance.Info($"系统缓存清理汇总:");
                LogService.Instance.Info($"  删除文件: {result.fileCount} 个");
                LogService.Instance.Info($"  删除文件夹: {result.dirCount} 个");
                LogService.Instance.Success($"系统缓存清理完成，释放空间: {FormatSize(result.totalSize)}");
                return result.totalSize;
            }
            catch (Exception ex)
            {
                LogService.Instance.Error($"系统缓存清理失败: {ex.Message}");
                return 0;
            }
        }

        private async void CleanBrowserCache_Click(object sender, RoutedEventArgs e)
        {
            if (_isOperating) return;
            _isOperating = true;
            try
            {
                await CleanBrowserCacheCoreAsync();
                MarkCleaned();
            }
            finally
            {
                _isOperating = false;
            }
        }

        private async Task<long> CleanBrowserCacheCoreAsync()
        {

            LogService.Instance.Info("开始清理浏览器缓存...");
            LogService.Instance.Info("----------------------------------------");

            try
            {
                var result = await Task.Run(() =>
                {
                    long totalSize = 0;
                    int fileCount = 0;
                    int dirCount = 0;
                    string userProfile = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);

                    var browserPaths = new Dictionary<string, string>();
                    foreach (var p in GetChromiumProfileCachePaths(Path.Combine(userProfile, @"AppData\Local\Google\Chrome\User Data")))
                        browserPaths[$"Chrome缓存({Path.GetFileName(Path.GetDirectoryName(p))})"] = p;
                    foreach (var p in GetChromiumProfileCachePaths(Path.Combine(userProfile, @"AppData\Local\Microsoft\Edge\User Data")))
                        browserPaths[$"Edge缓存({Path.GetFileName(Path.GetDirectoryName(p))})"] = p;
                    browserPaths.Add("Firefox缓存", Path.Combine(userProfile, @"AppData\Local\Mozilla\Firefox\Profiles"));
                    browserPaths.Add("360安全浏览器缓存", Path.Combine(userProfile, @"AppData\Local\360Chrome\Chrome\User Data\Default\Cache"));
                    browserPaths.Add("QQ浏览器缓存", Path.Combine(userProfile, @"AppData\Local\Tencent\QQBrowser\User Data\Default\Cache"));

                    foreach (var browser in browserPaths)
                    {
                        if (Directory.Exists(browser.Value))
                        {
                            var dirResult = DeleteDirectoryContents(browser.Value);
                            if (dirResult.Count > 0 || dirResult.Size > 0)
                            {
                                totalSize += dirResult.Size;
                                fileCount += dirResult.Count;
                                dirCount += dirResult.DirCount;
                                LogService.Instance.Info($"  {browser.Key}: 删除 {dirResult.Count} 个文件, 释放 {FormatSize(dirResult.Size)}");
                            }
                        }
                    }

                    return (totalSize, fileCount, dirCount);
                });

                LogService.Instance.Info("----------------------------------------");
                LogService.Instance.Info($"浏览器缓存清理汇总:");
                LogService.Instance.Info($"  删除文件: {result.fileCount} 个");
                LogService.Instance.Info($"  删除文件夹: {result.dirCount} 个");
                LogService.Instance.Success($"浏览器缓存清理完成，释放空间: {FormatSize(result.totalSize)}");
                return result.totalSize;
            }
            catch (Exception ex)
            {
                LogService.Instance.Error($"浏览器缓存清理失败: {ex.Message}");
                return 0;
            }
        }



        private async void CleanQQMusicCache_Click(object sender, RoutedEventArgs e)
        {
            if (_isOperating) return;
            _isOperating = true;
            try
            {
                await CleanQQMusicCacheCoreAsync();
                MarkCleaned();
            }
            finally
            {
                _isOperating = false;
            }
        }

        private async Task<long> CleanQQMusicCacheCoreAsync()
        {

            LogService.Instance.Info("开始清理QQ音乐缓存...");
            LogService.Instance.Info("----------------------------------------");

            try
            {
                var result = await Task.Run(() =>
                {
                    LogService.Instance.Info("正在关闭QQ音乐进程...");
                    KillProcesses(new[] { "QQMusic", "QQMusicService", "QQMusicLauncher" });
                    Thread.Sleep(2000);

                    var cachePaths = GetQQMusicCachePaths();
                    LogService.Instance.Info($"找到 {cachePaths.Count} 个缓存路径");
                    
                    return CleanCachePathsDetailed(cachePaths, "QQ音乐");
                });

                LogService.Instance.Info("----------------------------------------");
                LogService.Instance.Info($"QQ音乐缓存清理汇总:");
                LogService.Instance.Info($"  删除文件: {result.FileCount} 个");
                LogService.Instance.Info($"  删除文件夹: {result.DirCount} 个");
                LogService.Instance.Success($"QQ音乐缓存清理完成，释放空间: {FormatSize(result.Size)}");
                return result.Size;
            }
            catch (Exception ex)
            {
                LogService.Instance.Error($"QQ音乐缓存清理失败: {ex.Message}");
                return 0;
            }
        }

        private async void CleanCloudMusicCache_Click(object sender, RoutedEventArgs e)
        {
            if (_isOperating) return;
            _isOperating = true;
            try
            {
                await CleanCloudMusicCacheCoreAsync();
                MarkCleaned();
            }
            finally
            {
                _isOperating = false;
            }
        }

        private async Task<long> CleanCloudMusicCacheCoreAsync()
        {

            LogService.Instance.Info("开始清理网易云音乐缓存...");
            LogService.Instance.Info("----------------------------------------");

            try
            {
                var result = await Task.Run(() =>
                {
                    LogService.Instance.Info("正在关闭网易云音乐进程...");
                    KillProcesses(new[] { "cloudmusic", "网易云音乐" });
                    Thread.Sleep(2000);

                    var cachePaths = GetCloudMusicCachePaths();
                    LogService.Instance.Info($"找到 {cachePaths.Count} 个缓存路径");
                    
                    return CleanCachePathsDetailed(cachePaths, "网易云音乐");
                });

                LogService.Instance.Info("----------------------------------------");
                LogService.Instance.Info($"网易云音乐缓存清理汇总:");
                LogService.Instance.Info($"  删除文件: {result.FileCount} 个");
                LogService.Instance.Info($"  删除文件夹: {result.DirCount} 个");
                LogService.Instance.Success($"网易云音乐缓存清理完成，释放空间: {FormatSize(result.Size)}");
                return result.Size;
            }
            catch (Exception ex)
            {
                LogService.Instance.Error($"网易云音乐缓存清理失败: {ex.Message}");
                return 0;
            }
        }

        private async void CleanDouyinCache_Click(object sender, RoutedEventArgs e)
        {
            if (_isOperating) return;
            _isOperating = true;
            try
            {
                await CleanDouyinCacheCoreAsync();
                MarkCleaned();
            }
            finally
            {
                _isOperating = false;
            }
        }

        private async Task<long> CleanDouyinCacheCoreAsync()
        {

            LogService.Instance.Info("开始清理抖音缓存...");
            LogService.Instance.Info("----------------------------------------");

            try
            {
                var result = await Task.Run(() =>
                {
                    LogService.Instance.Info("正在关闭抖音进程...");
                    KillProcesses(new[] { "Douyin", "TikTok", "抖音" });
                    Thread.Sleep(2000);

                    var cachePaths = GetDouyinCachePaths();
                    LogService.Instance.Info($"找到 {cachePaths.Count} 个缓存路径");
                    
                    return CleanCachePathsDetailed(cachePaths, "抖音");
                });

                LogService.Instance.Info("----------------------------------------");
                LogService.Instance.Info($"抖音缓存清理汇总:");
                LogService.Instance.Info($"  删除文件: {result.FileCount} 个");
                LogService.Instance.Info($"  删除文件夹: {result.DirCount} 个");
                LogService.Instance.Success($"抖音缓存清理完成，释放空间: {FormatSize(result.Size)}");
                return result.Size;
            }
            catch (Exception ex)
            {
                LogService.Instance.Error($"抖音缓存清理失败: {ex.Message}");
                return 0;
            }
        }

        private async void CleanKuGouCache_Click(object sender, RoutedEventArgs e)
        {
            if (_isOperating) return;
            _isOperating = true;
            try
            {
                await CleanKuGouCacheCoreAsync();
                MarkCleaned();
            }
            finally
            {
                _isOperating = false;
            }
        }

        private async Task<long> CleanKuGouCacheCoreAsync()
        {

            LogService.Instance.Info("开始清理酷狗音乐缓存...");
            LogService.Instance.Info("----------------------------------------");

            try
            {
                var result = await Task.Run(() =>
                {
                    LogService.Instance.Info("正在关闭酷狗音乐进程...");
                    KillProcesses(new[] { "KuGou", "酷狗音乐", "KuGouMusic" });
                    Thread.Sleep(2000);

                    var cachePaths = GetKuGouCachePaths();
                    LogService.Instance.Info($"找到 {cachePaths.Count} 个缓存路径");
                    
                    return CleanCachePathsDetailed(cachePaths, "酷狗音乐");
                });

                LogService.Instance.Info("----------------------------------------");
                LogService.Instance.Info($"酷狗音乐缓存清理汇总:");
                LogService.Instance.Info($"  删除文件: {result.FileCount} 个");
                LogService.Instance.Info($"  删除文件夹: {result.DirCount} 个");
                LogService.Instance.Success($"酷狗音乐缓存清理完成，释放空间: {FormatSize(result.Size)}");
                return result.Size;
            }
            catch (Exception ex)
            {
                LogService.Instance.Error($"酷狗音乐缓存清理失败: {ex.Message}");
                return 0;
            }
        }

        private async void CleanWeChatCache_Click(object sender, RoutedEventArgs e)
        {
            if (_isOperating) return;
            _isOperating = true;
            try
            {
                await CleanWeChatCacheCoreAsync();
                MarkCleaned();
            }
            finally
            {
                _isOperating = false;
            }
        }

        private async Task<long> CleanWeChatCacheCoreAsync()
        {

            LogService.Instance.Info("开始清理微信缓存...");
            LogService.Instance.Info("----------------------------------------");

            try
            {
                var result = await Task.Run(() =>
                {
                    LogService.Instance.Info("正在关闭微信进程...");
                    KillProcesses(new[] { "WeChat", "WeChatAppEx", "WeChatApp" });
                    Thread.Sleep(2000);

                    long totalSize = 0;
                    int totalFiles = 0;
                    int totalDirs = 0;

                    var cachePaths = GetWeChatCachePaths();
                    LogService.Instance.Info($"找到 {cachePaths.Count} 个缓存路径");
                    
                    var cacheResult = CleanCachePathsDetailed(cachePaths, "微信");
                    totalSize += cacheResult.Size;
                    totalFiles += cacheResult.FileCount;
                    totalDirs += cacheResult.DirCount;

                    LogService.Instance.Info("清理微信xlog日志文件...");
                    var xlogResult = CleanWeChatXlogFiles();
                    totalSize += xlogResult.Size;
                    totalFiles += xlogResult.FileCount;

                    return (totalSize, totalFiles, totalDirs);
                });

                LogService.Instance.Info("----------------------------------------");
                LogService.Instance.Info($"微信缓存清理汇总:");
                LogService.Instance.Info($"  删除文件: {result.totalFiles} 个");
                LogService.Instance.Info($"  删除文件夹: {result.totalDirs} 个");
                LogService.Instance.Success($"微信缓存清理完成，释放空间: {FormatSize(result.totalSize)}");
                return result.totalSize;
            }
            catch (Exception ex)
            {
                LogService.Instance.Error($"微信缓存清理失败: {ex.Message}");
                return 0;
            }
        }

        private async void CleanQQCache_Click(object sender, RoutedEventArgs e)
        {
            if (_isOperating) return;
            _isOperating = true;
            try
            {
                await CleanQQCacheCoreAsync();
                MarkCleaned();
            }
            finally
            {
                _isOperating = false;
            }
        }

        private async Task<long> CleanQQCacheCoreAsync()
        {

            LogService.Instance.Info("开始清理QQ缓存...");
            LogService.Instance.Info("----------------------------------------");

            try
            {
                var result = await Task.Run(() =>
                {
                    LogService.Instance.Info("正在关闭QQ相关进程...");
                    KillProcesses(new[] { "QQ", "TIM", "QQMusic", "QQBrowser" });
                    Thread.Sleep(2000);

                    long totalSize = 0;
                    int totalFiles = 0;
                    int totalDirs = 0;

                    var cachePaths = GetQQCachePaths();
                    LogService.Instance.Info($"找到 {cachePaths.Count} 个缓存路径");
                    
                    foreach (var cachePath in cachePaths)
                    {
                        if (Directory.Exists(cachePath))
                        {
                            var cleanResult = CleanQQCacheDirectory(cachePath);
                            totalSize += cleanResult.Size;
                            totalFiles += cleanResult.FileCount;
                            totalDirs += cleanResult.DirCount;
                        }
                    }

                    return (totalSize, totalFiles, totalDirs);
                });

                LogService.Instance.Info("----------------------------------------");
                LogService.Instance.Info($"QQ缓存清理汇总:");
                LogService.Instance.Info($"  删除文件: {result.totalFiles} 个");
                LogService.Instance.Info($"  删除文件夹: {result.totalDirs} 个");
                LogService.Instance.Success($"QQ缓存清理完成，释放空间: {FormatSize(result.totalSize)}");
                return result.totalSize;
            }
            catch (Exception ex)
            {
                LogService.Instance.Error($"QQ缓存清理失败: {ex.Message}");
                return 0;
            }
        }

        private List<string> GetQQCachePaths()
        {
            var paths = new List<string>();
            var userProfile = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
            var documentsPath = Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments);
            var localAppData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
            var appData = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);

            var tencentPaths = new List<string>
            {
                Path.Combine(documentsPath, "Tencent Files"),
                Path.Combine(userProfile, "Documents", "Tencent Files"),
            };

            foreach (var drive in DriveInfo.GetDrives())
            {
                if (!drive.IsReady)
                    continue;

                var driveRoot = drive.RootDirectory.FullName;
                tencentPaths.Add(Path.Combine(driveRoot, "Tencent Files"));
                tencentPaths.Add(Path.Combine(driveRoot, "Documents", "Tencent Files"));
                tencentPaths.Add(Path.Combine(driveRoot, "Users", Environment.UserName, "Documents", "Tencent Files"));
            }

            foreach (var tencentPath in tencentPaths.Distinct(StringComparer.OrdinalIgnoreCase))
            {
                if (!Directory.Exists(tencentPath))
                    continue;

                try
                {
                    foreach (var userDir in Directory.GetDirectories(tencentPath))
                    {
                        var dirName = Path.GetFileName(userDir);
                        if (dirName == "All Users" || dirName == "Applet")
                            continue;

                        var cacheSubPaths = new[]
                        {
                            Path.Combine(userDir, "FileStorage", "Cache"),
                            Path.Combine(userDir, "FileStorage", "Temp"),
                            Path.Combine(userDir, "Image", "Temp"),
                            Path.Combine(userDir, "Temp"),
                        };

                        foreach (var cachePath in cacheSubPaths)
                        {
                            if (Directory.Exists(cachePath))
                            {
                                paths.Add(cachePath);
                            }
                        }

                        var ntQQPath = Path.Combine(userDir, "nt_qq");
                        ScanNtQQAccountDir(ntQQPath, paths);
                    }
                }
                catch (Exception ex)
                {
                    LogService.Instance.Warning("[CleanerPage.GetQQCachePaths] 枚举目录失败", ex);
                }
            }

            var qqAppPaths = new[]
            {
                Path.Combine(localAppData, "Tencent", "QQ", "Temp"),
                Path.Combine(localAppData, "Tencent", "QQ", "Cache"),
                Path.Combine(appData, "Tencent", "QQ", "Temp"),
                Path.Combine(appData, "Tencent", "QQ", "Cache"),
                Path.Combine(localAppData, "Tencent", "QQ", "Misc"),
                Path.Combine(localAppData, "Tencent", "QQ", "Logs"),
                Path.Combine(localAppData, "Tencent", "QQ", "WebCache"),
                Path.Combine(localAppData, "Tencent", "QQ", "Download", "Temp"),
            };

            foreach (var path in qqAppPaths)
            {
                if (Directory.Exists(path))
                {
                    paths.Add(path);
                }
            }

            var ntQQPaths = new[]
            {
                Path.Combine(localAppData, "Tencent", "QQNT", "QQ", "Cache"),
                Path.Combine(localAppData, "Tencent", "QQNT", "QQ", "Temp"),
                Path.Combine(localAppData, "Tencent", "QQNT", "QQ", "nt_qq", "Cache"),
                Path.Combine(localAppData, "Tencent", "QQNT", "QQ", "nt_qq", "Temp"),
                Path.Combine(localAppData, "Tencent", "QQNT", "QQ", "nt_qq", "FileStorage", "Cache"),
                Path.Combine(localAppData, "Tencent", "QQNT", "QQ", "nt_qq", "FileStorage", "Temp"),
            };

            foreach (var path in ntQQPaths)
            {
                if (Directory.Exists(path))
                {
                    paths.Add(path);
                }
            }

            // NTQQ 主数据目录（新版 QQ）：%LocalAppData%\Tencent\QQ\nt_qq，其下为各账号目录
            CollectNtQQDataRoot(Path.Combine(localAppData, "Tencent", "QQ", "nt_qq"), paths);

            // 注册表自定义数据路径（自适应）：HKCU\Software\Tencent\QQNT\PersonalPath|DataPath|PersonalFolder
            var regNtQQRoot = GetRegistryStringValueHKCU(@"Software\Tencent\QQNT", "PersonalPath", "DataPath", "PersonalFolder");
            if (!string.IsNullOrEmpty(regNtQQRoot))
            {
                CollectNtQQDataRoot(regNtQQRoot, paths);
            }

            return paths;
        }

        /// <summary>
        /// 扫描一个 nt_qq 账号目录：Cache/Temp/nt_temp + nt_data 下可重建的子目录。
        /// 注意：nt_data\{Pic,Video,Ptt,File} 为用户收发的媒体文件，nt_db/nt_msg 为数据库——一律不碰。
        /// 可安全删除的子目录清单交叉核对自开源清理工具 light-c（Chunyu33/light-c）的 NTQQ 适配器。
        /// </summary>
        private void ScanNtQQAccountDir(string accountDir, List<string> paths)
        {
            if (string.IsNullOrEmpty(accountDir) || !Directory.Exists(accountDir))
                return;

            var subPaths = new[]
            {
                Path.Combine(accountDir, "Cache"),
                Path.Combine(accountDir, "Temp"),
                Path.Combine(accountDir, "nt_temp"),
                Path.Combine(accountDir, "nt_data", "log"),
                Path.Combine(accountDir, "nt_data", "log-cache"),
                Path.Combine(accountDir, "nt_data", "Emoji"),
                Path.Combine(accountDir, "nt_data", "PokeFace"),
                Path.Combine(accountDir, "nt_data", "Skin"),
                Path.Combine(accountDir, "nt_data", "ntnnModel"),
                Path.Combine(accountDir, "nt_data", "onlineStatus"),
                Path.Combine(accountDir, "nt_data", "msf"),
                Path.Combine(accountDir, "nt_data", "Login"),
                Path.Combine(accountDir, "nt_data", "mmkv"),
                Path.Combine(accountDir, "nt_data", "search"),
            };

            foreach (var cachePath in subPaths)
            {
                if (Directory.Exists(cachePath) && !paths.Contains(cachePath, StringComparer.OrdinalIgnoreCase))
                {
                    paths.Add(cachePath);
                }
            }
        }

        /// <summary>
        /// 扫描一个 nt_qq 数据根目录：其下为各账号目录（另有 global 全局目录，只含数据库，不碰）。
        /// 兼容老架构账号目录下再嵌一层 nt_qq 的情况。
        /// </summary>
        private void CollectNtQQDataRoot(string ntQQRoot, List<string> paths)
        {
            if (string.IsNullOrEmpty(ntQQRoot) || !Directory.Exists(ntQQRoot))
                return;

            // 注册表给的可能直接就是账号目录（内含 nt_data/nt_db/nt_msg）
            if (Directory.Exists(Path.Combine(ntQQRoot, "nt_data")) ||
                Directory.Exists(Path.Combine(ntQQRoot, "nt_db")) ||
                Directory.Exists(Path.Combine(ntQQRoot, "nt_msg")))
            {
                ScanNtQQAccountDir(ntQQRoot, paths);
                return;
            }

            try
            {
                foreach (var accountDir in Directory.GetDirectories(ntQQRoot))
                {
                    var dirName = Path.GetFileName(accountDir);
                    if (dirName.Equals("global", StringComparison.OrdinalIgnoreCase))
                        continue; // 全局目录只含 nt_db 等数据库，不碰

                    ScanNtQQAccountDir(accountDir, paths);

                    var nested = Path.Combine(accountDir, "nt_qq");
                    if (Directory.Exists(nested))
                        ScanNtQQAccountDir(nested, paths);
                }
            }
            catch (Exception ex)
            {
                LogService.Instance.Warning("[CleanerPage.CollectNtQQDataRoot] 枚举目录失败", ex);
            }
        }

        /// <summary>
        /// 从 HKCU 读取字符串注册表值（依次尝试多个值名，含 Wow6432Node）。
        /// </summary>
        private string? GetRegistryStringValueHKCU(string subKey, params string[] valueNames)
        {
            try
            {
                var stripped = subKey.StartsWith(@"Software\", StringComparison.OrdinalIgnoreCase)
                    ? subKey.Substring(@"Software\".Length)
                    : subKey;
                foreach (var keyPath in new[] { subKey, @"SOFTWARE\Wow6432Node\" + stripped })
                {
                    using (var key = Registry.CurrentUser.OpenSubKey(keyPath))
                    {
                        if (key == null)
                            continue;
                        foreach (var valueName in valueNames)
                        {
                            var value = key.GetValue(valueName) as string;
                            if (!string.IsNullOrEmpty(value))
                                return value;
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                LogService.Instance.Warning("[CleanerPage.GetRegistryStringValueHKCU] 执行失败", ex);
            }

            return null;
        }

        private (long Size, int FileCount, int DirCount) CleanQQCacheDirectory(string path)
        {
            long totalSize = 0;
            int totalFiles = 0;
            int totalDirs = 0;

            var protectedFolders = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
            {
                "Image", "Images", "图片",
                "Video", "Videos", "视频",
                "Voice", "Audio", "语音",
                "Pic", "Photo", "Photos",
                "ShortVideo", "MicroMsg", "MsgAttach",
                "FileRecv", "Filerecv", "Received Files"
            };

            var protectedExtensions = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
            {
                ".jpg", ".jpeg", ".png", ".gif", ".bmp", ".webp",
                ".mp4", ".avi", ".mkv", ".mov", ".wmv", ".flv",
                ".mp3", ".wav", ".aac", ".flac", ".wma", ".amr", ".m4a", ".ogg",
                ".doc", ".docx", ".xls", ".xlsx", ".ppt", ".pptx", ".pdf",
                ".zip", ".rar", ".7z", ".tar", ".gz"
            };

            try
            {
                if (!Directory.Exists(path))
                {
                    return (0, 0, 0);
                }

                var dirInfo = new DirectoryInfo(path);
                LogService.Instance.Info($"清理目录: {path}");

                foreach (var file in dirInfo.GetFiles("*", SearchOption.AllDirectories))
                {
                    try
                    {
                        totalSize += file.Length;
                        file.Delete();
                        totalFiles++;
                    }
                    catch (Exception ex)
                    {
                        LogService.Instance.Warning("[CleanerPage.未知方法] 执行失败", ex);
                    }
                }

                foreach (var dir in dirInfo.GetDirectories("*", SearchOption.AllDirectories))
                {
                    try
                    {
                        dir.Delete(true);
                        totalDirs++;
                    }
                    catch (Exception ex)
                    {
                        LogService.Instance.Warning("[CleanerPage.未知方法] 执行失败", ex);
                    }
                }

                if (totalFiles > 0 || totalDirs > 0)
                {
                    LogService.Instance.Info($"  删除 {totalFiles} 个文件, {totalDirs} 个文件夹, 释放 {FormatSize(totalSize)}");
                }
            }
            catch (Exception ex)
            {
                LogService.Instance.Warning($"  清理目录失败: {ex.Message}");
            }

            return (totalSize, totalFiles, totalDirs);
        }

        private (long Size, int FileCount, int DirCount) CleanQQCacheDirectoryRecursive(
            string path, HashSet<string> protectedFolders, HashSet<string> protectedExtensions)
        {
            long totalSize = 0;
            int totalFiles = 0;
            int totalDirs = 0;

            try
            {
                var dirInfo = new DirectoryInfo(path);

                foreach (var dir in dirInfo.GetDirectories("*", SearchOption.TopDirectoryOnly))
                {
                    if (protectedFolders.Contains(dir.Name))
                    {
                        continue;
                    }

                    try
                    {
                        var subResult = CleanQQCacheDirectoryRecursive(dir.FullName, protectedFolders, protectedExtensions);
                        totalSize += subResult.Size;
                        totalFiles += subResult.FileCount;
                        totalDirs += subResult.DirCount;

                        if (!dir.EnumerateFileSystemInfos().Any())
                        {
                            dir.Delete();
                            totalDirs++;
                        }
                    }
                    catch (Exception ex)
                    {
                        LogService.Instance.Warning("[CleanerPage.未知方法] 执行失败", ex);
                    }
                }

                foreach (var file in dirInfo.GetFiles("*", SearchOption.TopDirectoryOnly))
                {
                    var ext = file.Extension;
                    if (protectedExtensions.Contains(ext))
                    {
                        continue;
                    }

                    try
                    {
                        totalSize += file.Length;
                        file.Delete();
                        totalFiles++;
                    }
                    catch (Exception ex)
                    {
                        LogService.Instance.Warning("[CleanerPage.未知方法] 执行失败", ex);
                    }
                }
            }
            catch (Exception ex)
            {
                LogService.Instance.Warning("[CleanerPage.未知方法] 执行失败", ex);
            }

            return (totalSize, totalFiles, totalDirs);
        }

        private (long Size, int FileCount) CleanWeChatXlogFiles()
        {
            long totalSize = 0;
            int fileCount = 0;
            var appData = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);

            var xwechatLogPath = Path.Combine(appData, "Tencent", "xwechat", "log");
            LogService.Instance.Info($"查找微信日志路径: {xwechatLogPath}");

            if (Directory.Exists(xwechatLogPath))
            {
                try
                {
                    var xlogFiles = Directory.GetFiles(xwechatLogPath, "*.xlog", SearchOption.TopDirectoryOnly);
                    LogService.Instance.Info($"找到 {xlogFiles.Length} 个.xlog文件");

                    foreach (var file in xlogFiles)
                    {
                        try
                        {
                            var fileInfo = new FileInfo(file);
                            totalSize += fileInfo.Length;
                            File.Delete(file);
                            fileCount++;
                        }
                        catch (Exception ex)
                        {
                            LogService.Instance.Warning("[CleanerPage.未知方法] 执行失败", ex);
                        }
                    }
                    
                    if (fileCount > 0)
                    {
                        LogService.Instance.Info($"  xlog日志: 删除 {fileCount} 个文件, 释放 {FormatSize(totalSize)}");
                    }
                }
                catch (Exception ex)
                {
                    LogService.Instance.Warning($"清理xlog文件失败: {ex.Message}");
                }
            }

            return (totalSize, fileCount);
        }

        private List<string> GetWeChatCachePaths()
        {
            var paths = new List<string>();
            var documentsPath = Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments);
            var localAppData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
            var appData = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);

            // 默认位置 + 各盘符根目录自适应（微信支持更改文件存储位置，如 D:\WeChat Files）
            var roots = new List<string>
            {
                Path.Combine(documentsPath, "WeChat Files"),
                Path.Combine(documentsPath, "xwechat_files"),
            };
            foreach (var drive in DriveInfo.GetDrives())
            {
                try
                {
                    if (!drive.IsReady || (drive.DriveType != DriveType.Fixed && drive.DriveType != DriveType.Removable))
                        continue;
                    roots.Add(Path.Combine(drive.RootDirectory.FullName, "WeChat Files"));
                    roots.Add(Path.Combine(drive.RootDirectory.FullName, "xwechat_files"));
                }
                catch { }
            }

            // 注册表自定义文件存储路径（自适应，解决改到非盘符根目录后扫不到的问题）：
            // 旧版 3.x: HKCU\Software\Tencent\WeChat\FileSavePath
            // 新版 4.x: HKCU\Software\Tencent\Weixin\FileSavePath
            // 值为 "MyDocument:" 时表示使用系统文档目录（已在 roots 中）
            var directAccountDirs = new List<string>();
            foreach (var regKey in new[] { @"Software\Tencent\WeChat", @"Software\Tencent\Weixin" })
            {
                var regPath = GetRegistryStringValueHKCU(regKey, "FileSavePath");
                if (string.IsNullOrEmpty(regPath) || regPath.Equals("MyDocument:", StringComparison.OrdinalIgnoreCase))
                    continue;
                AddWeChatDataRootCandidate(regPath, roots, directAccountDirs);
            }

            foreach (var weChatFilesPath in roots.Distinct(StringComparer.OrdinalIgnoreCase))
            {
                if (!Directory.Exists(weChatFilesPath))
                    continue;
                try
                {
                    foreach (var userDir in Directory.GetDirectories(weChatFilesPath))
                    {
                        ScanWeChatAccountDir(userDir, paths);
                    }
                }
                catch (Exception ex)
                {
                    LogService.Instance.Warning("[CleanerPage.GetWeChatCachePaths] 枚举目录失败", ex);
                }
            }

            // 注册表直接指向账号目录的情况
            foreach (var accountDir in directAccountDirs.Distinct(StringComparer.OrdinalIgnoreCase))
            {
                ScanWeChatAccountDir(accountDir, paths);
            }

            var xwechatLogPath = Path.Combine(appData, "Tencent", "xwechat", "log");
            if (Directory.Exists(xwechatLogPath))
            {
                paths.Add(xwechatLogPath);
            }

            var xpluginPath = Path.Combine(localAppData, "Tencent", "WeChat", "XPlugin");
            if (Directory.Exists(xpluginPath))
            {
                paths.Add(xpluginPath);
            }

            var wechatLogPath = Path.Combine(localAppData, "Tencent", "WeChat", "log");
            if (Directory.Exists(wechatLogPath))
            {
                paths.Add(wechatLogPath);
            }

            return paths;
        }

        /// <summary>
        /// 把注册表读到的微信自定义路径归一为"数据根"或"账号目录"。
        /// 注册表值可能是数据根（WeChat Files/xwechat_files）、账号目录，也可能是指向它们的父目录。
        /// </summary>
        private void AddWeChatDataRootCandidate(string configuredPath, List<string> roots, List<string> directAccountDirs)
        {
            if (string.IsNullOrEmpty(configuredPath) || !Directory.Exists(configuredPath))
                return;

            // 1. 本身就是账号目录（有 FileStorage 或 db_storage/msg）
            if (Directory.Exists(Path.Combine(configuredPath, "FileStorage")) ||
                Directory.Exists(Path.Combine(configuredPath, "db_storage")) ||
                Directory.Exists(Path.Combine(configuredPath, "msg")))
            {
                directAccountDirs.Add(configuredPath);
                return;
            }

            // 2. 本身就是数据根（WeChat Files / xwechat_files）
            var name = Path.GetFileName(configuredPath.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar));
            if (name.Equals("WeChat Files", StringComparison.OrdinalIgnoreCase) ||
                name.Equals("xwechat_files", StringComparison.OrdinalIgnoreCase))
            {
                roots.Add(configuredPath);
                return;
            }

            // 3. 其下可能挂着数据根
            foreach (var n in new[] { "WeChat Files", "xwechat_files" })
            {
                var nested = Path.Combine(configuredPath, n);
                if (Directory.Exists(nested))
                {
                    roots.Add(nested);
                    return;
                }
            }

            // 4. 兜底：当作数据根枚举
            roots.Add(configuredPath);
        }

        /// <summary>
        /// 扫描一个微信账号目录，自动区分 3.x（FileStorage）与 4.x（db_storage/msg/business）布局。
        /// 注意：db_storage 为聊天数据库、msg\{file,attach,video} 为用户收发的文件、BackupFiles 为备份——一律不碰。
        /// 4.x 可安全删除的子目录清单交叉核对自开源清理工具 light-c（Chunyu33/light-c）的微信适配器。
        /// </summary>
        private void ScanWeChatAccountDir(string accountDir, List<string> paths)
        {
            if (string.IsNullOrEmpty(accountDir) || !Directory.Exists(accountDir))
                return;

            var dirName = Path.GetFileName(accountDir);
            if (dirName == "All Users" || dirName == "Applet")
                return;

            void Add(string p)
            {
                if (Directory.Exists(p) && !paths.Contains(p, StringComparer.OrdinalIgnoreCase))
                    paths.Add(p);
            }

            bool isV4 = Directory.Exists(Path.Combine(accountDir, "db_storage")) ||
                        Directory.Exists(Path.Combine(accountDir, "msg"));

            if (isV4)
            {
                // 新版 4.x 布局
                Add(Path.Combine(accountDir, "msg", "migrate"));
                foreach (var n in new[] { "cache", "temp", "apm_record", "resource" })
                    Add(Path.Combine(accountDir, n));
                var business = Path.Combine(accountDir, "business");
                foreach (var n in new[] { "emoticon", "favorite", "xweb", "xeditor", "InputTemp", "migrate", "sns" })
                    Add(Path.Combine(business, n));
            }
            else
            {
                // 旧版 3.x 布局
                Add(Path.Combine(accountDir, "FileStorage", "Cache"));
                Add(Path.Combine(accountDir, "FileStorage", "Temp"));
            }
        }

        private List<string> GetQQMusicCachePaths()
        {
            var paths = new List<string>();
            var userProfile = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
            var appData = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
            var localAppData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);

            var defaultPaths = new[]
            {
                Path.Combine(appData, "Tencent", "QQMusic", "QQMusicCache"),
                Path.Combine(localAppData, "Tencent", "QQMusic", "Cache"),
                Path.Combine(localAppData, "Tencent", "QQMusic", "Temp"),
                Path.Combine(userProfile, "Music", "QQMusic", "Cache"),
                Path.Combine(userProfile, "Music", "QQMusic", "Temp"),
                Path.Combine(userProfile, "Documents", "Tencent", "QQMusic", "Cache"),
            };

            foreach (var path in defaultPaths)
            {
                if (Directory.Exists(path))
                {
                    paths.Add(path);
                }
            }

            var installPath = GetInstallPathFromRegistryMultiple(new[]
            {
                @"SOFTWARE\Microsoft\Windows\CurrentVersion\Uninstall\QQMusic",
                @"SOFTWARE\Tencent\QQMusic",
                @"SOFTWARE\WOW6432Node\Tencent\QQMusic"
            });

            if (!string.IsNullOrEmpty(installPath))
            {
                var regPaths = new[]
                {
                    Path.Combine(installPath, "Cache"),
                    Path.Combine(installPath, "QQMusicCache"),
                    Path.Combine(installPath, "Temp"),
                    Path.Combine(installPath, "Download"),
                    Path.Combine(installPath, "..", "UserData", "Cache"),
                    Path.Combine(installPath, "..", "UserData", "Temp")
                };

                foreach (var regPath in regPaths)
                {
                    try
                    {
                        var fullPath = Path.GetFullPath(regPath);
                        if (Directory.Exists(fullPath) && !paths.Contains(fullPath, StringComparer.OrdinalIgnoreCase))
                        {
                            paths.Add(fullPath);
                        }
                    }
                    catch (Exception ex)
                    {
                        LogService.Instance.Warning("[CleanerPage.GetQQMusicCachePaths] 执行失败", ex);
                    }
                }
            }

            var storePaths = GetMicrosoftStoreAppCachePaths("TencentQQMusic", new[] { "Cache", "Temp", "LocalCache" });
            foreach (var storePath in storePaths)
            {
                if (!paths.Contains(storePath, StringComparer.OrdinalIgnoreCase))
                {
                    paths.Add(storePath);
                }
            }

            var configCachePath = GetQQMusicConfigCachePath();
            if (!string.IsNullOrEmpty(configCachePath) && Directory.Exists(configCachePath) && !paths.Contains(configCachePath, StringComparer.OrdinalIgnoreCase))
            {
                paths.Add(configCachePath);
            }

            var allDrivePaths = ScanAllDrivesForMusicAppCache("QQMusic", new[] { "Cache", "Temp", "QQMusicCache", "Download", "LocalCache" });
            foreach (var drivePath in allDrivePaths)
            {
                if (!paths.Contains(drivePath, StringComparer.OrdinalIgnoreCase))
                {
                    paths.Add(drivePath);
                }
            }

            var appFolderPaths = ScanAllDrivesForCache(new[] { "QQMusic", "Tencent" }, new[] { "Cache", "Temp", "QQMusicCache" });
            foreach (var folderPath in appFolderPaths)
            {
                if (!paths.Contains(folderPath, StringComparer.OrdinalIgnoreCase))
                {
                    paths.Add(folderPath);
                }
            }

            // 自适应：QQ音乐自定义缓存根目录（如 D:\QQMusicCache）
            // 实测该目录下的可安全删除子文件夹；扫盘通用方法只认 Cache/Temp 等名字会漏掉它们
            foreach (var drive in DriveInfo.GetDrives())
            {
                try
                {
                    if (!drive.IsReady || (drive.DriveType != DriveType.Fixed && drive.DriveType != DriveType.Removable))
                        continue;
                    var cacheRoot = Path.Combine(drive.RootDirectory.FullName, "QQMusicCache");
                    if (!Directory.Exists(cacheRoot)) continue;
                    foreach (var sub in new[] { "Log", "WebkitCache", "QQMusicPicture", "QQMusicLyricNew", "Temp", "downloadproxyNew" })
                    {
                        var p = Path.Combine(cacheRoot, sub);
                        if (Directory.Exists(p) && !paths.Contains(p, StringComparer.OrdinalIgnoreCase))
                            paths.Add(p);
                    }
                }
                catch (Exception ex)
                {
                    LogService.Instance.Warning("[CleanerPage.GetQQMusicCachePaths] 枚举QQMusicCache根目录失败", ex);
                }
            }

            return paths;
        }

        private string? GetQQMusicConfigCachePath()
        {
            try
            {
                var appData = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
                var configFile = Path.Combine(appData, "Tencent", "QQMusic", "config.ini");

                if (File.Exists(configFile))
                {
                    var lines = File.ReadAllLines(configFile);
                    foreach (var line in lines)
                    {
                        if (line.StartsWith("CachePath=", StringComparison.OrdinalIgnoreCase))
                        {
                            var cachePath = line.Substring(10).Trim();
                            if (!string.IsNullOrEmpty(cachePath) && Directory.Exists(cachePath))
                            {
                                return cachePath;
                            }
                        }
                    }
                }

                var localAppData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
                var jsonConfig = Path.Combine(localAppData, "Tencent", "QQMusic", "config.json");
                if (File.Exists(jsonConfig))
                {
                    var json = File.ReadAllText(jsonConfig);
                    var match = System.Text.RegularExpressions.Regex.Match(json, @"""cachePath""\s*:\s*""([^""]+)""");
                    if (match.Success)
                    {
                        var cachePath = match.Groups[1].Value;
                        if (!string.IsNullOrEmpty(cachePath) && Directory.Exists(cachePath))
                        {
                            return cachePath;
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                LogService.Instance.Warning("[CleanerPage.GetQQMusicConfigCachePath] 执行失败", ex);
            }

            return null;
        }

        private List<string> GetCloudMusicCachePaths()
        {
            var paths = new List<string>();
            var userProfile = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
            var localAppData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
            var appData = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);

            var defaultPaths = new[]
            {
                Path.Combine(localAppData, "NetEase", "CloudMusic", "cache"),
                Path.Combine(localAppData, "NetEase", "CloudMusic", "Cache"),
                Path.Combine(localAppData, "NetEase", "CloudMusic", "temp"),
                Path.Combine(localAppData, "NetEase", "CloudMusic", "Temp"),
                // 内嵌浏览器（CEF）网页缓存：专辑图、页面资源等，删除后自动重建
                Path.Combine(localAppData, "NetEase", "CloudMusic", "webdata"),
                Path.Combine(appData, "NetEase", "CloudMusic", "cache"),
                Path.Combine(appData, "NetEase", "CloudMusic", "Cache"),
                Path.Combine(userProfile, "Music", "NetEase", "CloudMusic", "cache"),
                Path.Combine(userProfile, "Music", "NetEase", "CloudMusic", "Cache"),
                Path.Combine(userProfile, "Documents", "NetEase", "CloudMusic", "cache"),
            };

            foreach (var path in defaultPaths)
            {
                if (Directory.Exists(path))
                {
                    paths.Add(path);
                }
            }

            var installPath = GetInstallPathFromRegistryMultiple(new[]
            {
                @"SOFTWARE\Microsoft\Windows\CurrentVersion\Uninstall\网易云音乐",
                @"SOFTWARE\Microsoft\Windows\CurrentVersion\Uninstall\CloudMusic",
                @"SOFTWARE\NetEase\CloudMusic",
                @"SOFTWARE\WOW6432Node\NetEase\CloudMusic"
            });

            if (!string.IsNullOrEmpty(installPath))
            {
                var regPaths = new[]
                {
                    Path.Combine(installPath, "CloudMusic", "cache"),
                    Path.Combine(installPath, "CloudMusic", "Cache"),
                    Path.Combine(installPath, "cache"),
                    Path.Combine(installPath, "temp"),
                    Path.Combine(installPath, "download")
                };

                foreach (var regPath in regPaths)
                {
                    try
                    {
                        var fullPath = Path.GetFullPath(regPath);
                        if (Directory.Exists(fullPath) && !paths.Contains(fullPath, StringComparer.OrdinalIgnoreCase))
                        {
                            paths.Add(fullPath);
                        }
                    }
                    catch (Exception ex)
                    {
                        LogService.Instance.Warning("[CleanerPage.GetCloudMusicCachePaths] 执行失败", ex);
                    }
                }
            }

            var storePaths = GetMicrosoftStoreAppCachePaths("NetEase.CloudMusic", new[] { "cache", "Cache", "temp", "Temp" });
            foreach (var storePath in storePaths)
            {
                if (!paths.Contains(storePath, StringComparer.OrdinalIgnoreCase))
                {
                    paths.Add(storePath);
                }
            }

            var configCachePath = GetCloudMusicConfigCachePath();
            if (!string.IsNullOrEmpty(configCachePath) && Directory.Exists(configCachePath) && !paths.Contains(configCachePath, StringComparer.OrdinalIgnoreCase))
            {
                paths.Add(configCachePath);
            }

            var allDrivePaths = ScanAllDrivesForMusicAppCache("CloudMusic", new[] { "cache", "Cache", "temp", "Temp", "download" });
            foreach (var drivePath in allDrivePaths)
            {
                if (!paths.Contains(drivePath, StringComparer.OrdinalIgnoreCase))
                {
                    paths.Add(drivePath);
                }
            }

            var appFolderPaths = ScanAllDrivesForCache(new[] { "NetEase", "CloudMusic", "网易云音乐" }, new[] { "cache", "Cache", "temp", "Temp" });
            foreach (var folderPath in appFolderPaths)
            {
                if (!paths.Contains(folderPath, StringComparer.OrdinalIgnoreCase))
                {
                    paths.Add(folderPath);
                }
            }

            return paths;
        }

        private string? GetCloudMusicConfigCachePath()
        {
            try
            {
                var localAppData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
                var configFile = Path.Combine(localAppData, "NetEase", "CloudMusic", "config");

                if (File.Exists(configFile))
                {
                    var content = File.ReadAllText(configFile);
                    var match = System.Text.RegularExpressions.Regex.Match(content, @"cachePath\s*=\s*(.+)");
                    if (match.Success)
                    {
                        var cachePath = match.Groups[1].Value.Trim();
                        if (!string.IsNullOrEmpty(cachePath) && Directory.Exists(cachePath))
                        {
                            return cachePath;
                        }
                    }
                }

                var appData = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
                configFile = Path.Combine(appData, "NetEase", "CloudMusic", "config");
                if (File.Exists(configFile))
                {
                    var content = File.ReadAllText(configFile);
                    var match = System.Text.RegularExpressions.Regex.Match(content, @"cachePath\s*=\s*(.+)");
                    if (match.Success)
                    {
                        var cachePath = match.Groups[1].Value.Trim();
                        if (!string.IsNullOrEmpty(cachePath) && Directory.Exists(cachePath))
                        {
                            return cachePath;
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                LogService.Instance.Warning("[CleanerPage.GetCloudMusicConfigCachePath] 执行失败", ex);
            }

            return null;
        }

        private List<string> GetDouyinCachePaths()
        {
            var paths = new List<string>();

            var defaultPaths = new[]
            {
                Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Douyin", "Cache"),
                Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "Douyin", "Cache"),
                Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "Videos", "Douyin", "Cache")
            };

            foreach (var path in defaultPaths)
            {
                if (Directory.Exists(path))
                {
                    paths.Add(path);
                }
            }

            // 自适应：各盘符根目录下的 Douyin\Cache
            foreach (var drive in DriveInfo.GetDrives())
            {
                try
                {
                    if (!drive.IsReady || (drive.DriveType != DriveType.Fixed && drive.DriveType != DriveType.Removable))
                        continue;
                    var p = Path.Combine(drive.RootDirectory.FullName, "Douyin", "Cache");
                    if (Directory.Exists(p) && !paths.Contains(p, StringComparer.OrdinalIgnoreCase))
                        paths.Add(p);
                }
                catch (Exception ex)
                {
                    LogService.Instance.Warning("[CleanerPage.GetDouyinCachePaths] 枚举盘符失败", ex);
                }
            }

            return paths;
        }

        private List<string> GetKuGouCachePaths()
        {
            var paths = new List<string>();
            var userProfile = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
            var localAppData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
            var appData = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);

            var defaultPaths = new[]
            {
                Path.Combine(localAppData, "KuGou", "Temp"),
                Path.Combine(localAppData, "KuGou", "Cache"),
                Path.Combine(localAppData, "KuGou", "KuGouTemp"),
                Path.Combine(appData, "KuGou", "Temp"),
                Path.Combine(appData, "KuGou", "Cache"),
                Path.Combine(userProfile, "Music", "KuGou", "Temp"),
                Path.Combine(userProfile, "Music", "KuGou", "Cache"),
                Path.Combine(userProfile, "Documents", "KuGou", "Temp"),
            };

            foreach (var path in defaultPaths)
            {
                if (Directory.Exists(path))
                {
                    paths.Add(path);
                }
            }

            var installPath = GetInstallPathFromRegistryMultiple(new[]
            {
                @"SOFTWARE\Microsoft\Windows\CurrentVersion\Uninstall\酷狗音乐",
                @"SOFTWARE\Microsoft\Windows\CurrentVersion\Uninstall\KuGou",
                @"SOFTWARE\KuGou",
                @"SOFTWARE\WOW6432Node\KuGou"
            });

            if (!string.IsNullOrEmpty(installPath))
            {
                var regPaths = new[]
                {
                    Path.Combine(installPath, "KuGou", "Temp"),
                    Path.Combine(installPath, "KuGou", "Cache"),
                    Path.Combine(installPath, "Temp"),
                    Path.Combine(installPath, "Cache"),
                    Path.Combine(installPath, "Download"),
                    Path.Combine(installPath, "Lyric")
                };

                foreach (var regPath in regPaths)
                {
                    try
                    {
                        var fullPath = Path.GetFullPath(regPath);
                        if (Directory.Exists(fullPath) && !paths.Contains(fullPath, StringComparer.OrdinalIgnoreCase))
                        {
                            paths.Add(fullPath);
                        }
                    }
                    catch (Exception ex)
                    {
                        LogService.Instance.Warning("[CleanerPage.GetKuGouCachePaths] 执行失败", ex);
                    }
                }
            }

            var storePaths = GetMicrosoftStoreAppCachePaths("KuGou", new[] { "Temp", "Cache", "KuGouTemp" });
            foreach (var storePath in storePaths)
            {
                if (!paths.Contains(storePath, StringComparer.OrdinalIgnoreCase))
                {
                    paths.Add(storePath);
                }
            }

            var configCachePath = GetKuGouConfigCachePath();
            if (!string.IsNullOrEmpty(configCachePath) && Directory.Exists(configCachePath) && !paths.Contains(configCachePath, StringComparer.OrdinalIgnoreCase))
            {
                paths.Add(configCachePath);
            }

            var allDrivePaths = ScanAllDrivesForMusicAppCache("KuGou", new[] { "Temp", "Cache", "KuGouTemp", "Download", "Lyric" });
            foreach (var drivePath in allDrivePaths)
            {
                if (!paths.Contains(drivePath, StringComparer.OrdinalIgnoreCase))
                {
                    paths.Add(drivePath);
                }
            }

            var appFolderPaths = ScanAllDrivesForCache(new[] { "KuGou", "酷狗", "酷狗音乐" }, new[] { "Temp", "Cache", "KuGouTemp" });
            foreach (var folderPath in appFolderPaths)
            {
                if (!paths.Contains(folderPath, StringComparer.OrdinalIgnoreCase))
                {
                    paths.Add(folderPath);
                }
            }

            return paths;
        }

        private string? GetKuGouConfigCachePath()
        {
            try
            {
                var appData = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
                var configFile = Path.Combine(appData, "KuGou", "config.ini");

                if (File.Exists(configFile))
                {
                    var lines = File.ReadAllLines(configFile);
                    foreach (var line in lines)
                    {
                        if (line.StartsWith("CachePath=", StringComparison.OrdinalIgnoreCase) ||
                            line.StartsWith("TempPath=", StringComparison.OrdinalIgnoreCase))
                        {
                            var cachePath = line.Split('=')[1].Trim();
                            if (!string.IsNullOrEmpty(cachePath) && Directory.Exists(cachePath))
                            {
                                return cachePath;
                            }
                        }
                    }
                }

                var localAppData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
                configFile = Path.Combine(localAppData, "KuGou", "config.ini");
                if (File.Exists(configFile))
                {
                    var lines = File.ReadAllLines(configFile);
                    foreach (var line in lines)
                    {
                        if (line.StartsWith("CachePath=", StringComparison.OrdinalIgnoreCase) ||
                            line.StartsWith("TempPath=", StringComparison.OrdinalIgnoreCase))
                        {
                            var cachePath = line.Split('=')[1].Trim();
                            if (!string.IsNullOrEmpty(cachePath) && Directory.Exists(cachePath))
                            {
                                return cachePath;
                            }
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                LogService.Instance.Warning("[CleanerPage.GetKuGouConfigCachePath] 执行失败", ex);
            }

            return null;
        }

        private string? GetInstallPathFromRegistry(string subKey)
        {
            try
            {
                using (var key = Registry.LocalMachine.OpenSubKey(subKey))
                {
                    if (key != null)
                    {
                        var installPath = key.GetValue("InstallLocation") as string;
                        if (!string.IsNullOrEmpty(installPath) && Directory.Exists(installPath))
                            return installPath;
                    }
                }

                using (var key = Registry.LocalMachine.OpenSubKey(@"SOFTWARE\Wow6432Node\" + subKey))
                {
                    if (key != null)
                    {
                        var installPath = key.GetValue("InstallLocation") as string;
                        if (!string.IsNullOrEmpty(installPath) && Directory.Exists(installPath))
                            return installPath;
                    }
                }

                using (var key = Registry.CurrentUser.OpenSubKey(subKey))
                {
                    if (key != null)
                    {
                        var installPath = key.GetValue("InstallLocation") as string;
                        if (!string.IsNullOrEmpty(installPath) && Directory.Exists(installPath))
                            return installPath;
                    }
                }

                using (var key = Registry.CurrentUser.OpenSubKey(@"SOFTWARE\Wow6432Node\" + subKey))
                {
                    if (key != null)
                    {
                        var installPath = key.GetValue("InstallLocation") as string;
                        if (!string.IsNullOrEmpty(installPath) && Directory.Exists(installPath))
                            return installPath;
                    }
                }
            }
            catch (Exception ex)
            {
                LogService.Instance.Warning("[CleanerPage.GetInstallPathFromRegistry] 执行失败", ex);
            }

            return null;
        }

        private string? GetInstallPathFromRegistryMultiple(string[] subKeys)
        {
            foreach (var subKey in subKeys)
            {
                var path = GetInstallPathFromRegistry(subKey);
                if (!string.IsNullOrEmpty(path))
                    return path;
            }
            return null;
        }

        private List<string> GetMicrosoftStoreAppCachePaths(string packageNamePattern, string[] cacheFolderNames)
        {
            var paths = new List<string>();
            var localAppData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
            var packagesPath = Path.Combine(localAppData, "Packages");

            if (!Directory.Exists(packagesPath))
                return paths;

            try
            {
                foreach (var packageDir in Directory.GetDirectories(packagesPath))
                {
                    var dirName = Path.GetFileName(packageDir);
                    if (dirName.IndexOf(packageNamePattern, StringComparison.OrdinalIgnoreCase) >= 0)
                    {
                        foreach (var cacheFolder in cacheFolderNames)
                        {
                            var cachePath = Path.Combine(packageDir, "LocalCache", cacheFolder);
                            if (Directory.Exists(cachePath))
                            {
                                paths.Add(cachePath);
                            }

                            var localStatePath = Path.Combine(packageDir, "LocalState", cacheFolder);
                            if (Directory.Exists(localStatePath))
                            {
                                paths.Add(localStatePath);
                            }

                            var tempStatePath = Path.Combine(packageDir, "TempState");
                            if (Directory.Exists(tempStatePath))
                            {
                                paths.Add(tempStatePath);
                            }
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                LogService.Instance.Warning("[CleanerPage.GetMicrosoftStoreAppCachePaths] 枚举目录失败", ex);
            }

            return paths;
        }

        private List<string> ScanAllDrivesForCache(string[] appFolderNames, string[] cacheFolderNames)
        {
            var paths = new List<string>();
            var skippedDirs = new SkippedDirs(); // 无权限目录，扫描结束统一汇总，避免逐条刷屏

            try
            {
                var drives = DriveInfo.GetDrives()
                    .Where(d => d.DriveType == DriveType.Fixed || d.DriveType == DriveType.Removable)
                    .Where(d => d.IsReady)
                    .ToList();

                foreach (var drive in drives)
                {
                    try
                    {
                        foreach (var appFolder in appFolderNames)
                        {
                            var appPath = Path.Combine(drive.RootDirectory.FullName, appFolder);
                            if (Directory.Exists(appPath))
                            {
                                foreach (var cacheFolder in cacheFolderNames)
                                {
                                    var cachePath = Path.Combine(appPath, cacheFolder);
                                    if (Directory.Exists(cachePath) && !paths.Contains(cachePath, StringComparer.OrdinalIgnoreCase))
                                    {
                                        paths.Add(cachePath);
                                    }
                                }

                                try
                                {
                                    foreach (var subDir in Directory.GetDirectories(appPath, "*", SearchOption.TopDirectoryOnly))
                                    {
                                        foreach (var cacheFolder in cacheFolderNames)
                                        {
                                            var cachePath = Path.Combine(subDir, cacheFolder);
                                            if (Directory.Exists(cachePath) && !paths.Contains(cachePath, StringComparer.OrdinalIgnoreCase))
                                            {
                                                paths.Add(cachePath);
                                            }
                                        }
                                    }
                                }
                                catch { skippedDirs.Add(appPath); }
                            }
                        }
                    }
                    catch { skippedDirs.Add(drive.RootDirectory.FullName); }
                }
            }
            catch { skippedDirs.Add("(未知)"); }

            if (skippedDirs.Total > 0)
                LogService.Instance.Warning(skippedDirs.Summary("CleanerPage.ScanAllDrivesForCache"));

            return paths;
        }

        private List<string> ScanAllDrivesForMusicAppCache(string appNamePattern, string[] cacheFolderNames)
        {
            var paths = new List<string>();
            var skippedDirs = new SkippedDirs(); // 无权限目录，扫描结束统一汇总

            try
            {
                var drives = DriveInfo.GetDrives()
                    .Where(d => d.DriveType == DriveType.Fixed || d.DriveType == DriveType.Removable)
                    .Where(d => d.IsReady)
                    .ToList();

                foreach (var drive in drives)
                {
                    try
                    {
                        var searchPaths = new[]
                        {
                            drive.RootDirectory.FullName,
                            Path.Combine(drive.RootDirectory.FullName, "Program Files"),
                            Path.Combine(drive.RootDirectory.FullName, "Program Files (x86)"),
                            Path.Combine(drive.RootDirectory.FullName, "Users"),
                            Path.Combine(drive.RootDirectory.FullName, "Music"),
                            Path.Combine(drive.RootDirectory.FullName, "Documents"),
                        };

                        foreach (var searchPath in searchPaths)
                        {
                            if (!Directory.Exists(searchPath))
                                continue;

                            try
                            {
                                ScanDirectoryForAppCache(searchPath, appNamePattern, cacheFolderNames, paths, 0, 3, skippedDirs);
                            }
                            catch { skippedDirs.Add(searchPath); }
                        }
                    }
                    catch { skippedDirs.Add(drive.RootDirectory.FullName); }
                }
            }
            catch { skippedDirs.Add("(未知)"); }

            if (skippedDirs.Total > 0)
                LogService.Instance.Warning(skippedDirs.Summary("CleanerPage.ScanAllDrivesForMusicAppCache"));

            return paths;
        }

        private void ScanDirectoryForAppCache(string directory, string appNamePattern, string[] cacheFolderNames, List<string> foundPaths, int currentDepth, int maxDepth, SkippedDirs skippedDirs)
        {
            if (currentDepth > maxDepth)
                return;

            try
            {
                foreach (var dir in Directory.GetDirectories(directory))
                {
                    try
                    {
                        // 跳过软链接（junction/symlink）：它们多为指向已覆盖目录的兼容性别名
                        if (new DirectoryInfo(dir).Attributes.HasFlag(FileAttributes.ReparsePoint))
                            continue;
                        var dirName = Path.GetFileName(dir);

                        if (dirName.IndexOf(appNamePattern, StringComparison.OrdinalIgnoreCase) >= 0)
                        {
                            foreach (var cacheFolder in cacheFolderNames)
                            {
                                var cachePath = Path.Combine(dir, cacheFolder);
                                if (Directory.Exists(cachePath) && !foundPaths.Contains(cachePath, StringComparer.OrdinalIgnoreCase))
                                {
                                    foundPaths.Add(cachePath);
                                }
                            }

                            try
                            {
                                foreach (var subDir in Directory.GetDirectories(dir, "*", SearchOption.TopDirectoryOnly))
                                {
                                    foreach (var cacheFolder in cacheFolderNames)
                                    {
                                        var cachePath = Path.Combine(subDir, cacheFolder);
                                        if (Directory.Exists(cachePath) && !foundPaths.Contains(cachePath, StringComparer.OrdinalIgnoreCase))
                                        {
                                            foundPaths.Add(cachePath);
                                        }
                                    }
                                }
                            }
                            catch { skippedDirs.Add(dir); }
                        }

                        if (currentDepth < maxDepth)
                        {
                            ScanDirectoryForAppCache(dir, appNamePattern, cacheFolderNames, foundPaths, currentDepth + 1, maxDepth, skippedDirs);
                        }
                    }
                    catch { skippedDirs.Add(dir); }
                }
            }
            catch { skippedDirs.Add(directory); }
        }

        /// <summary>无权限跳过目录的记录器：记总数，名字去重保留前50个防刷屏。</summary>
        private sealed class SkippedDirs
        {
            public int Total;
            public readonly List<string> Names = new();
            public void Add(string dir)
            {
                Total++;
                if (Names.Count < 50 && !Names.Contains(dir, StringComparer.OrdinalIgnoreCase))
                    Names.Add(dir);
            }
            public string Summary(string tag)
            {
                string s = $"[{tag}] 扫描跳过 {Total} 个无权限目录";
                if (Names.Count > 0)
                    s += ": " + string.Join("; ", Names) + (Total > Names.Count ? "; …" : "");
                return s;
            }
        }

        private void KillProcesses(string[] processNames)
        {
            foreach (var processName in processNames)
            {
                try
                {
                    var processes = Process.GetProcessesByName(processName);
                    foreach (var process in processes)
                    {
                        process.Kill();
                        process.WaitForExit(3000);
                    }
                }
                catch (Exception ex)
                {
                    LogService.Instance.Warning("[CleanerPage.KillProcesses] 结束进程失败", ex);
                }
            }
        }

        private (long Size, int FileCount, int DirCount) CleanCachePaths(List<string> paths, string appName)
        {
            long totalSize = 0;
            int totalFiles = 0;
            int totalDirs = 0;

            foreach (var path in paths)
            {
                if (Directory.Exists(path))
                {
                    try
                    {
                        var dirInfo = new DirectoryInfo(path);

                        foreach (var file in dirInfo.GetFiles("*", SearchOption.AllDirectories))
                        {
                            try
                            {
                                totalSize += file.Length;
                                file.Delete();
                                totalFiles++;
                            }
                            catch (Exception ex)
                            {
                                LogService.Instance.Warning("[CleanerPage.KillProcesses] 删除失败", ex);
                            }
                        }

                        foreach (var dir in dirInfo.GetDirectories("*", SearchOption.AllDirectories))
                        {
                            try
                            {
                                dir.Delete(true);
                                totalDirs++;
                            }
                            catch (Exception ex)
                            {
                                LogService.Instance.Warning("[CleanerPage.KillProcesses] 删除失败", ex);
                            }
                        }
                    }
                    catch (Exception ex)
                    {
                        LogService.Instance.Warning("[CleanerPage.KillProcesses] 删除失败", ex);
                    }
                }
            }

            return (totalSize, totalFiles, totalDirs);
        }

        private (long Size, int FileCount, int DirCount) CleanCachePathsDetailed(List<string> paths, string appName)
        {
            long totalSize = 0;
            int totalFiles = 0;
            int totalDirs = 0;

            foreach (var path in paths)
            {
                if (Directory.Exists(path))
                {
                    try
                    {
                        var dirInfo = new DirectoryInfo(path);
                        long pathSize = 0;
                        int pathFiles = 0;
                        int pathDirs = 0;

                        foreach (var file in dirInfo.GetFiles("*", SearchOption.AllDirectories))
                        {
                            try
                            {
                                pathSize += file.Length;
                                file.Delete();
                                pathFiles++;
                            }
                            catch (Exception ex)
                            {
                                LogService.Instance.Warning("[CleanerPage.未知方法] 执行失败", ex);
                            }
                        }

                        foreach (var dir in dirInfo.GetDirectories("*", SearchOption.AllDirectories))
                        {
                            try
                            {
                                dir.Delete(true);
                                pathDirs++;
                            }
                            catch (Exception ex)
                            {
                                LogService.Instance.Warning("[CleanerPage.未知方法] 执行失败", ex);
                            }
                        }

                        if (pathFiles > 0 || pathDirs > 0)
                        {
                            totalSize += pathSize;
                            totalFiles += pathFiles;
                            totalDirs += pathDirs;
                            LogService.Instance.Info($"  {path}: 删除 {pathFiles} 个文件, {pathDirs} 个文件夹, 释放 {FormatSize(pathSize)}");
                        }
                    }
                    catch (Exception ex)
                    {
                        LogService.Instance.Warning("[CleanerPage.未知方法] 执行失败", ex);
                    }
                }
            }

            return (totalSize, totalFiles, totalDirs);
        }

        private async Task ExecuteAsync(string operationName, Func<Task> action)
        {
            if (_isOperating)
            {
                LogService.Instance.Warning("正在执行其他操作，请稍候...");
                return;
            }

            _isOperating = true;
            LogService.Instance.Info($"开始清理{operationName}...");

            try
            {
                await action();
                LogService.Instance.Success($"{operationName}清理已启动");
            }
            catch (Exception ex)
            {
                LogService.Instance.Error($"{operationName}清理失败: {ex.Message}");
            }
            finally
            {
                _isOperating = false;
            }
        }

        private (long Size, int Count, int DirCount) DeleteDirectoryContents(string path)
        {
            long size = 0;
            int fileCount = 0;
            int dirCount = 0;

            try
            {
                var dirInfo = new DirectoryInfo(path);

                Parallel.ForEach(dirInfo.GetFiles("*", SearchOption.AllDirectories), file =>
                {
                    try
                    {
                        Interlocked.Add(ref size, file.Length);
                        file.Delete();
                        Interlocked.Increment(ref fileCount);
                    }
                    catch (Exception ex)
                    {
                        LogService.Instance.Warning("[CleanerPage.ExecuteAsync] 删除失败", ex);
                    }
                });

                foreach (var dir in dirInfo.GetDirectories("*", SearchOption.AllDirectories))
                {
                    try
                    {
                        dir.Delete(true);
                        dirCount++;
                    }
                    catch (Exception ex)
                    {
                        LogService.Instance.Warning("[CleanerPage.ExecuteAsync] 删除失败", ex);
                    }
                }
            }
            catch (Exception ex)
            {
                LogService.Instance.Warning("[CleanerPage.ExecuteAsync] 删除失败", ex);
            }

            return (size, fileCount, dirCount);
        }

        private string FormatSize(long bytes)
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

        private void CleanMyComputerShortcuts_Click(object sender, RoutedEventArgs e)
        {
            var window = new ShortcutCleanerWindow();
            window.Owner = Window.GetWindow(this);
            window.ShowDialog();
        }
    }
}
