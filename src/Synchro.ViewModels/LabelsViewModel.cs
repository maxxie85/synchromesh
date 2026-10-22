using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace Synchro.ViewModels;

/// <summary>One label in the Labels dialog: on, off, or mixed (null) when only some of the torrents have it.</summary>
public sealed partial class LabelChoiceViewModel(string name, bool? isChecked) : ObservableObject
{
    public string Name { get; } = name;
    public bool StartedMixed { get; } = isChecked is null;

    [ObservableProperty] private bool? _isChecked = isChecked;
}

/// <summary>
/// The Labels dialog for the selected torrents. Ticking a label gives it to all of them, unticking takes it away from
/// all; a label left mixed stays as it is on each torrent. New labels can be added here too.
/// </summary>
public sealed partial class LabelsViewModel : ObservableObject
{
    private readonly IReadOnlyDictionary<string, IReadOnlyList<string>> _current;

    /// <param name="knownLabels">Every label on the server, offered as choices.</param>
    /// <param name="labelsByHash">The selected torrents and their labels now.</param>
    public LabelsViewModel(IReadOnlyList<string> knownLabels, IReadOnlyDictionary<string, IReadOnlyList<string>> labelsByHash)
    {
        _current = labelsByHash;
        foreach (var name in knownLabels.OrderBy(n => n, StringComparer.CurrentCultureIgnoreCase))
        {
            var having = labelsByHash.Values.Count(l => l.Contains(name));
            Choices.Add(new LabelChoiceViewModel(name, having == 0 ? false : having == labelsByHash.Count ? true : null));
        }

        Message = labelsByHash.Count == 1 ? "Labels for this torrent." : $"Labels for {labelsByHash.Count} torrents.";
    }

    public event EventHandler<IReadOnlyDictionary<string, IReadOnlyList<string>>?>? CloseRequested;

    public string Message { get; }
    public ObservableCollection<LabelChoiceViewModel> Choices { get; } = [];

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(NewLabelError))]
    [NotifyCanExecuteChangedFor(nameof(AddLabelCommand))]
    private string _newLabel = "";

    /// <summary>Transmission stores labels as a comma-separated list, so a comma can't be part of one.</summary>
    public string? NewLabelError => NewLabel.Contains(',') ? "A label can't contain a comma." : null;

    public bool HasChoices => Choices.Count > 0;

    private bool CanAddLabel() => NewLabel.Trim().Length > 0 && NewLabelError is null;

    [RelayCommand(CanExecute = nameof(CanAddLabel))]
    private void AddLabel()
    {
        var name = NewLabel.Trim();
        if (Choices.FirstOrDefault(c => string.Equals(c.Name, name, StringComparison.Ordinal)) is { } existing)
            existing.IsChecked = true;
        else
            Choices.Add(new LabelChoiceViewModel(name, true));

        NewLabel = "";
        OnPropertyChanged(nameof(HasChoices));
    }

    [RelayCommand]
    private void Save()
    {
        if (CanAddLabel()) AddLabel(); // a label typed but not added yet still counts

        var result = new Dictionary<string, IReadOnlyList<string>>();
        foreach (var (hash, labels) in _current)
        {
            var updated = labels.ToList();
            foreach (var choice in Choices)
            {
                if (choice.IsChecked == true && !updated.Contains(choice.Name)) updated.Add(choice.Name);
                if (choice.IsChecked == false) updated.Remove(choice.Name);
            }

            updated.Sort(StringComparer.CurrentCultureIgnoreCase);
            if (!updated.SequenceEqual(labels.OrderBy(l => l, StringComparer.CurrentCultureIgnoreCase)))
                result[hash] = updated;
        }

        CloseRequested?.Invoke(this, result);
    }

    [RelayCommand]
    private void Cancel() => CloseRequested?.Invoke(this, null);
}
