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
