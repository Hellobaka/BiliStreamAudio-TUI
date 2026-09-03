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
            var needsLayoutMigration = existing.StatusBarLayoutVersion is null;
            NormalizeStatusBarLayout(existing);
            if (needsLayoutMigration)
            {
                _collection.Upsert(existing);
            }

            return existing;
        }

        var defaults = new AppSettings();
        NormalizeStatusBarLayout(defaults);
        _collection.Insert(defaults);
        return defaults;
    }

    public void Save(AppSettings settings)
    {
        settings.Id = 1;
        NormalizeStatusBarLayout(settings);
        _collection.Upsert(settings);
    }

    public void Dispose() => _db.Dispose();

    private static void NormalizeStatusBarLayout(AppSettings settings)
    {
        var layout = StatusBarLayout.Normalize(
            settings.StatusBarFirstRow,
            settings.StatusBarSecondRow,
            settings.StatusBarLayoutVersion is null);
        settings.StatusBarFirstRow = layout.FirstRow;
        settings.StatusBarSecondRow = layout.SecondRow;
        settings.StatusBarLayoutVersion = 1;
    }
}
