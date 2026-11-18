using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace FluentTaskScheduler.ViewModels;

/// <summary>
/// A list of names the user keeps, such as the categories offered when editing a task. The list
/// itself lives elsewhere; this reads and writes it as a whole.
/// </summary>
public sealed partial class NameListViewModel : ObservableObject
{
    private readonly Func<ValueList<string>> _read;
    private readonly Action<ValueList<string>> _write;

    public NameListViewModel(Func<ValueList<string>> read, Action<ValueList<string>> write)
    {
        _read = read;
        _write = write;
        Items = [.. read()];
    }

    public ObservableCollection<string> Items { get; }

    /// <summary>What the user is typing as the next name.</summary>
    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(AddCommand))]
    private string _newName = "";

    /// <summary>Brings <see cref="Items"/> in line with the stored list after that has changed.</summary>
    public void Refresh()
    {
        ValueList<string> stored = _read();
        if (Items.SequenceEqual(stored)) return;

        Items.Clear();
        foreach (string name in stored) Items.Add(name);
        AddCommand.NotifyCanExecuteChanged();
    }

    [RelayCommand(CanExecute = nameof(CanAdd))]
    private void Add()
    {
        _write([.. _read(), NewName.Trim()]);
        NewName = "";
        Refresh();
    }

    // Names differing only in case would be one and the same to the user.
    private bool CanAdd() => NewName.Trim() is { Length: > 0 } name && !Items.Contains(name, StringComparer.OrdinalIgnoreCase);

    [RelayCommand]
    private void Remove(string name)
    {
        _write(_read().Where(existing => existing != name).ToValueList());
        Refresh();
    }
}
