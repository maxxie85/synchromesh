using Synchro.ViewModels;
using Xunit;

namespace Synchro.Tests;

public class NameRulesTests
{
    [Theory]
    [InlineData("a/b")]
    [InlineData("a\\b")]
    [InlineData(".")]
    [InlineData(" .. ")]
    public void Names_the_server_would_refuse_are_explained(string name) => Assert.NotNull(NameRules.Validate(name));

    [Theory]
    [InlineData("Mayday (2026).mkv")]
    [InlineData(".hidden")]
    [InlineData("Ünïcødé")]
    public void Ordinary_names_are_fine(string name) => Assert.Null(NameRules.Validate(name));
}
