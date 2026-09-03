using System.Globalization;
using System.Text;
using BiliStreamAudio.Tui.Core;

namespace BiliStreamAudio.Tui.Infrastructure;

internal sealed class MockAuthService : IAuthService
{
    private static readonly IReadOnlyDictionary<string, string> MockCookies =
        new Dictionary<string, string>
        {
            ["SESSDATA"] = "mock-session",
            ["bili_jct"] = "mock-csrf"
        };

    private AuthSession? _current = CreateSession();

    public AuthSession? Current => _current;

    public Task<AuthSession> LoginAsync(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        _current = CreateSession();
        return Task.FromResult(_current);
    }

    public Task<AuthSession?> LoadAsync(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return Task.FromResult(_current);
    }

    public Task SaveAsync(AuthSession session, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        _current = session;
        return Task.CompletedTask;
    }

    public Task ClearAsync(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        _current = null;
        return Task.CompletedTask;
    }

    private static AuthSession CreateSession() => new(
        MockCookies,
        null,
        1,
        null,
        "Mock_用户");
}

internal sealed class MockTokenRefreshService : ITokenRefreshService
{
    public Task<RefreshResult> RefreshIfNeededAsync(
        AuthSession session,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return Task.FromResult(new RefreshResult(true, session, null));
    }
}

internal sealed class MockRoomResolver : IRoomResolver
{
    public Task<LiveRoom> ResolveAsync(RoomReference room, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (room.RequestedId <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(room), "直播间号必须为正数。");
        }

        return Task.FromResult(new LiveRoom(
            room.RequestedId,
            room.RequestedId,
            1,
            $"模拟直播间 {room.RequestedId}",
            "Mock_主播",
            true));
    }
}

internal sealed class MockStreamResolver : IStreamResolver
{
    public Task<IReadOnlyList<StreamDescriptor>> ResolveAudioAsync(
        LiveRoom room,
        bool allowVideoFallback,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        IReadOnlyList<StreamDescriptor> streams =
        [
            new StreamDescriptor(
                new Uri($"mock://audio/{room.RoomId}"),
                "mock",
                "none",
                0,
                true,
                room.RoomId)
        ];
        return Task.FromResult(streams);
    }
}

internal sealed class MockLiveDirectoryService : ILiveDirectoryService
{
    private static readonly IReadOnlyList<LiveDirectoryEntry> FollowedLive =
    [
        new(10001, 101, "绮梦", "深夜电台：一起听歌", true, DateTimeOffset.Now.AddHours(-2)),
        new(10002, 102, "小北", "独立游戏试玩", true, DateTimeOffset.Now.AddMinutes(-48)),
        new(10003, 103, "阿澈", "晚间杂谈", true, DateTimeOffset.Now.AddMinutes(-15))
    ];

    private static readonly IReadOnlyList<LiveDirectoryEntry> SearchResults =
    [
        new(10001, 101, "绮梦", "", true, DateTimeOffset.Now.AddHours(-2)),
        new(0, 104, "未开播的模拟主播", "", false, null),
        new(10004, 105, "游戏小屋", "", true, DateTimeOffset.Now.AddHours(-1))
    ];

    public Task<IReadOnlyList<LiveDirectoryEntry>> GetFollowedLiveAsync(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return Task.FromResult(FollowedLive);
    }

    public async Task<IReadOnlyList<LiveDirectoryEntry>> SearchUsersAsync(string keyword, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        await Task.Delay(Random.Shared.Next(300, 801), cancellationToken).ConfigureAwait(false);
        return string.IsNullOrWhiteSpace(keyword)
            || keyword.Equals("empty", StringComparison.OrdinalIgnoreCase)
            ? []
            : SearchResults;
    }
}

internal sealed class MockAudioPlayer : IAudioPlayer
{
    private PlaybackState _state = PlaybackState.Stopped;
    private int _volume = 70;
    private bool _muted;

    public event EventHandler<PlaybackState>? StateChanged;

    public PlaybackState State => _state;
    public int Volume => _volume;
    public bool IsMuted => _muted;

    public Task PlayAsync(StreamDescriptor stream, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        SetState(PlaybackState.Buffering);
        SetState(PlaybackState.Playing);
        return Task.CompletedTask;
    }

    public Task StopAsync()
    {
        SetState(PlaybackState.Stopped);
        return Task.CompletedTask;
    }

    public void SetVolume(int volume) => _volume = Math.Clamp(volume, 0, 100);

