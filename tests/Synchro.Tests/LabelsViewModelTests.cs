using Synchro.ViewModels;
using Xunit;

namespace Synchro.Tests;

public class LabelsViewModelTests
{
    private static LabelsViewModel Create(params (string Hash, string[] Labels)[] torrents) =>
        new(["iso", "linux", "movies"], torrents.ToDictionary(t => t.Hash, t => (IReadOnlyList<string>)t.Labels));

    private static IReadOnlyDictionary<string, IReadOnlyList<string>>? Save(LabelsViewModel vm)
    {
        IReadOnlyDictionary<string, IReadOnlyList<string>>? result = null;
        vm.CloseRequested += (_, r) => result = r;
        vm.SaveCommand.Execute(null);
        return result;
    }

    [Fact]
    public void Each_label_starts_on_off_or_mixed_for_the_selection()
    {
        var vm = Create(("a", ["linux", "iso"]), ("b", ["linux"]));

        Assert.Equal(new bool?[] { null, true, false }, vm.Choices.Select(c => c.IsChecked));
        Assert.True(vm.Choices[0].StartedMixed);
    }

    [Fact]
    public void Ticking_adds_to_all_unticking_removes_from_all_and_mixed_stays_as_it_is()
    {
        var vm = Create(("a", ["linux", "iso"]), ("b", ["linux"]));
        vm.Choices.Single(c => c.Name == "movies").IsChecked = true;
        vm.Choices.Single(c => c.Name == "linux").IsChecked = false;

        var result = Save(vm)!;

        Assert.Equal(new[] { "iso", "movies" }, result["a"]); // iso was mixed: kept on a
        Assert.Equal(new[] { "movies" }, result["b"]);        // and not added to b
    }

    [Fact]
    public void A_new_label_is_added_and_a_typed_one_counts_on_save()
    {
        var vm = Create(("a", []));
        vm.NewLabel = " 4k ";
        vm.AddLabelCommand.Execute(null);
        vm.NewLabel = "hdr"; // typed but not added with the button

        var result = Save(vm)!;

        Assert.Equal(new[] { "4k", "hdr" }, result["a"]);
    }

    [Fact]
    public void Torrents_whose_labels_did_not_change_are_left_out()
    {
        var vm = Create(("a", ["linux"]), ("b", []));
        vm.Choices.Single(c => c.Name == "linux").IsChecked = true; // a already has it; b gets it

        var result = Save(vm)!;

        Assert.Equal(new[] { "b" }, result.Keys);
    }

    [Fact]
    public void A_comma_is_not_allowed_in_a_label()
    {
        var vm = Create(("a", []));

        vm.NewLabel = "a,b";

        Assert.NotNull(vm.NewLabelError);
        Assert.False(vm.AddLabelCommand.CanExecute(null));
    }
}
