using NSubstitute;
using Synchro.Core.Settings;
using Synchro.ViewModels.Services;
using Synchro.ViewModels.Settings;
using Xunit;

namespace Synchro.Tests;

public class AppearanceViewModelTests
{
    private readonly IAppSettingsStore _settings = Substitute.For<IAppSettingsStore>();
    private readonly IThemeService _theme = Substitute.For<IThemeService>();
    private AppSettings _current = new(ThemePreference.Dark);

    public AppearanceViewModelTests()
    {
        _settings.LoadAsync(Arg.Any<CancellationToken>()).Returns(_ => _current);
        _settings.UpdateAsync(Arg.Any<Func<AppSettings, AppSettings>>(), Arg.Any<CancellationToken>())
            .Returns(call => _current = call.Arg<Func<AppSettings, AppSettings>>()(_current));
    }

    [Fact]
    public async Task Shows_the_saved_theme_without_reapplying_it()
    {
        var vm = new AppearanceViewModel(_settings, _theme);

        await vm.LoadAsync();

        Assert.Equal(ThemePreference.Dark, vm.SelectedTheme?.Value);
        _theme.DidNotReceiveWithAnyArgs().Apply(default);
    }

    [Fact]
    public async Task Choosing_a_theme_applies_it_at_once_and_remembers_it()
    {
        var vm = new AppearanceViewModel(_settings, _theme);
        await vm.LoadAsync();

        vm.SelectedTheme = vm.Themes.Single(t => t.Value == ThemePreference.System);

        _theme.Received(1).Apply(ThemePreference.System);
        await TestWait.UntilAsync(() => _current.Theme == ThemePreference.System);
    }

    [Fact]
    public void Offers_system_light_and_dark()
    {
        var vm = new AppearanceViewModel(_settings, _theme);

        Assert.Equal(new[] { ThemePreference.System, ThemePreference.Light, ThemePreference.Dark }, vm.Themes.Select(t => t.Value));
    }
}
