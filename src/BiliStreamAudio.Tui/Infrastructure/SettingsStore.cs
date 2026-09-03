using BiliStreamAudio.Tui.Core;
using LiteDB;

namespace BiliStreamAudio.Tui.Infrastructure;

public sealed class SettingsStore : ISettingsStore
{
    private const string CollectionName = "settings";

    private readonly LiteDatabase _db;
    private readonly ILiteCollection<AppSettings> _collection;

    public SettingsStore(string? path = null)
    {
        var directory = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        var resolvedPath = Path.GetFullPath(
            path ?? Path.Combine(directory, "BiliStreamAudio-TUI", "settings.db"));
        var parentDirectory = Path.GetDirectoryName(resolvedPath);
        if (!string.IsNullOrEmpty(parentDirectory))
        {
            Directory.CreateDirectory(parentDirectory);
        }

        _db = new LiteDatabase(new ConnectionString($"filename={resolvedPath}"), new BsonMapper());
        _collection = _db.GetCollection<AppSettings>(CollectionName);
    }

    public AppSettings Load()
    {
        var existing = _collection.FindById(1);
        if (existing is not null)
        {
            return existing;
        }

        var defaults = new AppSettings();
        _collection.Insert(defaults);
        return defaults;
    }

    public void Save(AppSettings settings)
    {
        settings.Id = 1;
        _collection.Upsert(settings);
    }

    public void Dispose() => _db.Dispose();
}
