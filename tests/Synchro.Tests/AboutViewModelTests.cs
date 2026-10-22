using NSubstitute;
using Synchro.ViewModels;
using Synchro.ViewModels.Services;
using Xunit;

namespace Synchro.Tests;

public class AboutViewModelTests
{
    [Fact]
    public async Task Shows_the_version_and_opens_a_components_page()
    {
        var launcher = Substitute.For<IUrlLauncher>();
        var vm = new AboutViewModel(launcher, "1.2.0");

        await vm.OpenComponentCommand.ExecuteAsync(vm.Components[0]);

        Assert.Equal("Version 1.2.0", vm.Version);
        await launcher.Received(1).OpenAsync(new Uri("https://github.com/AvaloniaUI/Avalonia"));
    }

    [Fact]
    public void Every_listed_component_is_in_the_third_party_notices()
    {
        var notices = File.ReadAllText(Path.Combine(RepositoryRoot(), "THIRD-PARTY-NOTICES.txt"));

        foreach (var component in new AboutViewModel(Substitute.For<IUrlLauncher>(), "1").Components)
            Assert.Contains(component.Url.ToString().TrimEnd('/'), notices);
    }

    [Fact]
    public void The_app_is_mit_licensed_and_about_says_so()
    {
        var license = File.ReadAllText(Path.Combine(RepositoryRoot(), "LICENSE"));

        Assert.StartsWith("MIT License", license);
        Assert.Contains("MIT", AboutViewModel.AppLicense);
    }

    private static string RepositoryRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "Synchromesh.slnx")))
            dir = dir.Parent;
        return dir?.FullName ?? throw new InvalidOperationException("Repository root not found.");
    }
}
