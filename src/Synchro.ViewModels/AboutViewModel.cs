using CommunityToolkit.Mvvm.Input;
using Synchro.ViewModels.Services;

namespace Synchro.ViewModels;

/// <summary>An open-source component the app ships, as listed in About (full texts: THIRD-PARTY-NOTICES.txt).</summary>
public sealed record OpenSourceComponent(string Name, string License, Uri Url);

/// <summary>The About window: name, version, the app's own license, and the open-source components it includes.</summary>
public sealed partial class AboutViewModel(IUrlLauncher launcher, string version)
{
    public const string AppName = "Synchromesh";

    /// <summary>The app's own license (LICENSE at the repository root).</summary>
    public const string AppLicense = "Open source under the MIT License.";

    public const string Copyright = "© 2026 Max Vinken";

    public string Version { get; } = $"Version {version}";

    /// <summary>What a user can see in the app; THIRD-PARTY-NOTICES.txt has every component and its full text.</summary>
    public IReadOnlyList<OpenSourceComponent> Components { get; } =
    [
        new("Avalonia", "MIT", new Uri("https://github.com/AvaloniaUI/Avalonia")),
        new("Avalonia.Controls.DataGrid", "MIT", new Uri("https://github.com/AvaloniaUI/Avalonia.Controls.DataGrid")),
        new("Material.Avalonia", "MIT", new Uri("https://github.com/AvaloniaCommunity/Material.Avalonia")),
        new("Roboto font", "Apache 2.0", new Uri("https://github.com/googlefonts/roboto")),
        new("Material.Icons", "MIT", new Uri("https://github.com/SKProCH/Material.Icons")),
        new("Material Design Icons", "Apache 2.0", new Uri("https://pictogrammers.com/library/mdi/")),
        new("CommunityToolkit.Mvvm", "MIT", new Uri("https://github.com/CommunityToolkit/dotnet")),
        new("Simple Injector", "MIT", new Uri("https://simpleinjector.org")),
        new("SkiaSharp and Skia", "MIT, BSD", new Uri("https://github.com/mono/SkiaSharp")),
        new("HarfBuzz", "MIT", new Uri("https://github.com/harfbuzz/harfbuzz")),
        new(".NET", "MIT", new Uri("https://github.com/dotnet/runtime")),
    ];

    [RelayCommand]
    private Task OpenComponentAsync(OpenSourceComponent component) => launcher.OpenAsync(component.Url);
}
