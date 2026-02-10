using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using FluentTaskScheduler.ViewModels;

namespace FluentTaskScheduler
{
    public sealed partial class ScriptLibraryPage : Page
    {
        public ScriptLibraryViewModel ViewModel { get; } = new();

        public ScriptLibraryPage()
        {
            this.InitializeComponent();
        }

        private async void Page_Loaded(object sender, RoutedEventArgs e)
        {
            await ViewModel.LoadScriptsAsync();
        }

        private void ScheduleButton_Click(object sender, RoutedEventArgs e)
        {
             if (sender is Button btn && btn.Tag is ScriptTemplateModel template)
             {
                 MainPage.Current?.OpenCreateTaskFromTemplate(template);
             }
        }
    }
}
