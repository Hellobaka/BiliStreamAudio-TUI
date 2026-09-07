using BiliStreamAudio.Tui.Core;
using NAudio.CoreAudioApi;
using Serilog;
using System.Diagnostics;
using System.Text.RegularExpressions;
using BufferedWaveProvider = NAudio.Wave.BufferedWaveProvider;
using VolumeWaveProvider16 = NAudio.Wave.VolumeWaveProvider16;
using WasapiOut = NAudio.Wave.WasapiOut;
using WaveFormat = NAudio.Wave.WaveFormat;

namespace BiliStreamAudio.Tui.Infrastructure;

public sealed class AudioPlayer : IAudioPlayer, IAudioSpectrumSource
{
    private const int SampleRate = 48_000;
    private const int Channels = 2;
    private const int BitsPerSample = 16;
    private const int BytesPerSecond = SampleRate * Channels * (BitsPerSample / 8);
    private const int PlaybackBufferMilliseconds = 3_000;
    private const int PrebufferMilliseconds = 1_000;
    private const int PrebufferBytes = BytesPerSecond * PrebufferMilliseconds / 1_000;

    private readonly object _sync = new();
    private readonly SemaphoreSlim _lifecycle = new(1, 1);
    private readonly string _ffmpegPath;
    private readonly BufferedWaveProvider _audioBuffer = new(new WaveFormat(SampleRate, BitsPerSample, Channels))
    {
        BufferDuration = TimeSpan.FromMilliseconds(PlaybackBufferMilliseconds),
        DiscardOnBufferOverflow = true,
        ReadFully = true
    };
    private readonly VolumeWaveProvider16 _volumeProvider;
    private readonly AudioSpectrumAnalyzer _spectrumAnalyzer = new();
    private PlaybackProcessSession? _session;
    private PlaybackState _state = PlaybackState.Stopped;
    private int _volume = 70;
    private bool _muted;
    private NetworkProxyMode _networkProxyMode;
    private string _manualProxyUrl = string.Empty;
    private bool _disposed;
    private WasapiOut? _audioOutput;

    public AudioPlayer()
        : this(Path.Combine(AppContext.BaseDirectory, FfmpegProcess.ExecutableName))
    {
    }

    internal AudioPlayer(string ffmpegPath)
    {
        _ffmpegPath = ffmpegPath;
        _volumeProvider = new VolumeWaveProvider16(_audioBuffer)
        {
            Volume = _volume / 100f
        };
    }

    public event EventHandler<PlaybackState>? StateChanged;
    public event EventHandler<SpectrumFrame>? SpectrumChanged
    {
        add => _spectrumAnalyzer.SpectrumChanged += value;
        remove => _spectrumAnalyzer.SpectrumChanged -= value;
    }

    public PlaybackState State => _state;
    public int Volume => _volume;
    public bool IsMuted => _muted;
    public NetworkProxyMode NetworkProxyMode
    {
        get
        {
            lock (_sync)
            {
                return _networkProxyMode;
            }
        }
    }
    public SpectrumFrame? CurrentSpectrum => _spectrumAnalyzer.CurrentSpectrum;

    public void SetSpectrumEnabled(bool enabled) => _spectrumAnalyzer.SetEnabled(enabled);

