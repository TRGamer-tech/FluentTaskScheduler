using System;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Controls.Primitives;
using FluentTaskScheduler.Models;
using FluentTaskScheduler.Services;
using FluentTaskScheduler.ViewModels;
using Microsoft.Extensions.DependencyInjection;

namespace FluentTaskScheduler
{
    /// <summary>
    /// The combined Library page: ready-to-deploy task templates and reusable command/script
    /// snippets, switched with the segmented buttons at the top.
    /// </summary>
    public sealed partial class ScriptLibraryPage : Page
    {
        public ScriptLibraryViewModel ViewModel { get; } = new();
        public TaskTemplatesViewModel TemplatesViewModel { get; } = new();

        private static string L(string key, string fallback) => LocalizationService.GetString(key, fallback);
        private ISettingsService Settings => App.Container.GetRequiredService<ISettingsService>();

        private MainPage? _ownerMainPage;

        public ScriptLibraryPage()
        {
            this.InitializeComponent();
            LocalizationService.LanguageChanged += LocalizationService_LanguageChanged;
            this.Unloaded += (s, e) => LocalizationService.LanguageChanged -= LocalizationService_LanguageChanged;
            ApplyLocalizedUi();
        }

        private void LocalizationService_LanguageChanged(object? sender, EventArgs e)
        {
            if (DispatcherQueue == null) return;
            DispatcherQueue.TryEnqueue(async () =>
            {
                ApplyLocalizedUi();
                TemplatesViewModel.Load();

                // Built-in script names/descriptions are translated as they are read out of
                // Scripts.json, so the list has to be rebuilt for the new language to show.
                await ViewModel.LoadScriptsAsync(force: true);

                UpdateNoResultsVisibility();
            });
        }

        private void ApplyLocalizedUi()
        {
            LibrarySubtitle.Text = L("Library.Subtitle",
                "Deploy a ready-made scheduled task, or reuse a saved command as the action for a new task. Nothing is created until you save the task editor.");

            SectionTemplatesText.Text = L("Library.Section.Templates", "Task Templates");
            SectionScriptsText.Text = L("Library.Section.Scripts", "Scripts");

            TemplatesInfoBar.Title = L("Templates.Info.Title", "Nothing is created until you save");
            TemplatesInfoBar.Message = L("Templates.Info.Message",
                "Review the command, arguments and trigger in the editor before saving — several templates delete files or change system state, and some need administrator privileges.");

            ScriptLibraryTitle.Text = L("ScriptLibraryTitle.Text", "Saved Scripts");
            CreateTemplateButton.Content = L("ScriptLibraryCreateBtn.Content", "+ New Script");
            ToolTipService.SetToolTip(CreateTemplateButton, L("ScriptLibraryCreateBtn.Content", "+ New Script"));

            CreateTemplateDialog.Title = L("ScriptLibraryNewTemplateDialog.Title", "New Script");
            CreateTemplateDialog.PrimaryButtonText = L("Dialog.Common.Save", "Save");
            CreateTemplateDialog.CloseButtonText = L("Dialog.Common.Cancel", "Cancel");

            TemplateName.Header = L("ScriptLibraryNameHeader.Header", "Name");
            TemplateName.PlaceholderText = L("ScriptLibraryNamePlaceholder.PlaceholderText", "e.g. Cleanup Script");
            TemplateDesc.Header = L("ScriptLibraryDescHeader.Header", "Description");
            TemplateDesc.PlaceholderText = L("ScriptLibraryDescPlaceholder.PlaceholderText", "What does this script do?");
            TemplateCommand.Header = L("ScriptLibraryCommandHeader.Header", "Command");
            TemplateCommand.PlaceholderText = L("ScriptLibraryCommandPlaceholder.PlaceholderText", "e.g. powershell.exe");
            TemplateArgs.Header = L("ScriptLibraryArgsHeader.Header", "Arguments");
            TemplateArgs.PlaceholderText = L("ScriptLibraryArgsPlaceholder.PlaceholderText", "e.g. -File C:\\scripts\\cleanup.ps1");
            TemplateAdmin.Content = L("ScriptLibraryAdminContent.Content", "Run with highest privileges");

            LibrarySearchBox.PlaceholderText = L("Library.Search.Placeholder", "Search...");
            TemplatesNoResultsText.Text = L("Library.Search.NoTemplates", "No templates match your search.");
            ScriptsNoResultsText.Text = L("Library.Search.NoScripts", "No scripts match your search.");
        }

