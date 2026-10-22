using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace Synchro.ViewModels;

/// <summary>
/// What a text prompt asks for. <paramref name="Validate"/> returns an error to show, or null when the text is fine.
/// <paramref name="Secret"/> hides what's typed and keeps it exactly as typed (no trimming).
/// </summary>
public sealed record TextPrompt(
    string Title,
    string Message,
    string Label,
    string ConfirmText,
    string InitialText = "",
    bool Multiline = false,
    Func<string, string?>? Validate = null,
    bool Secret = false);

/// <summary>A small dialog that asks for one piece of text: a new name, tracker addresses.</summary>
public sealed partial class TextPromptViewModel : ObservableObject
{
    private readonly Func<string, string?>? _validate;
    private readonly string _initialText;

    public TextPromptViewModel(TextPrompt prompt)
    {
        Title = prompt.Title;
        Message = prompt.Message;
        Label = prompt.Label;
        ConfirmText = prompt.ConfirmText;
        Multiline = prompt.Multiline;
        Secret = prompt.Secret;
        _validate = prompt.Validate;
        _initialText = prompt.InitialText;
        _text = prompt.InitialText;
    }

    public event EventHandler<string?>? CloseRequested;

    public string Title { get; }
    public string Message { get; }
    public string Label { get; }
    public string ConfirmText { get; }
    public bool Multiline { get; }
    public bool Secret { get; }

    /// <summary>For the text box: dots for a secret, nothing otherwise.</summary>
    public char PasswordChar => Secret ? '•' : '\0';

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(Error), nameof(HasError))]
    [NotifyCanExecuteChangedFor(nameof(ConfirmCommand))]
    private string _text;

    /// <summary>Shown once something is typed: an empty field is not worth an error.</summary>
    public string? Error => string.IsNullOrWhiteSpace(Text) ? null : _validate?.Invoke(Text);

    public bool HasError => Error is not null;

    private bool CanConfirm() => !string.IsNullOrWhiteSpace(Text) && Error is null && Text != _initialText;

    [RelayCommand(CanExecute = nameof(CanConfirm))]
    private void Confirm() => CloseRequested?.Invoke(this, Multiline || Secret ? Text : Text.Trim());

    [RelayCommand]
    private void Cancel() => CloseRequested?.Invoke(this, null);
}
