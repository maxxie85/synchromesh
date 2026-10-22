using Synchro.ViewModels;
using Xunit;

namespace Synchro.Tests;

public class TextPromptViewModelTests
{
    private static TextPromptViewModel Create(string initial = "", bool multiline = false) => new(new TextPrompt(
        "Rename", "", "Name", "Rename", initial, multiline, t => t.Contains('/') ? "No slashes." : null));

    [Fact]
    public void Confirming_returns_the_trimmed_text()
    {
        var vm = Create("old");
        string? result = "unset";
        vm.CloseRequested += (_, text) => result = text;

        vm.Text = "  new name ";
        vm.ConfirmCommand.Execute(null);

        Assert.Equal("new name", result);
    }

    [Fact]
    public void Empty_unchanged_or_invalid_text_cannot_be_confirmed()
    {
        var vm = Create("old");
        Assert.False(vm.ConfirmCommand.CanExecute(null)); // unchanged

        vm.Text = " ";
        Assert.False(vm.ConfirmCommand.CanExecute(null));
        Assert.False(vm.HasError); // an empty field shows no error

        vm.Text = "a/b";
        Assert.False(vm.ConfirmCommand.CanExecute(null));
        Assert.Equal("No slashes.", vm.Error);

        vm.Text = "ab";
        Assert.True(vm.ConfirmCommand.CanExecute(null));
        Assert.False(vm.HasError);
    }

    [Fact]
    public void Cancelling_returns_null()
    {
        var vm = Create("old");
        string? result = "unset";
        vm.CloseRequested += (_, text) => result = text;

        vm.CancelCommand.Execute(null);

        Assert.Null(result);
    }

    [Fact]
    public void A_secret_is_hidden_and_kept_exactly_as_typed()
    {
        var vm = new TextPromptViewModel(new TextPrompt("Unlock", "", "Master password", "Unlock", Secret: true)) { Text = " pass phrase " };
        string? result = null;
        vm.CloseRequested += (_, r) => result = r;

        vm.ConfirmCommand.Execute(null);

        Assert.Equal('•', vm.PasswordChar);
        Assert.Equal(" pass phrase ", result);
    }
}