    public async Task PlayAsync(StreamDescriptor stream, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        await _lifecycle.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            await StopCoreAsync().ConfigureAwait(false);
            cancellationToken.ThrowIfCancellationRequested();

            if (!File.Exists(_ffmpegPath))
            {
                SetState(PlaybackState.Error);
                throw new FileNotFoundException("找不到 FFmpeg 音频解码器。", _ffmpegPath);
            }

            Log.Information(
                "拉取直播流：房间 {RoomId}，{Protocol}/{Format}，编码 {Codec}，画质 {Quality}，预期码率 {Bitrate}",
                stream.RoomId,
                stream.Protocol,
                stream.Format,
                stream.Codec,
                stream.Quality,
                stream.BitrateKbps is { } bitrate ? $"{bitrate} kbps" : "未知");

            NetworkProxyMode proxyMode;
            string manualProxyUrl;
            lock (_sync)
            {
                proxyMode = _networkProxyMode;
                manualProxyUrl = _manualProxyUrl;
            }
            var proxy = NetworkProxy.Resolve(stream.Url, proxyMode, manualProxyUrl);
            if (proxy is not null)
            {
                Log.Information(
                    "FFmpeg 播放使用 {ProxyMode} 网络代理 {ProxyHost}:{ProxyPort}",
                    proxyMode == NetworkProxyMode.AutoDetect ? "自动检测" : "手动配置",
                    proxy.Host,
                    proxy.Port);
            }

            var process = new Process
            {
                StartInfo = FfmpegProcess.CreateStartInfo(
                    _ffmpegPath,
                    stream,
                    proxy)
            };
            PlaybackProcessSession? session = null;
            try
            {
                EnsureAudioOutput();
                if (!process.Start())
                {
                    throw new InvalidOperationException("FFmpeg 音频解码器无法启动。");
                }

                session = new PlaybackProcessSession(process, cancellationToken);
                lock (_sync)
                {
                    _session = session;
                }

                _audioBuffer.ClearBuffer();
                _spectrumAnalyzer.Start();
                SetState(PlaybackState.Buffering);
                Log.Information(
                    "播放阶段：FFmpeg 已启动，等待 {PrebufferMilliseconds} ms PCM 音频缓冲",
                    PrebufferMilliseconds);
                session.Completion = MonitorPlaybackAsync(session);
            }
            catch
            {
                session?.CancelAndKill();
                if (session is not null)
                {
                    DetachCurrentSession(session);
                }

                session?.Dispose();
                process.Dispose();
                _audioOutput?.Stop();
                _audioBuffer.ClearBuffer();
                _spectrumAnalyzer.Stop();
                SetState(PlaybackState.Error);
                throw;
            }
        }
        finally
        {
            _lifecycle.Release();
        }
    }

    public async Task StopAsync()
    {
        await _lifecycle.WaitAsync().ConfigureAwait(false);
        try
        {
            await StopCoreAsync().ConfigureAwait(false);
        }
        finally
        {
            _lifecycle.Release();
        }
    }

    public void SetVolume(int volume)
    {
        _volume = Math.Clamp(volume, 0, 100);
        _volumeProvider.Volume = _muted ? 0 : _volume / 100f;
    }

    public void ToggleMute()
    {
        _muted = !_muted;
        _volumeProvider.Volume = _muted ? 0 : _volume / 100f;
    }

    public void SetNetworkProxy(NetworkProxyMode mode, string? manualProxyUrl = null)
    {
        lock (_sync)
        {
            _networkProxyMode = Enum.IsDefined(mode) ? mode : NetworkProxyMode.Disabled;
            _manualProxyUrl = manualProxyUrl?.Trim() ?? string.Empty;
        }
    }

    private async Task StopCoreAsync()
    {
        PlaybackProcessSession? session;
        lock (_sync)
        {
            session = _session;
            _session = null;
        }

        if (session is not null)
        {
            session.CancelAndKill();
            try
            {
                await session.Completion.ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
            }
            finally
            {
                session.Dispose();
            }
        }

        _audioOutput?.Stop();
        _audioBuffer.ClearBuffer();
        _spectrumAnalyzer.Stop();
        SetState(PlaybackState.Stopped);
    }

    private async Task MonitorPlaybackAsync(PlaybackProcessSession session)
    {
        var startedAt = DateTimeOffset.Now;
        try
        {
            var pcmTask = PumpPcmAsync(session, startedAt);
            var logTask = PumpLogAsync(session);
            await session.Process.WaitForExitAsync(session.Token).ConfigureAwait(false);
            await Task.WhenAll(pcmTask, logTask).ConfigureAwait(false);

            if (IsCurrentSession(session))
            {
                if (session.Process.ExitCode == 0)
                {
                    SetState(PlaybackState.Stopped);
                }
                else
                {
                    Log.Error(
                        "FFmpeg 音频解码器异常退出，代码 {ExitCode}。最近输出：{Output}",
                        session.Process.ExitCode,
                        session.RecentLog);
                    SetState(PlaybackState.Error);
                }
            }
        }
        catch (OperationCanceledException) when (session.Token.IsCancellationRequested)
        {
            if (IsCurrentSession(session))
            {
                SetState(PlaybackState.Stopped);
            }
        }
        catch (Exception exception)
        {
            Log.Error(exception, "读取 FFmpeg 音频输出失败");
            if (IsCurrentSession(session))
            {
                SetState(PlaybackState.Error);
            }
        }
        finally
        {
            var wasCurrent = DetachCurrentSession(session);
            if (wasCurrent)
            {
                _audioOutput?.Stop();
                _audioBuffer.ClearBuffer();
                _spectrumAnalyzer.Stop();
            }

            session.Dispose();
        }
    }

    private async Task PumpPcmAsync(PlaybackProcessSession session, DateTimeOffset startedAt)
    {
        var buffer = new byte[16 * 1024];
        var receivedFirstFrame = false;
        while (true)
        {
            var read = await session.Process.StandardOutput.BaseStream
                .ReadAsync(buffer, session.Token)
                .ConfigureAwait(false);
            if (read == 0)
            {
                return;
            }

            if (!receivedFirstFrame)
            {
                receivedFirstFrame = true;
                Log.Information(
                    "播放阶段：收到首个 PCM 音频帧，距启动 FFmpeg {Elapsed:F1} 秒",
                    (DateTimeOffset.Now - startedAt).TotalSeconds);
                if (IsCurrentSession(session))
                {
                    Log.Information("播放阶段：收到首个 PCM 音频帧，开始填充播放缓冲");
                }
            }

            _audioBuffer.AddSamples(buffer, 0, read);
            _spectrumAnalyzer.PushPcm16Stereo(buffer, read);
            StartAudioOutputWhenBuffered(session);
        }
    }

    private void StartAudioOutputWhenBuffered(PlaybackProcessSession session)
    {
        if (_audioBuffer.BufferedBytes < PrebufferBytes)
        {
            return;
        }

        lock (_sync)
        {
            if (!ReferenceEquals(_session, session) || !session.TryMarkAudioOutputStarted())
            {
                return;
            }

            _audioOutput!.Play();
        }

        Log.Information(
            "播放阶段：PCM 缓冲已达到 {PrebufferMilliseconds} ms，启动音频输出",
            PrebufferMilliseconds);
        SetState(PlaybackState.Playing);
    }

    private static async Task PumpLogAsync(PlaybackProcessSession session)
    {
        while (await session.Process.StandardError.ReadLineAsync(session.Token).ConfigureAwait(false) is { } line)
        {
            var sanitized = FfmpegLogSanitizer.Sanitize(line);
            session.AddLog(sanitized);
            Log.Warning("FFmpeg {Message}", sanitized);
        }
    }

    private bool IsCurrentSession(PlaybackProcessSession session)
    {
        lock (_sync)
        {
            return ReferenceEquals(_session, session);
        }
    }

    private bool DetachCurrentSession(PlaybackProcessSession session)
    {
        lock (_sync)
        {
            if (!ReferenceEquals(_session, session))
            {
                return false;
            }

            _session = null;
            return true;
        }
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

    private void EnsureAudioOutput()
    {
        if (_audioOutput is not null)
        {
            return;
        }

        var output = new WasapiOut(AudioClientShareMode.Shared, useEventSync: true, latency: 100);
        output.Init(_volumeProvider);
        _audioOutput = output;
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _lifecycle.Wait();
        try
        {
            if (_disposed)
            {
                return;
            }

            StopCoreAsync().GetAwaiter().GetResult();
            _disposed = true;
            _audioOutput?.Dispose();
            _spectrumAnalyzer.Dispose();
        }
        finally
        {
            _lifecycle.Release();
            _lifecycle.Dispose();
        }
    }
}

