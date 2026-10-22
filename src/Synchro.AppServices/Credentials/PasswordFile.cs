using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Synchro.Core.Servers;

namespace Synchro.AppServices.Credentials;

/// <summary>
/// Server passwords in <c>passwords.json</c>, each encrypted with AES-256-GCM under a random 256-bit key. The server
/// profile's id is the associated data, so an entry copied onto another profile doesn't decrypt. The key is in
/// <c>passwords.key</c>: the 32 bytes as they are, or, with a master password, a small JSON document holding the key
/// encrypted (AES-256-GCM) under a key derived from the master password with PBKDF2-SHA256. Changing the master
/// password re-encrypts only that key. Both files are readable by the user only (mode 600). See
/// <see cref="IPasswordFile"/> for what each variant protects against.
/// </summary>
public sealed class PasswordFile : IPasswordFile, ICredentialStore
{
    private const int KeySize = 32;
    private const int NonceSize = 12; // AesGcm.NonceByteSizes.MaxSize
    private const int TagSize = 16;

    // OWASP's current advice for PBKDF2-HMAC-SHA256. Stored with the key, so it can be raised later.
    internal const int DefaultIterations = 600_000;
    private const int SaltSize = 16;
    private static readonly byte[] KeyAssociatedData = "synchromesh-password-key"u8.ToArray();

    private static readonly JsonSerializerOptions Options = new() { WriteIndented = true };

    private readonly string _directory;
    private readonly string _keyPath;
    private readonly string _filePath;
    private readonly IMasterPasswordPrompt? _prompt;
    private readonly int _iterations;
    private readonly SemaphoreSlim _gate = new(1, 1);
    private byte[]? _key; // once read (or unlocked), for the rest of the run

    /// <param name="directory">The app's data folder; the key and the passwords go next to the other settings.</param>
    /// <param name="prompt">Asks for the master password; without one a protected file stays locked.</param>
    public PasswordFile(string directory, IMasterPasswordPrompt? prompt = null) : this(directory, prompt, DefaultIterations) { }

    internal PasswordFile(string directory, IMasterPasswordPrompt? prompt, int iterations)
    {
        _directory = directory;
        _keyPath = Path.Combine(directory, "passwords.key");
        _filePath = Path.Combine(directory, "passwords.json");
        _prompt = prompt;
        _iterations = iterations;
    }

    public string Name => "Synchromesh's encrypted password file";

    public bool IsEnabled => File.Exists(_keyPath);

    public bool HasMasterPassword => File.Exists(_keyPath) && new FileInfo(_keyPath).Length != KeySize;

    public void Enable()
    {
        if (IsEnabled) return;

        Directory.CreateDirectory(Path.GetDirectoryName(_keyPath)!);
        try
        {
            using var stream = new FileStream(_keyPath, PrivateFile(FileMode.CreateNew));
            stream.Write(RandomNumberGenerator.GetBytes(KeySize));
        }
        catch (IOException) when (IsEnabled)
        {
            // Created at the same moment elsewhere: that key is as good as ours.
        }
    }

    /// <summary>Whether this file holds a password for the profile (it then wins over the keyring).</summary>
    public async Task<bool> ContainsAsync(Guid profileId, CancellationToken ct = default)
    {
        await _gate.WaitAsync(ct).ConfigureAwait(false);
        try { return (await ReadAsync(ct).ConfigureAwait(false)).ContainsKey(Id(profileId)); }
        finally { _gate.Release(); }
    }

