using BiliStreamAudio.Tui.Core;
using System.Text;
using Terminal.Gui.App;
using Terminal.Gui.Drawing;
using Terminal.Gui.Input;
using Terminal.Gui.ViewBase;
using Terminal.Gui.Views;
using GuiLabel = Terminal.Gui.Views.Label;
using GuiView = Terminal.Gui.ViewBase.View;
using GuiButton = Terminal.Gui.Views.Button;
using GuiAttribute = Terminal.Gui.Drawing.Attribute;
using GuiColor = Terminal.Gui.Drawing.Color;

namespace BiliStreamAudio.Tui.Views;

internal sealed class BrowseWindow : Window
{
    private readonly SearchLiveWindow _search;

    public BrowseWindow(
        IApplication app,
        ILiveDirectoryService directory,
        IRoomResolver rooms,
        RoomSession session,
        Action showLiveRoom)
    {
        Title = "浏览";
        var tabs = new Tabs
        {
            X = 0,
            Y = 0,
            Width = Dim.Fill(),
            Height = Dim.Fill()
        };
        _search = new SearchLiveWindow(app, directory, rooms, session, showLiveRoom);
        tabs.Add(new FollowedLiveWindow(app, directory, session, showLiveRoom), _search);
        Add(tabs);
    }

    public bool IsSearchInputFocused => _search.IsQueryFocused;
}

internal abstract class LiveListWindow : Window
{
    private static readonly string[] LoadingFrames = ["⠋", "⠙", "⠹", "⠸", "⠼", "⠴", "⠦", "⠧", "⠇", "⠏"];
    private readonly IApplication _app;
    private readonly RoomSession _session;
    private readonly Action _showLiveRoom;
    private readonly GuiView _cards;
    private readonly GuiLabel _message;
    private CancellationTokenSource? _loadingAnimation;

    protected LiveListWindow(IApplication app, RoomSession session, Action showLiveRoom)
    {
        _app = app;
        _session = session;
        _showLiveRoom = showLiveRoom;
        _message = new GuiLabel
        {
            X = 1,
            Y = 2,
            Width = Dim.Fill(2),
            HotKeySpecifier = new Rune(0xffff)
        };
        _cards = new GuiView
        {
            X = 0,
            Y = 3,
            Width = Dim.Fill(),
            Height = Dim.Fill(3)
        };
        Add(_message, _cards);
    }

    protected void ShowEntries(
        IReadOnlyList<LiveDirectoryEntry> entries,
        string emptyMessage,
        string? resultMessage = null)
    {
        StopLoadingAnimation();
        _cards.RemoveAll();
        _message.Text = entries.Count == 0
            ? emptyMessage
            : resultMessage ?? $"共 {entries.Count} 个结果";
        for (var index = 0; index < entries.Count; index++)
        {
            AddCard(entries[index], index);
        }

        SetNeedsDraw();
    }

    protected void ShowError(string error)
    {
        StopLoadingAnimation();
        _cards.RemoveAll();
        _message.Text = $"加载失败：{error}";
        SetNeedsDraw();
    }

    private void AddCard(LiveDirectoryEntry entry, int y)
    {
        var card = new GuiView
        {
            X = 1,
            Y = y,
            Width = Dim.Fill(2),
            Height = 1
        };
        var status = entry.IsLive ? "正在直播" : "未开播";
        var name = new GuiLabel
        {
            Text = entry.IsDirectRoomEntry ? entry.Anchor : $"{entry.Anchor} · {status}",
            X = 1,
            Y = 0,
            Width = 28,
            HotKeySpecifier = new Rune(0xffff)
        };
        var title = new GuiLabel
        {
            Text = entry.Title,
            X = 30,
            Y = 0,
            Width = Dim.Fill(43),
            HotKeySpecifier = new Rune(0xffff)
        };
        name.SetScheme(new Scheme(new GuiAttribute(
            entry.IsLive ? new GuiColor("#fb7299") : GuiColor.None,
            GuiColor.None,
            TextStyle.Bold)));
        title.SetScheme(new Scheme(new GuiAttribute(GuiColor.White, GuiColor.None, TextStyle.Bold)));
        var started = new GuiLabel
        {
            Text = entry.StartedAt is { } time ? time.ToString("G") : string.Empty,
            X = Pos.AnchorEnd(40),
            Y = 0,
            Width = 22,
            HotKeySpecifier = new Rune(0xffff)
        };
        var play = new GuiButton
        {
            Text = "▶️ 播放",
            X = Pos.AnchorEnd(14),
            Y = 0,
            Width = 12,
            HotKeySpecifier = new Rune(0xffff),
            Enabled = (entry.IsLive || entry.IsDirectRoomEntry) && entry.RoomId > 0
        };
        play.Accepted += (_, _) => Play(entry);
        card.Add(name, title, started, play);
        _cards.Add(card);
    }

    private void Play(LiveDirectoryEntry entry)
    {
        if ((!entry.IsLive && !entry.IsDirectRoomEntry) || entry.RoomId <= 0)
        {
            return;
        }

        _showLiveRoom();
        _ = RunUiTaskAsync(
            () => _session.SwitchAsync(entry.RoomId, CancellationToken.None),
            error => _message.Text = $"播放失败：{error}");
    }

