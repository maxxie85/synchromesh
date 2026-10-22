using System.Text.RegularExpressions;
using Xunit;

namespace Synchro.Tests;

/// <summary>CHANGELOG.md keeps the Keep a Changelog shape that build/ci/changelog-notes.sh and the release rely on.</summary>
public partial class ChangelogTests
{
    private static readonly string[] ChangeTypes = ["Added", "Changed", "Deprecated", "Removed", "Fixed", "Security"];

    [GeneratedRegex(@"^## \[(?<version>\d+\.\d+\.\d+(-[0-9A-Za-z.]+)?)\] - (?<date>\d{4}-\d{2}-\d{2})$")]
    private static partial Regex ReleaseHeading();

    private static string[] Lines() => File.ReadAllLines(Path.Combine(RepositoryRoot(), "CHANGELOG.md"));

    [Fact]
    public void Starts_with_an_unreleased_section()
        => Assert.Equal("## [Unreleased]", Lines().First(l => l.StartsWith("## ", StringComparison.Ordinal)));

    [Fact]
    public void Releases_have_a_version_and_a_date_newest_first()
    {
        var releases = Lines().Where(l => l.StartsWith("## ", StringComparison.Ordinal)).Skip(1).ToArray();

        Assert.NotEmpty(releases);
        var dates = releases.Select(heading =>
        {
            var match = ReleaseHeading().Match(heading);
            Assert.True(match.Success, $"Not a release heading: \"{heading}\" (expected \"## [1.2.0] - 2026-01-31\").");
            return DateOnly.ParseExact(match.Groups["date"].Value, "yyyy-MM-dd");
        }).ToArray();
        Assert.Equal(dates.OrderDescending(), dates);
        Assert.Equal(releases.Length, releases.Distinct().Count());
    }

    [Fact]
    public void Changes_are_grouped_by_the_standard_types()
    {
        foreach (var line in Lines().Where(l => l.StartsWith("### ", StringComparison.Ordinal)))
            Assert.Contains(line[4..], ChangeTypes);
    }

    private static string RepositoryRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "Synchromesh.slnx")))
            dir = dir.Parent;
        return dir?.FullName ?? throw new InvalidOperationException("Repository root not found.");
    }
}
