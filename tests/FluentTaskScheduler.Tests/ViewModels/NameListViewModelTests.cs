using FluentTaskScheduler.ViewModels;

namespace FluentTaskScheduler.Tests.ViewModels;

public class NameListViewModelTests
{
    private ValueList<string> _stored = ["Work", "Personal"];
    private readonly NameListViewModel _list;

    public NameListViewModelTests() => _list = new NameListViewModel(() => _stored, names => _stored = names);

    [Fact]
    public void ItStartsOutWithTheStoredNames()
    {
        Assert.Equal(["Work", "Personal"], _list.Items);
        Assert.False(_list.AddCommand.CanExecute(null));
    }

    [Fact]
    public void Adding_StoresTheTrimmedName_AndEmptiesTheBox()
    {
        _list.NewName = "  Backups ";

        _list.AddCommand.Execute(null);

        Assert.Equal(["Work", "Personal", "Backups"], _stored);
        Assert.Equal(["Work", "Personal", "Backups"], _list.Items);
        Assert.Equal("", _list.NewName);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("Work")]
    [InlineData(" work ")]
    public void ANameThatIsEmptyOrAlreadyThere_CannotBeAdded(string name)
    {
        _list.NewName = name;

        Assert.False(_list.AddCommand.CanExecute(null));
    }

    [Fact]
    public void TheAddButton_FollowsWhatIsTyped()
    {
        int notified = 0;
        _list.AddCommand.CanExecuteChanged += (_, _) => notified++;

        _list.NewName = "Ne";
        _list.NewName = "New";

        Assert.Equal(2, notified);
        Assert.True(_list.AddCommand.CanExecute(null));
    }

    [Fact]
    public void Removing_TakesTheNameOutOfTheStoredList()
    {
        _list.RemoveCommand.Execute("Work");

        Assert.Equal(["Personal"], _stored);
        Assert.Equal(["Personal"], _list.Items);
    }

    [Fact]
    public void ANameThatWasRemoved_CanBeAddedAgain()
    {
        _list.NewName = "Work";
        _list.RemoveCommand.Execute("Work");

        Assert.True(_list.AddCommand.CanExecute(null));
    }

    [Fact]
    public void Refresh_TakesOverAListThatWasChangedElsewhere()
    {
        _stored = ["Maintenance"];

        _list.Refresh();

        Assert.Equal(["Maintenance"], _list.Items);
    }
}