    public void ToggleMute() => _muted = !_muted;

    public void Dispose()
    {
    }

    private void SetState(PlaybackState state)
    {
        if (_state == state)
        {
            return;
        }

        _state = state;
        StateChanged?.Invoke(this, state);
    }
}

internal sealed class MockDanmakuConnection : IDanmakuConnection
{
    private static readonly string[] UserNamePrefixes =
    [
        "星", "云", "小", "阿", "白", "柚", "夏", "夜"
    ];

    private static readonly string[] UserNameSuffixes =
    [
        "团子", "汽水", "海盐", "奶糖", "旅人", "鲸鱼", "猫", "同学"
    ];

    private static readonly string[] Messages =
    [
        "来了来了", "这个节目真不错", "主播晚上好", "好听！", "前排打卡", "今天也很开心",
        "哈哈哈哈", "支持一下", "这段我喜欢", "路过留个爪印"
    ];

    private static readonly string[] MedalNames =
    [
        "星光团", "小北团", "夜聊会", "游玩团", "绮梦团"
    ];

    private static readonly string[] GiftNames =
    [
        "辣条", "小花花", "能量石", "告白气球", "星愿瓶"
    ];

    private static readonly long[] GiftPrices = [100, 500, 1000, 5000];

    private static readonly string[] SuperChatMessages =
    [
        "主播辛苦啦，今晚的节目很喜欢！", "第一次来，留下一张 SC 支持一下。",
        "这段分享太有意思了，继续加油！", "送给直播间的每一位朋友。"
    ];

    private static readonly int[] SuperChatPrices = [30, 50, 100, 300];

    private readonly object _gate = new();
    private readonly TimeSpan _minimumInterval;
    private readonly TimeSpan _maximumInterval;
    private readonly TimeSpan _minimumGiftInterval;
    private readonly TimeSpan _maximumGiftInterval;
    private readonly TimeSpan _minimumSuperChatInterval;
    private readonly TimeSpan _maximumSuperChatInterval;
    private CancellationTokenSource? _pushLifetime;
    private Task? _pushTask;
    private bool _disposed;

    public MockDanmakuConnection(
        TimeSpan? minimumInterval = null,
        TimeSpan? maximumInterval = null,
        TimeSpan? minimumGiftInterval = null,
        TimeSpan? maximumGiftInterval = null,
        TimeSpan? minimumSuperChatInterval = null,
        TimeSpan? maximumSuperChatInterval = null)
    {
        _minimumInterval = minimumInterval ?? TimeSpan.FromMilliseconds(800);
        _maximumInterval = maximumInterval ?? TimeSpan.FromMilliseconds(1800);
        _minimumGiftInterval = minimumGiftInterval ?? TimeSpan.FromSeconds(6);
        _maximumGiftInterval = maximumGiftInterval ?? TimeSpan.FromSeconds(15);
        _minimumSuperChatInterval = minimumSuperChatInterval ?? TimeSpan.FromSeconds(25);
        _maximumSuperChatInterval = maximumSuperChatInterval ?? TimeSpan.FromSeconds(45);

        ValidateInterval(_minimumInterval, _maximumInterval, nameof(minimumInterval), nameof(maximumInterval));
        ValidateInterval(_minimumGiftInterval, _maximumGiftInterval, nameof(minimumGiftInterval), nameof(maximumGiftInterval));
        ValidateInterval(
            _minimumSuperChatInterval,
            _maximumSuperChatInterval,
            nameof(minimumSuperChatInterval),
            nameof(maximumSuperChatInterval));
    }

    public event EventHandler<LiveEvent>? EventReceived;
    public event EventHandler<DanmakuEvent>? Received;
    public event EventHandler<string>? StatusChanged;

    public async Task ConnectAsync(LiveRoom room, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        await DisconnectAsync().ConfigureAwait(false);

        var pushLifetime = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        lock (_gate)
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            _pushLifetime = pushLifetime;
            _pushTask = Task.WhenAll(
                PushGeneratedDanmakuAsync(pushLifetime.Token),
                PushGeneratedGiftsAsync(pushLifetime.Token),
                PushGeneratedSuperChatsAsync(pushLifetime.Token));
        }

