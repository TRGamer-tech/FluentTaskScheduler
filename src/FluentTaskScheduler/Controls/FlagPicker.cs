using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Controls.Primitives;

namespace FluentTaskScheduler.Controls;

/// <summary>One choice of a <see cref="FlagPicker"/>: what it is called and the bit (or bits) it stands for.</summary>
public sealed record FlagOption(string Label, int Value);

/// <summary>
/// A row or grid of toggle buttons that together edit one flags value, such as the weekdays or
/// the days of the month of a trigger.
/// </summary>
public sealed class FlagPicker : UserControl
{
    public static readonly DependencyProperty ValueProperty = DependencyProperty.Register(
        nameof(Value), typeof(int), typeof(FlagPicker), new PropertyMetadata(0, (d, _) => ((FlagPicker)d).ShowValue()));

    public static readonly DependencyProperty OptionsProperty = DependencyProperty.Register(
        nameof(Options), typeof(IReadOnlyList<FlagOption>), typeof(FlagPicker), new PropertyMetadata(null, (d, _) => ((FlagPicker)d).Build()));

    public static readonly DependencyProperty ColumnsProperty = DependencyProperty.Register(
        nameof(Columns), typeof(int), typeof(FlagPicker), new PropertyMetadata(0, (d, _) => ((FlagPicker)d).Build()));

    private readonly Grid _grid = new() { ColumnSpacing = 4, RowSpacing = 4, HorizontalAlignment = HorizontalAlignment.Left };
    private bool _showing;

    public FlagPicker()
    {
        Content = _grid;
        IsTabStop = false;
    }

    /// <summary>The selected options, combined with OR.</summary>
    public int Value
    {
        get => (int)GetValue(ValueProperty);
        set => SetValue(ValueProperty, value);
    }

    public IReadOnlyList<FlagOption>? Options
    {
        get => (IReadOnlyList<FlagOption>?)GetValue(OptionsProperty);
        set => SetValue(OptionsProperty, value);
    }

    /// <summary>How many buttons go in a row; 0 for all in one row.</summary>
    public int Columns
    {
        get => (int)GetValue(ColumnsProperty);
        set => SetValue(ColumnsProperty, value);
    }

    private void Build()
    {
        _grid.Children.Clear();
        _grid.ColumnDefinitions.Clear();
        _grid.RowDefinitions.Clear();
        if (Options is not { Count: > 0 } options) return;

        int columns = Columns > 0 ? Columns : options.Count;
        for (int column = 0; column < columns; column++) _grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        for (int row = 0; row <= (options.Count - 1) / columns; row++) _grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });

        for (int i = 0; i < options.Count; i++)
        {
            var button = new ToggleButton
            {
                Content = options[i].Label,
                Tag = options[i].Value,
                MinWidth = 40,
                Padding = new Thickness(8, 4, 8, 4),
                HorizontalAlignment = HorizontalAlignment.Stretch
            };
            button.Checked += Toggled;
            button.Unchecked += Toggled;
            Grid.SetColumn(button, i % columns);
            Grid.SetRow(button, i / columns);
            _grid.Children.Add(button);
        }
        ShowValue();
    }

    private void ShowValue()
    {
        _showing = true;
        foreach (ToggleButton button in _grid.Children.OfType<ToggleButton>())
        {
            int flag = (int)button.Tag;
            button.IsChecked = (Value & flag) == flag;
        }
        _showing = false;
    }

    private void Toggled(object sender, RoutedEventArgs e)
    {
        // Checking a button from ShowValue is not the user choosing something.
        if (_showing) return;

        var button = (ToggleButton)sender;
        int flag = (int)button.Tag;
        Value = button.IsChecked == true ? Value | flag : Value & ~flag;
    }
}
