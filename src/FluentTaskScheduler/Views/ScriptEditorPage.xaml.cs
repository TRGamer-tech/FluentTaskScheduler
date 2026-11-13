using System.Collections.Specialized;
using FluentTaskScheduler.ViewModels;
using Microsoft.UI.Dispatching;
using Microsoft.UI.Input;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Windows.UI.Core;
using VirtualKey = Windows.System.VirtualKey;

namespace FluentTaskScheduler.Views;

public sealed partial class ScriptEditorPage : Page
{
    private bool _scrollQueued;

    public ScriptEditorPage(ScriptEditorViewModel viewModel)
    {
        ViewModel = viewModel;
        InitializeComponent();

        // Never removed: the view model and this page both belong to the window and go together.
        ViewModel.Output.CollectionChanged += Output_CollectionChanged;
        Loaded += (_, _) =>
        {
            ScrollOutputToEnd();
            ScriptText.Focus(FocusState.Programmatic);
        };
    }

    public ScriptEditorViewModel ViewModel { get; }

    private void Output_CollectionChanged(object? sender, NotifyCollectionChangedEventArgs e)
    {
        // Lines arrive in batches; one scroll after the batch is enough.
        if (e.Action != NotifyCollectionChangedAction.Add || _scrollQueued) return;

        _scrollQueued = DispatcherQueue.TryEnqueue(DispatcherQueuePriority.Low, () =>
        {
            _scrollQueued = false;
            ScrollOutputToEnd();
        });
    }

    private void ScrollOutputToEnd()
    {
        if (IsLoaded && ViewModel.Output.Count > 0) OutputList.ScrollIntoView(ViewModel.Output[^1]);
    }

    private void ScriptText_PreviewKeyDown(object sender, KeyRoutedEventArgs e)
    {
        // Tab indents, as in every code editor. Shift+Tab still moves the focus, so the box can be left by keyboard.
        bool shift = InputKeyboardSource.GetKeyStateForCurrentThread(VirtualKey.Shift).HasFlag(CoreVirtualKeyStates.Down);
        if (e.Key != VirtualKey.Tab || shift) return;

        TextEdit edit = ScriptIndentation.Tab(ScriptText.Text, ScriptText.SelectionStart, ScriptText.SelectionLength);
        ScriptText.Select(edit.Start, edit.Length);
        ScriptText.SelectedText = edit.Replacement;
        ScriptText.Select(edit.SelectionStart, edit.SelectionLength);
        e.Handled = true;
    }
}

/// <summary>Picks the template for a line of script output by its kind.</summary>
public sealed class OutputLineTemplateSelector : DataTemplateSelector
{
    public DataTemplate? Output { get; set; }
    public DataTemplate? Error { get; set; }
    public DataTemplate? Note { get; set; }

    protected override DataTemplate? SelectTemplateCore(object item) => (item as OutputLine)?.Kind switch
    {
        OutputKind.Error => Error,
        OutputKind.Note => Note,
        _ => Output
    };

    protected override DataTemplate? SelectTemplateCore(object item, DependencyObject container) => SelectTemplateCore(item);
}