internal sealed class PlaybackProcessSession : IDisposable
{
    private const int RecentLogLimit = 8;
    private readonly object _logSync = new();
    private readonly Queue<string> _recentLog = new();
    private readonly CancellationTokenSource _cancellation;
    private readonly CancellationTokenRegistration _cancellationRegistration;
    private int _audioOutputStarted;
    private int _disposed;

    public PlaybackProcessSession(Process process, CancellationToken cancellationToken)
    {
        Process = process;
        _cancellation = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        _cancellationRegistration = _cancellation.Token.Register(TryKill);
    }

    public Process Process { get; }
    public CancellationToken Token => _cancellation.Token;
    public Task Completion { get; set; } = Task.CompletedTask;

    public string RecentLog
    {
        get
        {
            lock (_logSync)
            {
                return string.Join(" | ", _recentLog);
            }
        }
    }

    public void AddLog(string line)
    {
        lock (_logSync)
        {
            _recentLog.Enqueue(line);
            while (_recentLog.Count > RecentLogLimit)
            {
                _recentLog.Dequeue();
            }
        }
    }

    public void CancelAndKill()
    {
        _cancellation.Cancel();
        TryKill();
    }

    public bool TryMarkAudioOutputStarted() =>
        Interlocked.CompareExchange(ref _audioOutputStarted, 1, 0) == 0;

    private void TryKill()
    {
        try
        {
            if (!Process.HasExited)
            {
                Process.Kill(entireProcessTree: true);
            }
        }
        catch (InvalidOperationException)
        {
        }
        catch (System.ComponentModel.Win32Exception)
        {
        }
    }

    public void Dispose()
    {
        if (Interlocked.Exchange(ref _disposed, 1) != 0)
        {
            return;
        }

        _cancellationRegistration.Dispose();
        _cancellation.Dispose();
        Process.Dispose();
    }
}

internal static class FfmpegProcess
{
    public const string ExecutableName = "ffmpeg-aac.exe";
    private const int SampleRate = 48_000;
    private const int Channels = 2;