        StatusChanged?.Invoke(this, "弹幕模拟已连接");
        Publish("系统", $"已进入模拟直播间 {room.RoomId}");
    }

    public async Task DisconnectAsync()
    {
        CancellationTokenSource? pushLifetime;
        Task? pushTask;
        lock (_gate)
        {
            pushLifetime = _pushLifetime;
            pushTask = _pushTask;
            _pushLifetime = null;
            _pushTask = null;
        }

        if (pushLifetime is null)
        {
            return;
        }

        pushLifetime.Cancel();
        try
        {
            if (pushTask is not null)
            {
                await pushTask.ConfigureAwait(false);
            }
        }
        finally
        {
            pushLifetime.Dispose();
        }
    }

    public async ValueTask DisposeAsync()
    {
        lock (_gate)
        {
            _disposed = true;
        }

        await DisconnectAsync().ConfigureAwait(false);
    }

    public void Publish(string userName, string message)
    {
        Publish(new DanmakuEvent(userName, message, DateTimeOffset.Now));
    }

    public void Publish(LiveEvent item)
    {
        EventReceived?.Invoke(this, item);
        if (item is DanmakuEvent danmaku)
        {
            Received?.Invoke(this, danmaku);
        }
    }

    private async Task PushGeneratedDanmakuAsync(CancellationToken cancellationToken)
    {
        try
        {
            while (true)
            {
                await Task.Delay(
                    GetRandomInterval(_minimumInterval, _maximumInterval),
                    cancellationToken).ConfigureAwait(false);
                cancellationToken.ThrowIfCancellationRequested();
                Publish(CreateRandomDanmaku());
            }
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
        }
    }

    private async Task PushGeneratedGiftsAsync(CancellationToken cancellationToken)
    {
        try
        {
            while (true)
            {
                await Task.Delay(
                    GetRandomInterval(_minimumGiftInterval, _maximumGiftInterval),
                    cancellationToken).ConfigureAwait(false);
                cancellationToken.ThrowIfCancellationRequested();
                Publish(CreateRandomGift());
            }
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
        }
    }

    private async Task PushGeneratedSuperChatsAsync(CancellationToken cancellationToken)
    {
        try
        {
            while (true)
            {
                await Task.Delay(
                    GetRandomInterval(_minimumSuperChatInterval, _maximumSuperChatInterval),
                    cancellationToken).ConfigureAwait(false);
                cancellationToken.ThrowIfCancellationRequested();
                Publish(CreateRandomSuperChat());
            }
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
        }
    }

    private static void ValidateInterval(
        TimeSpan minimum,
        TimeSpan maximum,
        string minimumParameterName,
        string maximumParameterName)
    {
        if (minimum <= TimeSpan.Zero)
        {
            throw new ArgumentOutOfRangeException(minimumParameterName);
        }

        if (maximum < minimum)
        {
            throw new ArgumentOutOfRangeException(maximumParameterName);
        }
    }

    private static TimeSpan GetRandomInterval(TimeSpan minimum, TimeSpan maximum)
    {
        var range = maximum - minimum;
        return range <= TimeSpan.Zero
            ? minimum
            : minimum + TimeSpan.FromMilliseconds(Random.Shared.NextDouble() * range.TotalMilliseconds);
    }

    private static DanmakuEvent CreateRandomDanmaku()
    {
        var user = CreateRandomUser();
        var medal = Random.Shared.Next(100) < 45 ? null : new FanMedal(
            Random.Shared.NextInt64(1, long.MaxValue),
            Random.Shared.GetItems(MedalNames, 1)[0],
            Random.Shared.Next(1, 31),
            Random.Shared.NextInt64(1, long.MaxValue),
            "Mock_主播",
            0,
            true,
            0,
            0,
            0,
            0,
            0,
            string.Empty,
            string.Empty,
            string.Empty,
            string.Empty,
            string.Empty,
            string.Empty,
            string.Empty);

        return new DanmakuEvent(
            user.UserName,
            Random.Shared.GetItems(Messages, 1)[0],
            DateTimeOffset.Now,
            UserId: user.UserId,
            Medal: medal);
    }

    private static GiftEvent CreateRandomGift()
    {
        var user = CreateRandomUser();
        var count = Random.Shared.Next(1, 6);
        var unitPrice = Random.Shared.GetItems(GiftPrices, 1)[0];
        return new GiftEvent(
            user.UserId,
            user.UserName,
            Random.Shared.NextInt64(1, long.MaxValue),
            Random.Shared.GetItems(GiftNames, 1)[0],
            count,
            unitPrice,
            unitPrice * count,
            "gold",
            Guid.NewGuid().ToString("N", CultureInfo.InvariantCulture),
            string.Empty,
            DateTimeOffset.Now);
    }

    private static SuperChatEvent CreateRandomSuperChat()
    {
        var user = CreateRandomUser();
        var price = Random.Shared.GetItems(SuperChatPrices, 1)[0];
        var now = DateTimeOffset.Now;
        return new SuperChatEvent(
            Guid.NewGuid().ToString("N", CultureInfo.InvariantCulture),
            user.UserId,
            user.UserName,
            Random.Shared.GetItems(SuperChatMessages, 1)[0],
            string.Empty,
            string.Empty,
            price,
            now,
            now.Add(SuperChatPresentation.GetLifetime(price)),
            price * 2);
    }

    private static (long UserId, string UserName) CreateRandomUser() => (
        Random.Shared.NextInt64(1, long.MaxValue),
        $"{Random.Shared.GetItems(UserNamePrefixes, 1)[0]}{Random.Shared.GetItems(UserNameSuffixes, 1)[0]}");
}

