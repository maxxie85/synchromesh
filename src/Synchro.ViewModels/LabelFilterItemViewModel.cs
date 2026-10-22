using CommunityToolkit.Mvvm.ComponentModel;

namespace Synchro.ViewModels;

/// <summary>A label in the sidebar, with how many torrents carry it. <see cref="Label"/> null is "All labels".</summary>
public sealed partial class LabelFilterItemViewModel(string? label) : ObservableObject
{
    public string? Label { get; } = label;
    public string Title => Label ?? "All labels";
    public bool IsAll => Label is null;

    [ObservableProperty] private int _count;
}
