using System.Security.Cryptography;
using System.Text.Json;
using BiliStreamAudio.Tui.Core;

namespace BiliStreamAudio.Tui.Infrastructure;

public sealed class AuthStorage
{
    private readonly string _directory;
    private readonly string _path;
    private readonly string _backupPath;

    public AuthStorage(string? directory = null)
    {
        var localData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        _directory = directory ?? Path.Combine(localData, "BiliStreamAudio-TUI");
        _path = Path.Combine(_directory, "session.dat");
        _backupPath = Path.Combine(_directory, "session.previous.dat");
    }

    public async Task<AuthSession?> LoadAsync(CancellationToken cancellationToken)
    {
        if (!File.Exists(_path))
        {
            return null;
        }

        try
        {
            return await ReadAsync(_path, cancellationToken).ConfigureAwait(false);
        }
        catch (CryptographicException)
        {
            return null;
        }
        catch (JsonException)
        {
            return null;
        }
    }

    public async Task SaveAsync(AuthSession session, CancellationToken cancellationToken)
    {
        Directory.CreateDirectory(_directory);
        var temporary = _path + ".tmp";
        await WriteAsync(temporary, session, cancellationToken).ConfigureAwait(false);
        File.Move(temporary, _path, true);
    }

    public async Task SaveBackupAsync(AuthSession session, CancellationToken cancellationToken)
    {
        Directory.CreateDirectory(_directory);
        await WriteAsync(_backupPath, session, cancellationToken).ConfigureAwait(false);
    }

    public void DeleteBackup()
    {
        if (File.Exists(_backupPath))
        {
            File.Delete(_backupPath);
        }
    }

    public Task ClearAsync(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (File.Exists(_path))
        {
            File.Delete(_path);
        }

        if (File.Exists(_backupPath))
        {
            File.Delete(_backupPath);
        }

        return Task.CompletedTask;
    }

    private static async Task<AuthSession> ReadAsync(string path, CancellationToken cancellationToken)
    {
        var encrypted = await File.ReadAllBytesAsync(path, cancellationToken).ConfigureAwait(false);
        var bytes = ProtectedData.Unprotect(encrypted, null, DataProtectionScope.CurrentUser);
        return JsonSerializer.Deserialize<AuthSession>(bytes) ?? throw new JsonException("Empty session.");
    }

    private static async Task WriteAsync(string path, AuthSession session, CancellationToken cancellationToken)
    {
        var bytes = JsonSerializer.SerializeToUtf8Bytes(session);
        var encrypted = ProtectedData.Protect(bytes, null, DataProtectionScope.CurrentUser);
        await File.WriteAllBytesAsync(path, encrypted, cancellationToken).ConfigureAwait(false);
    }
}
