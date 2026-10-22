using CommunityToolkit.Mvvm.ComponentModel;

namespace Synchro.ViewModels.Details;

/// <summary>One label/value line on the General tab. Rows are created once and their values updated in place.</summary>
public sealed partial class DetailRow(string label, bool wraps = false) : ObservableObject
{
    public string Label { get; } = label;

    /// <summary>True for free text (error, comment, path) that may wrap; other values stay on one line.</summary>
    public bool Wraps { get; } = wraps;

    [ObservableProperty] private string _value = "";

    /// <summary>False hides rows that only matter sometimes (for example the error).</summary>
    [ObservableProperty] private bool _isVisible = true;
}
