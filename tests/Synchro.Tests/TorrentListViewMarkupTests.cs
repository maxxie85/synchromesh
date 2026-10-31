using System.Xml.Linq;
using Synchro.Core.Settings;
using Xunit;

namespace Synchro.Tests;

/// <summary>
/// The torrent grid's columns, read from the XAML. A DataGrid template column only raises Sorting (our header click)
/// when it has a SortMemberPath; without one the header click silently does nothing.
/// </summary>
public class TorrentListViewMarkupTests
{
    private static readonly XNamespace Avalonia = "https://github.com/avaloniaui";

    private static XElement[] Columns()
    {
        var path = Path.Combine(RepositoryRoot(), "src/Synchro.Desktop/Views/TorrentListView.axaml");
        return XDocument.Load(path).Descendants(Avalonia + "DataGridTemplateColumn").ToArray();
    }

    [Fact]
    public void Every_column_can_be_clicked_to_sort()
    {
        foreach (var column in Columns())
            Assert.False(string.IsNullOrEmpty((string?)column.Attribute("SortMemberPath")),
                $"Column {(string?)column.Attribute("Tag")} needs a SortMemberPath, or its header click never sorts.");
    }

    [Fact]
    public void Every_sort_column_has_a_grid_column()
    {
        var tags = Columns().Select(c => ((string?)c.Attribute("Tag") ?? "").Split('.').Last().TrimEnd('}')).ToHashSet();

        foreach (var column in Enum.GetNames<TorrentSortColumn>())
            Assert.Contains(column, tags);
    }

    private static string RepositoryRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "Synchromesh.slnx")))
            dir = dir.Parent;
        return dir?.FullName ?? throw new InvalidOperationException("Repository root not found.");
    }
}
