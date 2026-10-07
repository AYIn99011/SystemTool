using System.Collections.ObjectModel;
using System.Windows;
using SystemTool.Models;

namespace SystemTool.Windows
{
    public partial class CleanSelectorWindow : Window
    {
        public ObservableCollection<CleanItem> AllItems { get; set; }
        public List<CleanItem> SelectedItems => AllItems.Where(i => i.IsSelected).ToList();

        public CleanSelectorWindow(List<CleanGroup> groups)
        {
            InitializeComponent();
            AllItems = new ObservableCollection<CleanItem>(groups.SelectMany(g => g.Items));
            DataContext = this;
        }

        private void SelectAll_Click(object sender, RoutedEventArgs e)
        {
            foreach (var item in AllItems)
            {
                item.IsSelected = true;
            }
        }

        private void DeselectAll_Click(object sender, RoutedEventArgs e)
        {
            foreach (var item in AllItems)
            {
                item.IsSelected = false;
            }
        }

        private void StartClean_Click(object sender, RoutedEventArgs e)
        {
            if (SelectedItems.Count == 0)
            {
                MessageBox.Show("请至少选择一个清理项目", "提示", MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }
            DialogResult = true;
            Close();
        }
    }
}
