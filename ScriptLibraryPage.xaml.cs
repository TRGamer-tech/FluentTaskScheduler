using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using FluentTaskScheduler.ViewModels;

namespace FluentTaskScheduler
{
    public sealed partial class ScriptLibraryPage : Page
    {
        public ScriptLibraryViewModel ViewModel { get; } = new();
        private static string L(string key, string fallback) => Services.LocalizationService.GetString(key, fallback);

        private MainPage? _ownerMainPage;

        public ScriptLibraryPage()
        {
            this.InitializeComponent();
        }

        protected override void OnNavigatedTo(Microsoft.UI.Xaml.Navigation.NavigationEventArgs e)
        {
            base.OnNavigatedTo(e);
            if (e.Parameter is MainPage mp) _ownerMainPage = mp;
        }

        private async void Page_Loaded(object sender, RoutedEventArgs e)
        {
            PageScrollViewer.IsScrollInertiaEnabled = FluentTaskScheduler.Services.SettingsService.SmoothScrolling;
            await ViewModel.LoadScriptsAsync();
        }

        private void ScheduleButton_Click(object sender, RoutedEventArgs e)
        {
            if (sender is Button btn && btn.Tag is ScriptTemplateModel template)
                (_ownerMainPage ?? MainPage.Current)?.OpenCreateTaskFromTemplate(template);
        }

        // ── Custom templates ─────────────────────────────────────────────────────

        private async void CreateTemplateButton_Click(object sender, RoutedEventArgs e)
        {
            TemplateName.Text = "";
            TemplateDesc.Text = "";
            TemplateCommand.Text = "";
            TemplateArgs.Text = "";
            TemplateAdmin.IsChecked = false;

            CreateTemplateDialog.XamlRoot = this.XamlRoot;
            await CreateTemplateDialog.ShowAsync();
        }

        private void CreateTemplateDialog_PrimaryButtonClick(ContentDialog sender, ContentDialogButtonClickEventArgs args)
        {
            if (string.IsNullOrWhiteSpace(TemplateName.Text)) { args.Cancel = true; return; }

            ViewModel.AddUserTemplate(new ScriptTemplateModel
            {
                Name        = TemplateName.Text.Trim(),
                Description = TemplateDesc.Text.Trim(),
                Command     = TemplateCommand.Text.Trim(),
                Arguments   = TemplateArgs.Text.Trim(),
                RunAsAdmin  = TemplateAdmin.IsChecked == true
            });
        }

        private async void DeleteTemplateButton_Click(object sender, RoutedEventArgs e)
        {
            if (sender is not Button btn || btn.Tag is not ScriptTemplateModel template) return;

            var confirm = new ContentDialog
            {
                Title           = L("ScriptLibrary.DeleteTemplate.Title", "Delete Template"),
                Content         = string.Format(L("ScriptLibrary.DeleteTemplate.Content", "Delete '{0}'?"), template.Name),
                PrimaryButtonText = L("Dialog.Delete", "Delete"),
                CloseButtonText = L("Dialog.Cancel", "Cancel"),
                DefaultButton   = ContentDialogButton.Close,
                XamlRoot        = this.XamlRoot
            };

            if (await confirm.ShowAsync() == ContentDialogResult.Primary)
                ViewModel.DeleteUserTemplate(template);
        }
    }
}
