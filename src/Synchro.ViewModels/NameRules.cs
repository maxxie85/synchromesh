namespace Synchro.ViewModels;

/// <summary>What the server accepts as a new name for a torrent, file or folder.</summary>
public static class NameRules
{
    /// <summary>An error to show, or null when the name is fine.</summary>
    public static string? Validate(string name)
    {
        var trimmed = name.Trim();
        if (trimmed.Contains('/') || trimmed.Contains('\\'))
            return "A name can't contain / or \\. To move files, use Move data.";
        if (trimmed is "." or "..")
            return "Choose a name other than . or ..";
        return null;
    }

    public static TextPrompt RenamePrompt(string what, string currentName) => new(
        $"Rename {what}",
        what == "torrent"
            ? "This also renames the torrent's file or folder on the server."
            : $"This also renames the {what} on the server.",
        "New name",
        "Rename",
        currentName,
        Validate: Validate);
}