        protected override void OnNavigatedTo(Microsoft.UI.Xaml.Navigation.NavigationEventArgs e)
        {
            base.OnNavigatedTo(e);
            if (e.Parameter is MainPage mp) _ownerMainPage = mp;
        }

        private async void Page_Loaded(object sender, RoutedEventArgs e)
        {
            PageScrollViewer.IsScrollInertiaEnabled = Settings.SmoothScrolling;
            if (TemplatesViewModel.Groups.Count == 0) TemplatesViewModel.Load();
            await ViewModel.LoadScriptsAsync();
            UpdateNoResultsVisibility();
        }

        // ── Section switcher ─────────────────────────────────────────────────────

        private void SectionButton_Click(object sender, RoutedEventArgs e)
        {
            if (sender is not ToggleButton clicked) return;

            bool templates = (clicked.Tag?.ToString() ?? "templates") == "templates";

            // Behave like a segmented control: exactly one section stays selected.
            SectionTemplatesButton.IsChecked = templates;
            SectionScriptsButton.IsChecked = !templates;

            TemplatesSection.Visibility = templates ? Visibility.Visible : Visibility.Collapsed;
            ScriptsSection.Visibility = templates ? Visibility.Collapsed : Visibility.Visible;
            PageScrollViewer.ScrollToVerticalOffset(0);
        }

        // ── Search ───────────────────────────────────────────────────────────────

        private void LibrarySearchBox_TextChanged(AutoSuggestBox sender, AutoSuggestBoxTextChangedEventArgs args)
        {
            // Only react to actual typing, not the programmatic set that follows a suggestion pick.
            if (args.Reason != AutoSuggestionBoxTextChangeReason.UserInput) return;

            string query = LibrarySearchBox.Text;
            TemplatesViewModel.ApplyFilter(query);
            ViewModel.ApplyFilter(query);
            UpdateNoResultsVisibility();
        }

        private void UpdateNoResultsVisibility()
        {
            TemplatesNoResultsText.Visibility = TemplatesViewModel.Groups.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
            ScriptsNoResultsText.Visibility = ViewModel.Scripts.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
        }

        // ── Task templates ───────────────────────────────────────────────────────

        private void DeployTemplate_Click(object sender, RoutedEventArgs e)
        {
            if (sender is not Button button || button.Tag is not TaskTemplate template) return;

            var owner = _ownerMainPage ?? MainPage.Current;
            if (owner == null)
            {
                Serilog.Log.Error("{Message}", $"Cannot deploy template '{template.Id}': no main page is available.");
                return;
            }

            try
            {
                Serilog.Log.Information("{Message}", $"Deploying task template '{template.Id}'.");
                owner.OpenCreateTaskFromTaskTemplate(template);
            }
            catch (Exception ex)
            {
                Serilog.Log.Error(ex, "{Message}", $"Failed to deploy task template '{template.Id}'.");
            }
        }

        // ── Scripts ──────────────────────────────────────────────────────────────

        private void ScheduleButton_Click(object sender, RoutedEventArgs e)
        {
            if (sender is Button btn && btn.Tag is ScriptTemplateModel template)
                (_ownerMainPage ?? MainPage.Current)?.OpenCreateTaskFromTemplate(template);
        }

        private async void CreateTemplateButton_Click(object sender, RoutedEventArgs e)
        {
            TemplateName.Text = "";
            TemplateDesc.Text = "";
            TemplateCommand.Text = "";
            TemplateArgs.Text = "";
            TemplateAdmin.IsChecked = false;

            CreateTemplateDialog.XamlRoot = this.XamlRoot;
            CreateTemplateDialog.RequestedTheme = Settings.Theme;
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
            UpdateNoResultsVisibility();
        }

        private async void DeleteTemplateButton_Click(object sender, RoutedEventArgs e)
        {
            if (sender is not Button btn || btn.Tag is not ScriptTemplateModel template) return;

            var confirm = new ContentDialog
            {
                Title             = L("ScriptLibrary.DeleteTemplate.Title", "Delete Script"),
                Content           = string.Format(L("ScriptLibrary.DeleteTemplate.Content", "Delete '{0}'?"), template.Name),
                PrimaryButtonText = L("Dialog.Delete", "Delete"),
                CloseButtonText   = L("Dialog.Cancel", "Cancel"),
                DefaultButton     = ContentDialogButton.Close,
                XamlRoot          = this.XamlRoot,
                RequestedTheme    = Settings.Theme
            };

            if (await confirm.ShowAsync() == ContentDialogResult.Primary)
            {
                ViewModel.DeleteUserTemplate(template);
                UpdateNoResultsVisibility();
            }
        }
    }
}
