using System.ComponentModel;

namespace WebScraper
{
    public class FieldSelectionItem : INotifyPropertyChanged
    {
        private bool _isSelected;

        public string DisplayName { get; set; }
        public string FieldName { get; set; }
        public string DataType { get; set; }
        public bool IsRequired { get; set; }

        public bool IsSelected
        {
            get => _isSelected;
            set
            {
                if (_isSelected != value)
                {
                    _isSelected = value;
                    OnPropertyChanged(nameof(IsSelected));
                }
            }
        }

        public event PropertyChangedEventHandler PropertyChanged;

        protected virtual void OnPropertyChanged(string propertyName)
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
        }
    }
}