    /// <summary>The password, or null when there's none, the key is gone or the entry doesn't decrypt.</summary>
    /// <exception cref="PasswordFileLockedException">It has a master password and the user didn't enter it.</exception>
    public async Task<string?> GetPasswordAsync(Guid profileId, CancellationToken ct = default)
    {
        await _gate.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            var entries = await ReadAsync(ct).ConfigureAwait(false);
            if (!entries.TryGetValue(Id(profileId), out var sealedText) || await ReadKeyAsync(ct).ConfigureAwait(false) is not { } key)
                return null;
            return Open(key, profileId, sealedText);
        }
        finally { _gate.Release(); }
    }

    public async Task SetPasswordAsync(Guid profileId, string password, CancellationToken ct = default)
    {
        await _gate.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            var key = await ReadKeyAsync(ct).ConfigureAwait(false)
                ?? throw new InvalidOperationException("The password file isn't set up on this computer.");
            var entries = await ReadAsync(ct).ConfigureAwait(false);
            entries[Id(profileId)] = Seal(key, profileId, password);
            await WriteAsync(entries, ct).ConfigureAwait(false);
        }
        finally { _gate.Release(); }
    }

    public async Task DeleteAsync(Guid profileId, CancellationToken ct = default)
    {
        await _gate.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            var entries = await ReadAsync(ct).ConfigureAwait(false);
            if (entries.Remove(Id(profileId)))
                await WriteAsync(entries, ct).ConfigureAwait(false);
        }
        finally { _gate.Release(); }
    }

    private static string Id(Guid profileId) => profileId.ToString("D");

    // nonce | ciphertext | tag, base64.
    private static string Seal(byte[] key, Guid profileId, string password)
    {
        var plain = Encoding.UTF8.GetBytes(password);
        var output = new byte[NonceSize + plain.Length + TagSize];
        var nonce = output.AsSpan(0, NonceSize);
        RandomNumberGenerator.Fill(nonce);
        using var aes = new AesGcm(key, TagSize);
        aes.Encrypt(nonce, plain, output.AsSpan(NonceSize, plain.Length), output.AsSpan(NonceSize + plain.Length),
            Encoding.UTF8.GetBytes(Id(profileId)));
        CryptographicOperations.ZeroMemory(plain);
        return Convert.ToBase64String(output);
    }

    private static string? Open(byte[] key, Guid profileId, string sealedText)
    {
        try
        {
            var input = Convert.FromBase64String(sealedText);
            if (input.Length < NonceSize + TagSize) return null;
            var plain = new byte[input.Length - NonceSize - TagSize];
            using var aes = new AesGcm(key, TagSize);
            aes.Decrypt(input.AsSpan(0, NonceSize), input.AsSpan(NonceSize, plain.Length),
                input.AsSpan(NonceSize + plain.Length), plain, Encoding.UTF8.GetBytes(Id(profileId)));
            var password = Encoding.UTF8.GetString(plain);
            CryptographicOperations.ZeroMemory(plain);
            return password;
        }
        catch (Exception ex) when (ex is FormatException or CryptographicException)
        {
            // Damaged, or sealed with a key that has since been replaced: as good as no password.
            return null;
        }
    }

    public async Task SetMasterPasswordAsync(string? masterPassword, CancellationToken ct = default)
    {
        await _gate.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            var key = await ReadKeyAsync(ct).ConfigureAwait(false)
                ?? throw new InvalidOperationException("The password file isn't set up on this computer.");
            var contents = masterPassword is null
                ? key
                : JsonSerializer.SerializeToUtf8Bytes(await Task.Run(() => Wrap(key, masterPassword, _iterations), ct).ConfigureAwait(false), Options);
            await WritePrivateAsync(_keyPath, contents, ct).ConfigureAwait(false);
        }
        finally { _gate.Release(); }
    }

    /// <summary>The key, asking for the master password when it's protected; null when there is no key file.</summary>
    private async Task<byte[]?> ReadKeyAsync(CancellationToken ct)
    {
        if (_key is not null && File.Exists(_keyPath)) return _key;
        _key = null;

        byte[] contents;
        try
        {
            contents = await File.ReadAllBytesAsync(_keyPath, ct).ConfigureAwait(false);
        }
        catch (Exception ex) when (ex is FileNotFoundException or DirectoryNotFoundException)
        {
            return null;
        }

        if (contents.Length == KeySize) return _key = contents;

        WrappedKey wrapped;
        try
        {
            wrapped = JsonSerializer.Deserialize<WrappedKey>(contents, Options) ?? throw new JsonException();
        }
        catch (JsonException)
        {
            return null; // damaged: as good as no key
        }

        string? problem = null;
        while (true)
        {
            var masterPassword = _prompt is null ? null
                : await _prompt.AskAsync(problem, _directory, ct).ConfigureAwait(false);
            if (masterPassword is null) throw new PasswordFileLockedException();

            if (await Task.Run(() => Unwrap(wrapped, masterPassword), ct).ConfigureAwait(false) is { } key)
                return _key = key;
            problem = "That isn't the master password.";
        }
    }

    private static WrappedKey Wrap(byte[] key, string masterPassword, int iterations)
    {
        var salt = RandomNumberGenerator.GetBytes(SaltSize);
        var wrapping = Derive(masterPassword, salt, iterations);
        var output = new byte[NonceSize + KeySize + TagSize];
        RandomNumberGenerator.Fill(output.AsSpan(0, NonceSize));
        using (var aes = new AesGcm(wrapping, TagSize))
            aes.Encrypt(output.AsSpan(0, NonceSize), key, output.AsSpan(NonceSize, KeySize), output.AsSpan(NonceSize + KeySize), KeyAssociatedData);
        CryptographicOperations.ZeroMemory(wrapping);
        return new WrappedKey(1, "pbkdf2-sha256", iterations, Convert.ToBase64String(salt), Convert.ToBase64String(output));
    }

    // Null for a wrong master password: the GCM tag doesn't match.
    private static byte[]? Unwrap(WrappedKey wrapped, string masterPassword)
    {
        try
        {
            var input = Convert.FromBase64String(wrapped.Key);
            // The iteration cap keeps a damaged file from hanging the app for hours.
            if (wrapped.Kdf != "pbkdf2-sha256" || wrapped.Iterations is < 1 or > 10_000_000
                || input.Length != NonceSize + KeySize + TagSize) return null;
            var wrapping = Derive(masterPassword, Convert.FromBase64String(wrapped.Salt), wrapped.Iterations);
            var key = new byte[KeySize];
            using (var aes = new AesGcm(wrapping, TagSize))
                aes.Decrypt(input.AsSpan(0, NonceSize), input.AsSpan(NonceSize, KeySize), input.AsSpan(NonceSize + KeySize), key, KeyAssociatedData);
            CryptographicOperations.ZeroMemory(wrapping);
            return key;
        }
        catch (Exception ex) when (ex is FormatException or CryptographicException)
        {
            return null;
        }
    }

    private static byte[] Derive(string masterPassword, byte[] salt, int iterations)
        => Rfc2898DeriveBytes.Pbkdf2(masterPassword, salt, iterations, HashAlgorithmName.SHA256, KeySize);

    private async Task<Dictionary<string, string>> ReadAsync(CancellationToken ct)
    {
        if (!File.Exists(_filePath)) return [];
        try
        {
            await using var stream = File.OpenRead(_filePath);
            return (await JsonSerializer.DeserializeAsync<Contents>(stream, Options, ct).ConfigureAwait(false))?.Passwords ?? [];
        }
        catch (JsonException)
        {
            return [];
        }
    }

    private Task WriteAsync(Dictionary<string, string> entries, CancellationToken ct)
        => WritePrivateAsync(_filePath, JsonSerializer.SerializeToUtf8Bytes(new Contents(1, entries), Options), ct);

    // Atomic: a crash leaves the old file, never half a key.
    private static async Task WritePrivateAsync(string path, byte[] contents, CancellationToken ct)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        var temp = path + ".tmp";
        File.Delete(temp); // CreateNew below: the mode only applies to a file it creates
        await using (var stream = new FileStream(temp, PrivateFile(FileMode.CreateNew)))
            await stream.WriteAsync(contents, ct).ConfigureAwait(false);
        File.Move(temp, path, overwrite: true);
    }

    // Readable and writable by the user only. Windows has no Unix modes; the profile folder's ACL covers it there.
    private static FileStreamOptions PrivateFile(FileMode mode)
    {
        var options = new FileStreamOptions { Mode = mode, Access = FileAccess.Write };
        if (!OperatingSystem.IsWindows())
            options.UnixCreateMode = UnixFileMode.UserRead | UnixFileMode.UserWrite;
        return options;
    }

    private sealed record Contents(int Version, Dictionary<string, string> Passwords);

    /// <param name="Key">nonce | encrypted key | tag, base64.</param>
    private sealed record WrappedKey(int Version, string Kdf, int Iterations, string Salt, string Key);
}
