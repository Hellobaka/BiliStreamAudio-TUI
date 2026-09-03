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

internal sealed class BrowseWindow : ApplicationWindow
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

internal abstract class LiveListWindow : ApplicationWindow
{
    private static readonly string[] LoadingFrames = ["⠋", "⠙", "⠹", "⠸", "⠼", "⠴", "⠦", "⠧", "⠇", "⠏"];
    protected readonly IApplication _app;
    private readonly RoomSession _session;
    private readonly Action _showLiveRoom;
    private readonly GuiView _cards;
    private readonly GuiLabel _message;
    private readonly bool _showDeleteButton;
    private readonly string _dateColumnHeader;
    private CancellationTokenSource? _loadingAnimation;

    /// <summary>点击“删除”按钮时回调，由子类实现确认与删除逻辑。</summary>
    protected virtual void OnDelete(LiveDirectoryEntry entry)
    {
    }

    protected LiveListWindow(
        IApplication app,
        RoomSession session,
        Action showLiveRoom,
        bool showHeader = false,
        bool showDeleteButton = false,
        string dateColumnHeader = "开播时间")
    {
        _app = app;
        _session = session;
        _showLiveRoom = showLiveRoom;
        _showDeleteButton = showDeleteButton;
        _dateColumnHeader = dateColumnHeader;
        if (showHeader)
        {
            AddHeader();
        }

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

    private void AddHeader()
    {
        var header = new GuiView
        {
            X = 1,
            Y = 1,
            Width = Dim.Fill(2),
            Height = 1
        };
        var bold = new Scheme(new GuiAttribute(GuiColor.White, GuiColor.None, TextStyle.Bold));
        var anchorHeader = new GuiLabel
        {
            Text = "主播",
            X = 1,
            Y = 0,
            Width = 28,
            HotKeySpecifier = new Rune(0xffff)
        };
        var titleHeader = new GuiLabel
        {
            Text = "标题",
            X = 30,
            Y = 0,
            Width = 20,
            HotKeySpecifier = new Rune(0xffff)
        };
        var dateHeader = new GuiLabel
        {
            Text = _dateColumnHeader,
            X = _showDeleteButton ? Pos.AnchorEnd(56) : Pos.AnchorEnd(40),
            Y = 0,
            Width = 22,
            HotKeySpecifier = new Rune(0xffff)
        };
        var actionHeader = new GuiLabel
        {
            Text = "操作",
            X = _showDeleteButton ? Pos.AnchorEnd(32) : Pos.AnchorEnd(14),
            Y = 0,
            Width = 24,
            HotKeySpecifier = new Rune(0xffff)
        };
        anchorHeader.SetScheme(bold);
        titleHeader.SetScheme(bold);
        dateHeader.SetScheme(bold);
        actionHeader.SetScheme(bold);
        header.Add(anchorHeader, titleHeader, dateHeader, actionHeader);
        Add(header);
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
        var date = new GuiLabel
        {
            Text = (entry.WatchedAt ?? entry.StartedAt) is { } time ? time.ToString("G") : string.Empty,
            X = _showDeleteButton ? Pos.AnchorEnd(56) : Pos.AnchorEnd(40),
            Y = 0,
            Width = 22,
            HotKeySpecifier = new Rune(0xffff)
        };
        var play = new GuiButton
        {
            Text = "▶️ 播放",
            X = _showDeleteButton ? Pos.AnchorEnd(32) : Pos.AnchorEnd(14),
            Y = 0,
            Width = 12,
            HotKeySpecifier = new Rune(0xffff),
            Enabled = (entry.IsLive || entry.IsDirectRoomEntry) && entry.RoomId > 0
        };
        play.Accepted += (_, _) => Play(entry);
        if (_showDeleteButton)
        {
            var delete = new GuiButton
            {
                Text = "🗑 删除",
                X = Pos.AnchorEnd(14),
                Y = 0,
                Width = 12,
                HotKeySpecifier = new Rune(0xffff),
                Enabled = entry.RoomId > 0
            };
            delete.Accepted += (_, _) => OnDelete(entry);
            card.Add(name, title, date, play, delete);
        }
        else
        {
            card.Add(name, title, date, play);
        }

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

internal sealed class PlaybackHistoryWindow : LiveListWindow
{
    private const int MaximumConcurrentStatusRequests = 4;

    private readonly IHistoryStore _history;
    private readonly IRoomResolver _rooms;
    private readonly SemaphoreSlim _statusRequestGate = new(MaximumConcurrentStatusRequests);
    private int _loadVersion;

    public PlaybackHistoryWindow(
        IApplication app,
        IHistoryStore history,
        IRoomResolver rooms,
        RoomSession session,
        Action showLiveRoom)
        : base(app, session, showLiveRoom, showHeader: true, showDeleteButton: true, dateColumnHeader: "上次观看")
    {
        Title = "观看历史";
        _history = history;
        _rooms = rooms;
        KeyDown += (_, key) =>
        {
            if (key == Key.R || key == Key.R.WithShift)
            {
                Load();
                key.Handled = true;
            }
        };
    }

    public void Load()
    {
        var loadVersion = Interlocked.Increment(ref _loadVersion);
        _ = RunUiTaskAsync(async () =>
        {
            var records = _history.GetPlaybackHistory();
            var entries = records
                .Select(record => new LiveDirectoryEntry(
                    record.RoomId,
                    0,
                    record.Anchor,
                    record.Title,
                    false,
                    null,
                    true,
                    record.WatchedAt))
                .ToList();
            var enriched = await Task.WhenAll(entries.Select(RefreshLiveStatusAsync)).ConfigureAwait(false);
            Invoke(() =>
            {
                if (loadVersion != Volatile.Read(ref _loadVersion))
                {
                    return;
                }

                ShowEntries(
                    enriched,
                    "还没有观看历史。播放任意直播间后会在这里显示。",
                    $"共 {enriched.Length} 条观看历史");
            });
        }, error =>
        {
            if (loadVersion == Volatile.Read(ref _loadVersion))
            {
                ShowError(error);
            }
        });
    }

    private async Task<LiveDirectoryEntry> RefreshLiveStatusAsync(LiveDirectoryEntry entry)
    {
        if (entry.RoomId <= 0)
        {
            return entry;
        }

        try
        {
            await _statusRequestGate.WaitAsync().ConfigureAwait(false);
            var room = await _rooms
                .ResolveAsync(new RoomReference(entry.RoomId), CancellationToken.None)
                .ConfigureAwait(false);
            return entry with
            {
                RoomId = room.RoomId,
                Title = room.Title,
                IsLive = room.IsLive,
                IsDirectRoomEntry = false
            };
        }
        catch
        {
            // Keep the history entry usable when one room's detail request fails.
            return entry;
        }
        finally
        {
            _statusRequestGate.Release();
        }
    }

    protected override void OnDelete(LiveDirectoryEntry entry)
    {
        if (entry.RoomId <= 0)
        {
            return;
        }

        var choice = Terminal.Gui.Views.MessageBox.Query(
            _app,
            "删除观看历史",
            $"确定要删除「{entry.Anchor}」的观看历史吗？",
            ["删除", "取消"]);
        if (choice != 0)
        {
            return;
        }

        _history.DeletePlayback(entry.RoomId);
        Load();
    }
}