internal static class MockSuperChatCommand
{
    private const string Prefix = "sc:";

    public static bool IsCommand(string value) =>
        value.StartsWith(Prefix, StringComparison.OrdinalIgnoreCase);

    public static SuperChatEvent Parse(string value, AuthSession session, DateTimeOffset now)
    {
        var payload = value[Prefix.Length..];
        var parts = payload.Split((char[]?)null, 2, StringSplitOptions.RemoveEmptyEntries);
        if (parts.Length != 2
            || !int.TryParse(parts[0], NumberStyles.None, CultureInfo.InvariantCulture, out var price)
            || price <= 0
            || string.IsNullOrWhiteSpace(parts[1]))
        {
            throw new ArgumentException("Mock SC 格式应为：sc:<正整数金额> <正文>。", nameof(value));
        }

        var lifetime = SuperChatPresentation.GetLifetime(price);
        var durationSeconds = price > int.MaxValue / 2 ? int.MaxValue : price * 2;
        return new SuperChatEvent(
            Guid.NewGuid().ToString("N", CultureInfo.InvariantCulture),
            session.UserId,
            session.UserName ?? "Mock 用户",
            parts[1],
            string.Empty,
            string.Empty,
            price,
            now,
            now.Add(lifetime),
            durationSeconds);
    }
}

internal static class MockGiftCommand
{
    private const string Prefix = "gift";

    public static bool IsCommand(string value) =>
        value.Equals(Prefix, StringComparison.OrdinalIgnoreCase)
        || value.StartsWith($"{Prefix} ", StringComparison.OrdinalIgnoreCase);

    public static GiftEvent Parse(string value, AuthSession session, DateTimeOffset now)
    {
        var parts = value.Split((char[]?)null, 4, StringSplitOptions.RemoveEmptyEntries);
        if (parts.Length != 4
            || !decimal.TryParse(parts[1], NumberStyles.Float, CultureInfo.InvariantCulture, out var amount)
            || amount <= 0
            || !int.TryParse(parts[2], NumberStyles.None, CultureInfo.InvariantCulture, out var count)
            || count <= 0
            || string.IsNullOrWhiteSpace(parts[3]))
        {
            throw new ArgumentException("Mock 礼物格式应为：gift <正数金额> <正整数个数> <描述>。", nameof(value));
        }

        if (amount > long.MaxValue / 1000m)
        {
            throw new ArgumentException("Mock 礼物金额或个数超出范围。", nameof(value));
        }

        var unitPrice = decimal.ToInt64(decimal.Round(amount * 1000m, 0, MidpointRounding.AwayFromZero));
        if (unitPrice < 1 || unitPrice > long.MaxValue / count)
        {
            throw new ArgumentException("Mock 礼物金额或个数超出范围。", nameof(value));
        }

        return new GiftEvent(
            session.UserId,
            session.UserName ?? "Mock 用户",
            0,
            parts[3],
            count,
            unitPrice,
            unitPrice * count,
            "gold",
            Guid.NewGuid().ToString("N", CultureInfo.InvariantCulture),
            string.Empty,
            now);
    }
}

internal static class MockFanMedalCommand
{
    private const string Prefix = "badge";
    private const int MaximumNameWidth = 6;

    public static bool IsCommand(string value) =>
        value.Equals(Prefix, StringComparison.OrdinalIgnoreCase)
        || value.StartsWith($"{Prefix} ", StringComparison.OrdinalIgnoreCase);

