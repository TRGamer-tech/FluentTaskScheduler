using FluentTaskScheduler.Updates;
using Microsoft.UI.Text;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Automation.Peers;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Documents;
using Microsoft.UI.Xaml.Media;
using Windows.UI.Text;

namespace FluentTaskScheduler.Dialogs;

/// <summary>Turns parsed Markdown into the elements that show it.</summary>
internal static class MarkdownView
{
    private const string FixedWidthFont = "Consolas";

    /// <summary>Adds one element per block to <paramref name="panel"/>, top to bottom.</summary>
    public static void Show(IEnumerable<MarkdownBlock> blocks, Panel panel)
    {
        foreach (MarkdownBlock block in blocks)
        {
            panel.Children.Add(block switch
            {
                MarkdownHeading heading => Heading(heading),
                MarkdownListItem item => ListItem(item),
                MarkdownTable table => Table(table),
                MarkdownCode code => new TextBlock { Text = code.Text, FontFamily = new FontFamily(FixedWidthFont), TextWrapping = TextWrapping.Wrap, IsTextSelectionEnabled = true },
                MarkdownParagraph paragraph => Text(paragraph.Text),
                _ => throw new NotSupportedException($"No view for {block.GetType().Name}.")
            });
        }
    }

    /// <summary>
    /// The address as one the app is willing to open, or null. The text comes from the internet,
    /// so only web addresses qualify; anything else could start a program.
    /// </summary>
    public static Uri? WebAddress(string? address) =>
        Uri.TryCreate(address, UriKind.Absolute, out Uri? uri) && (uri.Scheme == Uri.UriSchemeHttps || uri.Scheme == Uri.UriSchemeHttp) ? uri : null;

    private static TextBlock Heading(MarkdownHeading heading)
    {
        TextBlock text = Text(heading.Text);
        text.FontWeight = FontWeights.SemiBold;
        text.FontSize = heading.Level switch { 1 => 20, 2 => 16, _ => 14 };
        text.Margin = new Thickness(0, 8, 0, 0);
        AutomationProperties.SetHeadingLevel(text, (AutomationHeadingLevel)Math.Clamp(heading.Level, 1, 6));
        return text;
    }

    // Marker and text sit in two columns, so that the lines of a long item start under each other and not under its marker.
    private static Grid ListItem(MarkdownListItem item)
    {
        var row = new Grid { ColumnSpacing = 8, Margin = new Thickness(8 + 20 * item.Depth, 0, 0, 0) };
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });

        row.Children.Add(new TextBlock { Text = item.Number ?? "\u2022" });
        TextBlock text = Text(item.Text);
        Grid.SetColumn(text, 1);
        row.Children.Add(text);
        return row;
    }

    private static Grid Table(MarkdownTable table)
    {
        var grid = new Grid { ColumnSpacing = 16, RowSpacing = 4 };
        for (int column = 0; column < table.Columns; column++)
        {
            // The last column takes what is left and wraps; the others are as wide as their content.
            bool last = column == table.Columns - 1;
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = last ? new GridLength(1, GridUnitType.Star) : GridLength.Auto });
        }

        for (int i = 0; i < table.Cells.Count; i++)
        {
            int row = i / table.Columns;
            int column = i % table.Columns;
            if (column == 0) grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });

            TextBlock cell = Text(table.Cells[i]);
            if (row == 0) cell.FontWeight = FontWeights.SemiBold;
            Grid.SetRow(cell, row);
            Grid.SetColumn(cell, column);
            grid.Children.Add(cell);
        }
        return grid;
    }

    private static TextBlock Text(IEnumerable<MarkdownRun> runs)
    {
        var text = new TextBlock { TextWrapping = TextWrapping.Wrap, IsTextSelectionEnabled = true };
        foreach (MarkdownRun run in runs) text.Inlines.Add(InlineFor(run));
        return text;
    }

    private static Inline InlineFor(MarkdownRun run)
    {
        var text = new Run { Text = run.Text };
        if (run.Bold) text.FontWeight = FontWeights.SemiBold;
        if (run.Italic) text.FontStyle = FontStyle.Italic;
        if (run.Code) text.FontFamily = new FontFamily(FixedWidthFont);
        if (WebAddress(run.Link) is not { } address) return text;

        var link = new Hyperlink { NavigateUri = address };
        link.Inlines.Add(text);
        return link;
    }
}
