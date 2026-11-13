using FluentTaskScheduler.ViewModels;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Windows.System;

namespace FluentTaskScheduler.Controls;

/// <summary>Edits a list of names: a box to add one, and the names with a button each to remove it.</summary>
public sealed partial class NameListEditor : UserControl
{
    public static readonly DependencyProperty ListProperty =
        DependencyProperty.Register(nameof(List), typeof(NameListViewModel), typeof(NameListEditor), new PropertyMetadata(null));

    public static readonly DependencyProperty PlaceholderProperty =
        DependencyProperty.Register(nameof(Placeholder), typeof(string), typeof(NameListEditor), new PropertyMetadata(""));

    public NameListEditor() => InitializeComponent();

    public NameListViewModel? List
    {
        get => (NameListViewModel?)GetValue(ListProperty);
        set => SetValue(ListProperty, value);
    }

    /// <summary>What the empty box says it is for.</summary>
    public string Placeholder
    {
        get => (string)GetValue(PlaceholderProperty);
        set => SetValue(PlaceholderProperty, value);
    }

    private void NewNameBox_KeyDown(object sender, KeyRoutedEventArgs e)
    {
        if (e.Key != VirtualKey.Enter || List is not { } list || !list.AddCommand.CanExecute(null)) return;
        list.AddCommand.Execute(null);
        e.Handled = true;
    }

    private void Remove_Click(object sender, RoutedEventArgs e)
    {
        if (sender is FrameworkElement { Tag: string name }) List?.RemoveCommand.Execute(name);
    }
}