    public static FanMedal Parse(string value)
    {
        var parts = value.Split((char[]?)null, 3, StringSplitOptions.RemoveEmptyEntries);
        if (parts.Length != 3
            || !int.TryParse(parts[1], NumberStyles.None, CultureInfo.InvariantCulture, out var level)
            || level < 0
            || string.IsNullOrWhiteSpace(parts[2])
            || GetNameWidth(parts[2]) > MaximumNameWidth)
        {
            throw new ArgumentException("Mock 勋章格式应为：badge <非负等级> <最多 6 个英文或 3 个中文字符的名称>。", nameof(value));
        }

        return new FanMedal(
            0,
            parts[2],
            level,
            0,
            string.Empty,
            0,
            false,
            0,
            0,
            0,
            0,
            0,
            string.Empty,
            string.Empty,
            string.Empty,
            string.Empty,
            string.Empty,
            string.Empty,
            string.Empty);
    }

    private static int GetNameWidth(string name) => name.EnumerateRunes().Sum(rune =>
        rune.Value is <= 0x7f ? 1 : 2);
}

internal sealed class MockHistoryStore : IHistoryStore
{
    private readonly object _gate = new();
    private readonly List<(long RoomId, string Message, DateTimeOffset SentAt)> _danmaku = [];
    private readonly Dictionary<long, PlaybackHistoryEntry> _playback = [];

    public void RecordDanmakuSent(long roomId, string message, DateTimeOffset sentAt)
    {
        if (string.IsNullOrWhiteSpace(message))
        {
            return;
        }

        lock (_gate)
        {
            _danmaku.Add((roomId, message, sentAt));
        }
    }

    public IReadOnlyList<string> GetDanmakuHistory(long? roomId, int limit = 50)
    {
        lock (_gate)
        {
            return _danmaku
                .Where(item => roomId is null || item.RoomId == roomId)
                .OrderByDescending(item => item.SentAt)
                .Take(limit)
                .Select(item => item.Message)
                .ToList();
        }
    }

    public void RecordPlayback(long roomId, string anchor, string title, DateTimeOffset watchedAt)
    {
        lock (_gate)
        {
            _playback[roomId] = new PlaybackHistoryEntry(roomId, anchor, title, watchedAt);
        }
    }

    public IReadOnlyList<PlaybackHistoryEntry> GetPlaybackHistory(int limit = 100)
    {
        lock (_gate)
        {
            return _playback.Values
                .OrderByDescending(entry => entry.WatchedAt)
                .Take(limit)
                .ToList();
        }
    }

    public void DeletePlayback(long roomId)
    {
        lock (_gate)
        {
            _playback.Remove(roomId);
        }
    }

    public void Dispose()
    {
    }
}

internal sealed class MockDanmakuSender(MockDanmakuConnection connection) : IDanmakuSender
{
    private FanMedal? _fanMedal;

    public async Task SendAsync(
        long roomId,
        string message,
        AuthSession session,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (!session.IsAuthenticated)
        {
            throw new InvalidOperationException("请先登录。");
        }

        if (MockSuperChatCommand.IsCommand(message))
        {
            var superChat = MockSuperChatCommand.Parse(message, session, DateTimeOffset.Now);
            await Task.Delay(Random.Shared.Next(100, 301), cancellationToken).ConfigureAwait(false);
            connection.Publish(superChat);
            return;
        }

        if (MockGiftCommand.IsCommand(message))
        {
            var gift = MockGiftCommand.Parse(message, session, DateTimeOffset.Now);
            await Task.Delay(Random.Shared.Next(100, 301), cancellationToken).ConfigureAwait(false);
            connection.Publish(gift);
            return;
        }

        if (MockFanMedalCommand.IsCommand(message))
        {
            _fanMedal = MockFanMedalCommand.Parse(message);
            return;
        }

        if (string.IsNullOrWhiteSpace(message) || message.EnumerateRunes().Count() > 30)
        {
            throw new ArgumentException("弹幕长度须为 1–30 个字符。", nameof(message));
        }

        await Task.Delay(Random.Shared.Next(400, 1201), cancellationToken).ConfigureAwait(false);
        if (string.Equals(message, "error", StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException("模拟发送失败。");
        }

        connection.Publish(new DanmakuEvent(
            session.UserName ?? "Mock 用户",
            message,
            DateTimeOffset.Now,
            Medal: _fanMedal));
    }
}