    protected Task RunUiTaskAsync(Func<Task> operation, Action<string> onError) => Task.Run(async () =>
    {
        try
        {
            await operation().ConfigureAwait(false);
        }
        catch (Exception exception)
        {
            _app.Invoke(() =>
            {
                onError(exception.ToDisplayText());
                SetNeedsDraw();
            });
        }
    });

    protected void Invoke(Action action) => _app.Invoke(action);

    protected void ShowLoading(
        string text,
        IReadOnlyList<LiveDirectoryEntry>? retainedEntries = null)
    {
        StopLoadingAnimation();
        _cards.RemoveAll();
        if (retainedEntries is not null)
        {
            for (var index = 0; index < retainedEntries.Count; index++)
            {
                AddCard(retainedEntries[index], index);
            }
        }
        var cancellation = new CancellationTokenSource();
        _loadingAnimation = cancellation;
        _ = Task.Run(async () =>
        {
            var frame = 0;
            while (!cancellation.IsCancellationRequested)
            {
                var currentFrame = LoadingFrames[frame++ % LoadingFrames.Length];
                _app.Invoke(() =>
                {
                    if (!cancellation.IsCancellationRequested)
                    {
                        _message.Text = $"{text} {currentFrame}";
                        _message.SetNeedsDraw();
                    }
                });

                try
                {
                    await Task.Delay(100, cancellation.Token).ConfigureAwait(false);
                }
                catch (OperationCanceledException)
                {
                    break;
                }
            }
        });
    }

    private void StopLoadingAnimation()
    {
        _loadingAnimation?.Cancel();
        _loadingAnimation?.Dispose();
        _loadingAnimation = null;
    }
}

internal sealed class FollowedLiveWindow : LiveListWindow
{
    private readonly ILiveDirectoryService _directory;

    public FollowedLiveWindow(
        IApplication app,
        ILiveDirectoryService directory,
        RoomSession session,
        Action showLiveRoom)
        : base(app, session, showLiveRoom)
    {
        Title = "关注的人";
        _directory = directory;
        KeyDown += (_, key) =>
        {
            if (key == Key.R || key == Key.R.WithShift)
            {
                Load();
                key.Handled = true;
            }
        };
        Load();
    }

    private void Load() => _ = RunUiTaskAsync(async () =>
    {
        var entries = await _directory.GetFollowedLiveAsync(CancellationToken.None).ConfigureAwait(false);
        Invoke(() => ShowEntries(entries, "当前没有关注的主播正在直播。"));
    }, ShowError);
}

internal sealed class SearchLiveWindow : LiveListWindow
{
    private readonly IRoomResolver _rooms;
    private readonly ILiveDirectoryService _directory;
    private readonly TextField _query;

    public bool IsQueryFocused => _query.HasFocus;

    public SearchLiveWindow(
        IApplication app,
        ILiveDirectoryService directory,
        IRoomResolver rooms,
        RoomSession session,
        Action showLiveRoom)
        : base(app, session, showLiveRoom)
    {
        Title = "搜索";
        _directory = directory;
        _rooms = rooms;
        _query = new TextField
        {
            X = 1,
            Y = 0,
            Width = Dim.Fill(12),
            Text = ""
        };
        var search = new GuiButton
        {
            Text = "搜索",
            X = Pos.AnchorEnd(10),
            Y = 0,
            Width = 9
        };
        _query.KeyDown += (_, key) =>
        {
            if (key == Key.Enter)
            {
                Search();
                key.Handled = true;
            }
        };
        search.Accepted += (_, _) => Search();
        Add(_query, search);
    }

    private void Search()
    {
        var query = _query.Text.ToString()?.Trim() ?? string.Empty;
        if (string.IsNullOrEmpty(query))
        {
            ShowEntries([], "请输入主播名称或直播间号。" );
            return;
        }

        var directRoomId = long.TryParse(query, out var parsedRoomId) && parsedRoomId > 0
            ? parsedRoomId
            : 0;
        LiveDirectoryEntry? directEntry = directRoomId > 0
            ? new LiveDirectoryEntry(
                directRoomId,
                0,
                $"进入直播间 {directRoomId}",
                string.Empty,
                false,
                null,
                true)
            : null;
        ShowLoading("搜索中", directEntry is null ? null : [directEntry]);
        _ = RunUiTaskAsync(async () =>
        {
            var entries = await _directory.SearchUsersAsync(query, CancellationToken.None).ConfigureAwait(false);
            var enrichedEntries = await Task.WhenAll(entries.Select(EnrichLiveRoomAsync)).ConfigureAwait(false);
            var allEntries = directEntry is null
                ? enrichedEntries
                : new[] { directEntry }
                    .Concat(enrichedEntries.Where(entry => entry.RoomId != directRoomId))
                    .ToArray();
            Invoke(() => ShowEntries(allEntries, "没有找到匹配的主播。"));
        }, ShowError);
    }

    private async Task<LiveDirectoryEntry> EnrichLiveRoomAsync(LiveDirectoryEntry entry)
    {
        if (!entry.IsLive || entry.RoomId <= 0)
        {
            return entry;
        }

        try
        {
            var room = await _rooms
                .ResolveAsync(new RoomReference(entry.RoomId), CancellationToken.None)
                .ConfigureAwait(false);
            return entry with
            {
                RoomId = room.RoomId,
                Title = room.Title,
                IsLive = room.IsLive
            };
        }
        catch
        {
            // Keep the search result usable when one room's detail request fails.
            return entry;
        }
    }
}
