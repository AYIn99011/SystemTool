using System.ComponentModel;
using System.Runtime.CompilerServices;

namespace SystemTool.Models
{
    public class CleanItem : INotifyPropertyChanged
    {
        private bool _isSelected;

        public string Name { get; set; } = "";
        public string Description { get; set; } = "";
        public string Group { get; set; } = "";
        public string Warning { get; set; } = "";
        public bool IsSelected
        {
            get => _isSelected;
            set
            {
                if (_isSelected != value)
                {
                    _isSelected = value;
                    OnPropertyChanged();
                }
            }
        }
        public List<string> Paths { get; set; } = new();
        public int Level { get; set; }

        public event PropertyChangedEventHandler? PropertyChanged;

        protected virtual void OnPropertyChanged([CallerMemberName] string? propertyName = null)
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
        }
    }

    public class CleanGroup
    {
        public string Name { get; set; } = "";
        public List<CleanItem> Items { get; set; } = new();
        public bool IsExpanded { get; set; } = true;
    }
}
