using SimpleInjector;
using Synchro.Desktop;
using Synchro.ViewModels;
using Xunit;

namespace Synchro.Tests;

public class CompositionRootTests
{
    [Fact]
    public void The_container_is_valid_and_resolves_the_main_view_model()
    {
        if (!OperatingSystem.IsMacOS()) return; // credential store is macOS-only until Windows support lands

        using var container = CompositionRoot.Create(Path.Combine(Path.GetTempPath(), "tm-tests-container"));

        container.Verify();

        Assert.NotNull(container.GetInstance<MainWindowViewModel>());
        Assert.NotSame(container.GetInstance<ServerSetupViewModel>(), container.GetInstance<ServerSetupViewModel>());
    }
}