    public static ProcessStartInfo CreateStartInfo(
        string executablePath,
        StreamDescriptor stream,
        Uri? proxy = null)
    {
        var startInfo = new ProcessStartInfo
        {
            FileName = executablePath,
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true
        };
        string[] arguments =
        [
            "-hide_banner",
            "-loglevel", "warning",
            "-nostats",
            "-nostdin",
            "-user_agent", BiliHttp.DesktopBrowserUserAgent,
            "-referer", $"https://live.bilibili.com/{stream.RoomId}",
        ];

        foreach (var argument in arguments)
        {
            startInfo.ArgumentList.Add(argument);
        }

        RemoveInheritedProxyEnvironment(startInfo);
        if (proxy is not null)
        {
            startInfo.ArgumentList.Add("-http_proxy");
            startInfo.ArgumentList.Add(proxy.AbsoluteUri);
        }

        string[] inputAndOutputArguments =
        [
            "-rw_timeout", "15000000",
            "-reconnect", "1",
            "-reconnect_streamed", "1",
            "-reconnect_delay_max", "3",
            "-probesize", "32768",
            "-analyzeduration", "0",
            "-i", stream.Url.AbsoluteUri,
            "-map", "0:a:0",
            "-vn",
            "-ac", Channels.ToString(System.Globalization.CultureInfo.InvariantCulture),
            "-ar", SampleRate.ToString(System.Globalization.CultureInfo.InvariantCulture),
            "-c:a", "pcm_s16le",
            "-f", "s16le",
            "pipe:1"
        ];
        foreach (var argument in inputAndOutputArguments)
        {
            startInfo.ArgumentList.Add(argument);
        }

        return startInfo;
    }

    private static void RemoveInheritedProxyEnvironment(ProcessStartInfo startInfo)
    {
        string[] proxyVariables =
        [
            "HTTP_PROXY",
            "HTTPS_PROXY",
            "ALL_PROXY",
            "NO_PROXY",
            "http_proxy",
            "https_proxy",
            "all_proxy",
            "no_proxy"
        ];
        foreach (var variable in proxyVariables)
        {
            startInfo.Environment.Remove(variable);
        }
    }
}

internal static class NetworkProxy
{
    public static Uri? Resolve(
        Uri destination,
        NetworkProxyMode mode,
        string? manualProxyUrl)
    {
        if (mode == NetworkProxyMode.Disabled)
        {
            return null;
        }

        if (mode == NetworkProxyMode.Manual)
        {
            if (TryParseManual(manualProxyUrl, out var manualProxy))
            {
                return manualProxy;
            }

            Log.Warning("手动代理 URL 无效，本次播放使用直连");
            return null;
        }

        return ResolveSystem(destination);
    }

    public static bool TryParseManual(string? value, out Uri? proxy)
    {
        if (Uri.TryCreate(value?.Trim(), UriKind.Absolute, out var parsed)
            && parsed.Scheme is "http" or "https"
            && !string.IsNullOrEmpty(parsed.Host))
        {
            proxy = parsed;
            return true;
        }

        proxy = null;
        return false;
    }

    private static Uri? ResolveSystem(Uri destination)
    {
        try
        {
            var systemProxy = HttpClient.DefaultProxy;
            if (systemProxy.IsBypassed(destination))
            {
                return null;
            }

            var proxy = systemProxy.GetProxy(destination);
            if (proxy is null || proxy == destination)
            {
                return null;
            }

            if (proxy.Scheme is not ("http" or "https"))
            {
                Log.Warning("FFmpeg 不支持系统代理协议 {Scheme}，本次播放使用直连", proxy.Scheme);
                return null;
            }

            return proxy;
        }
        catch (Exception exception)
        {
            Log.Warning(exception, "解析系统网络代理失败，本次播放使用直连");
            return null;
        }
    }
}

internal static partial class FfmpegLogSanitizer
{
    [GeneratedRegex(
        "(?i)(cookie|authorization)(?:\\s+header)?\\s*:?\\s*.*$",
        RegexOptions.CultureInvariant)]
    private static partial Regex CredentialHeaderPattern();

    [GeneratedRegex(
        "(?i)(https?://[^?\\s]+)\\?[^\\s]+",
        RegexOptions.CultureInvariant)]
    private static partial Regex SignedUrlPattern();

    [GeneratedRegex(
        "(?i)((?:GET|HEAD)\\s+[^?\\s]+)\\?+[^\\s]+",
        RegexOptions.CultureInvariant)]
    private static partial Regex SignedRequestPathPattern();

    [GeneratedRegex(
        "(?i)((?:token|csrf|sessdata|bili_jct)=)[^&;\\s]+",
        RegexOptions.CultureInvariant)]
    private static partial Regex CredentialValuePattern();

    public static string Sanitize(string message)
    {
        var sanitized = CredentialHeaderPattern().Replace(message, "$1: <redacted>");
        sanitized = SignedUrlPattern().Replace(sanitized, "$1?<redacted>");
        sanitized = SignedRequestPathPattern().Replace(sanitized, "$1?<redacted>");
        return CredentialValuePattern().Replace(sanitized, "$1<redacted>");
    }
}
