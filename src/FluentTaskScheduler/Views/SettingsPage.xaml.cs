using FluentTaskScheduler.ViewModels;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

namespace FluentTaskScheduler.Views;

public sealed partial class SettingsPage : Page
{
    public SettingsPage(SettingsViewModel viewModel)
    {
        ViewModel = viewModel;
        InitializeComponent();

        Loaded += (_, _) =>
        {
            TellTheme();
            ViewModel.Refresh();
        };
        ActualThemeChanged += (_, _) => TellTheme();
    }

    public SettingsViewModel ViewModel { get; }

    // Which theme is showing is only known here: with the setting on "follow Windows" it is whatever Windows uses.
    private void TellTheme() => ViewModel.IsDarkTheme = ActualTheme == ElementTheme.Dark;
}
