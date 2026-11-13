using System.Text.Json;

namespace FluentTaskScheduler.Tests;

public class ValueListTests
{
    private sealed record Holder(string Name, ValueList<string> Items);

    [Fact]
    public void ListsWithEqualItems_AreEqual_AndSoAreRecordsHoldingThem()
    {
        ValueList<string> a = ["x", "y"];
        ValueList<string> b = new[] { "x", "y" };
        ValueList<string> c = new List<string> { "x", "y" };

        Assert.Equal(a, b);
        Assert.Equal(b, c);
        Assert.Equal(a.GetHashCode(), c.GetHashCode());
        Assert.Equal(new Holder("n", a), new Holder("n", c));
        Assert.True(new Holder("n", a) == new Holder("n", b));
    }

    [Fact]
    public void OrderAndContentMatter()
    {
        ValueList<string> list = ["x", "y"];

        Assert.NotEqual<ValueList<string>>(["y", "x"], list);
        Assert.NotEqual<ValueList<string>>(["x"], list);
        Assert.NotEqual<ValueList<string>>(["x", "Y"], list);
    }

    [Fact]
    public void ALaterChangeToTheSourceArray_DoesNotReachTheList()
    {
        var source = new[] { "x", "y" };
        ValueList<string> list = source;

        source[0] = "changed";

        Assert.Equal("x", list[0]);
    }

    [Fact]
    public void Spread_BuildsANewListFromAnExistingOne()
    {
        ValueList<int> list = [1, 2];

        ValueList<int> longer = [.. list, 3];

        Assert.Equal<IEnumerable<int>>([1, 2, 3], longer);
        Assert.Equal(2, list.Count);
        Assert.Same(ValueList<int>.Empty, list.Where(_ => false).ToValueList());
    }

    [Fact]
    public void ItIsStoredAsAPlainJsonArray()
    {
        var holder = new Holder("n", ["x", "y"]);

        string json = JsonSerializer.Serialize(holder);

        Assert.Equal("""{"Name":"n","Items":["x","y"]}""", json);
        Assert.Equal(holder, JsonSerializer.Deserialize<Holder>(json));
        Assert.Empty(JsonSerializer.Deserialize<Holder>("""{"Name":"n","Items":[]}""")!.Items);
    }
}
