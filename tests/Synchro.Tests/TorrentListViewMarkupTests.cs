using System.Xml.Linq;
using Synchro.Core.Settings;
using Synchro.ViewModels.Details;
using Xunit;

namespace Synchro.Tests;

/// <summary>
/// The torrent grid's columns, read from the XAML. A DataGrid template column only raises Sorting (our header click)
/// when it has a SortMemberPath; without one the header click silently does nothing.
/// </summary>
public class TorrentListViewMarkupTests
{
    private static readonly XNamespace Avalonia = "https://github.com/avaloniaui";

    private static XElement[] Columns(string view = "TorrentListView")
    {
        var path = Path.Combine(RepositoryRoot(), $"src/Synchro.Desktop/Views/{view}.axaml");
        return XDocument.Load(path).Descendants(Avalonia + "DataGridTemplateColumn").ToArray();
    }

    [Theory]
    [InlineData("TorrentListView")]
    [InlineData("TorrentDetailsView")]
    [InlineData("FileTreeView")]
    public void Every_sortable_column_can_be_clicked_to_sort(string view)
    {
        // Columns without a key (the remove button, the lock icon) don't sort.
        foreach (var column in Columns(view).Where(c => c.Attribute("Tag") is not null))
            Assert.False(string.IsNullOrEmpty((string?)column.Attribute("SortMemberPath")),
                $"{view}: column {(string?)column.Attribute("Tag")} needs a SortMemberPath, or its header click never sorts.");
    }

    [Fact]
    public void Every_detail_column_key_is_one_the_view_models_sort_by()
    {
        string[] keys = [.. typeof(TrackerItemViewModel.Columns).GetFields().Select(f => (string)f.GetValue(null)!),
            .. typeof(PeerItemViewModel.Columns).GetFields().Select(f => (string)f.GetValue(null)!),
            .. typeof(FilesViewModel.Columns).GetFields().Select(f => (string)f.GetValue(null)!)];

        foreach (var view in new[] { "TorrentDetailsView", "FileTreeView" })
            foreach (var tag in Columns(view).Select(c => (string?)c.Attribute("Tag")).OfType<string>())
                Assert.Contains(tag, keys);
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
