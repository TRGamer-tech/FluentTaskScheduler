namespace FluentTaskScheduler.Models
{
    public class TaskActionModel : System.ComponentModel.INotifyPropertyChanged
    {
        public event System.ComponentModel.PropertyChangedEventHandler? PropertyChanged;
        private void OnPropertyChanged([System.Runtime.CompilerServices.CallerMemberName] string? propertyName = null)
        {
            PropertyChanged?.Invoke(this, new System.ComponentModel.PropertyChangedEventArgs(propertyName));
        }

        private string _command = "";
        public string Command 
        { 
            get => _command; 
            set { _command = value; OnPropertyChanged(); } 
        }

        private string _arguments = "";
        public string Arguments 
        { 
            get => _arguments; 
            set { _arguments = value; OnPropertyChanged(); } 
        }

        private string _workingDirectory = "";
        public string WorkingDirectory 
        { 
            get => _workingDirectory; 
            set { _workingDirectory = value; OnPropertyChanged(); } 
        }
    }
}
