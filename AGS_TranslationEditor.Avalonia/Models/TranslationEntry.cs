using System.ComponentModel;
using System.Runtime.CompilerServices;

namespace AGS_TranslationEditor.Models
{
    public class TranslationEntry : INotifyPropertyChanged
    {
        private string _key = string.Empty;
        private string _value = string.Empty;

        public string Key
        {
            get => _key;
            set { _key = value; OnPropertyChanged(); }
        }

        public string Value
        {
            get => _value;
            set { _value = value; OnPropertyChanged(); }
        }

        private string _origin = string.Empty;

        /// <summary>Who says the text and where it comes from (optional context file), e.g. "Rosa · room12.crm".</summary>
        public string Origin
        {
            get => _origin;
            set { _origin = value; OnPropertyChanged(); }
        }

        /// <summary>1-based row number set by the ViewModel after loading.</summary>
        public int RowIndex { get; set; }

        public bool IsUntranslated => string.IsNullOrEmpty(_value);

        public event PropertyChangedEventHandler? PropertyChanged;

        protected void OnPropertyChanged([CallerMemberName] string? propertyName = null)
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
            if (propertyName == nameof(Value))
                PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(IsUntranslated)));
        }
    }
}
