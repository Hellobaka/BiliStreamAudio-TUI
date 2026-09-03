using BiliStreamAudio.Tui.Core;
using LibVLCSharp.Shared;
using NAudio.CoreAudioApi;
using Serilog;
using System.Buffers;
using System.Runtime.InteropServices;
using System.Text.RegularExpressions;
using BufferedWaveProvider = NAudio.Wave.BufferedWaveProvider;
using WasapiOut = NAudio.Wave.WasapiOut;
using WaveFormat = NAudio.Wave.WaveFormat;

namespace BiliStreamAudio.Tui.Infrastructure;

public sealed class AudioPlayer : IAudioPlayer, IAudioSpectrumSource
{
    private const string LiveReferrer = "https://live.bilibili.com/";
    private const int SampleRate = 48_000;
    private const int Channels = 2;
    private const int BitsPerSample = 16;

    private readonly LibVLC _vlc;
    private readonly MediaPlayer _player;
    private readonly BufferedWaveProvider _audioBuffer = new(new WaveFormat(SampleRate, BitsPerSample, Channels))
    {
        BufferDuration = TimeSpan.FromMilliseconds(500),
        DiscardOnBufferOverflow = true,
        ReadFully = true
    };
    private readonly AudioSpectrumAnalyzer _spectrumAnalyzer = new();
    private readonly PlaybackReadiness _readiness = new();
    private PlaybackState _state = PlaybackState.Stopped;
    private int _volume = 70;
    private bool _muted;
    private DateTimeOffset? _fetchStartedAt;
    private bool _bufferingLogged;
    private bool _clockStartedLogged;
    private bool _playingLogged;
    private WasapiOut? _audioOutput;

    public AudioPlayer()
    {
        LibVLCSharp.Shared.Core.Initialize();
        _vlc = new LibVLC(
            "--no-video",
            "--intf=dummy",
            "--no-osd",
            "--quiet",
            "--verbose=2",
            $"--http-user-agent={BiliHttp.DesktopBrowserUserAgent}",
            $"--http-referrer={LiveReferrer}",
            "--http-forward-cookies");
        _vlc.SetUserAgent("BiliStreamAudio-TUI", BiliHttp.DesktopBrowserUserAgent);
        _vlc.Log += OnVlcLog;
        _player = new MediaPlayer(_vlc) { Volume = _volume };
        _player.Playing += OnPlaying;
        _player.Buffering += OnBuffering;
        _player.ESSelected += OnElementaryStreamSelected;
        _player.TimeChanged += OnTimeChanged;
        _player.EncounteredError += (_, _) => SetState(PlaybackState.Error);
        _player.Stopped += (_, _) => SetState(PlaybackState.Stopped);
        _player.SetAudioFormat("S16N", SampleRate, Channels);
        _player.SetAudioCallbacks(
            (data, samples, count, pts) => OnAudioPlay(samples, count),
            (data, pts) => _audioOutput?.Pause(),
            (data, pts) => _audioOutput?.Play(),
            (data, pts) => _audioBuffer.ClearBuffer(),
            data => { });
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
    public SpectrumFrame? CurrentSpectrum => _spectrumAnalyzer.CurrentSpectrum;

    public Task PlayAsync(StreamDescriptor stream, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        _readiness.Reset();
        _fetchStartedAt = DateTimeOffset.Now;
        _bufferingLogged = false;
        _clockStartedLogged = false;
        _playingLogged = false;
        Log.Information(
            "拉取直播流：房间 {RoomId}，{Protocol}/{Format}，编码 {Codec}，画质 {Quality}，预期码率 {Bitrate}",
            stream.RoomId,
            stream.Protocol,
            stream.Format,
            stream.Codec,
            stream.Quality,
            stream.BitrateKbps is { } bitrate ? $"{bitrate} kbps" : "未知");
        using var media = new Media(_vlc, stream.Url);
        foreach (var option in VlcRequestOptions.Create(stream))
        {
            media.AddOption(option);
        }

        _audioBuffer.ClearBuffer();
        _spectrumAnalyzer.Start();
        EnsureAudioOutput().Play();
        if (!_player.Play(media))
        {
            _audioOutput?.Stop();
            _audioBuffer.ClearBuffer();
            _spectrumAnalyzer.Stop();
            throw new InvalidOperationException("音频播放器无法启动直播流。");
        }

        return Task.CompletedTask;
    }

    public Task StopAsync()
    {
        _readiness.Reset();
        _fetchStartedAt = null;
        _bufferingLogged = false;
        _clockStartedLogged = false;
        _playingLogged = false;
        _player.Stop();
        _audioOutput?.Stop();
        _audioBuffer.ClearBuffer();
        _spectrumAnalyzer.Stop();
        return Task.CompletedTask;
    }

    public void SetVolume(int volume)
    {
        _volume = Math.Clamp(volume, 0, 100);
        _player.Volume = _muted ? 0 : _volume;
    }

    public void ToggleMute()
    {
        _muted = !_muted;
        _player.Mute = _muted;
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

    private void OnPlaying(object? sender, EventArgs args)
    {
        if (!_playingLogged)
        {
            _playingLogged = true;
            LogPlaybackTiming("播放器开始播放", _fetchStartedAt);
        }

        if (_readiness.OnPlaying() is { } state)
        {
            SetState(state);
        }
    }

    private void OnBuffering(object? sender, MediaPlayerBufferingEventArgs args)
    {
        if (!_bufferingLogged)
        {
            _bufferingLogged = true;
            LogPlaybackTiming("开始缓冲", _fetchStartedAt);
        }

        SetState(_readiness.OnBuffering(args.Cache));
    }

    private void OnElementaryStreamSelected(object? sender, MediaPlayerESSelectedEventArgs args)
    {
        if (args.Type == TrackType.Audio && _readiness.OnAudioTrackSelected() is { } state)
        {
            SetState(state);
        }
    }

    private void OnTimeChanged(object? sender, MediaPlayerTimeChangedEventArgs args)
    {
        if (args.Time >= 0 && !_clockStartedLogged)
        {
            _clockStartedLogged = true;
            LogPlaybackTiming("播放时钟首次推进", _fetchStartedAt);
        }

        if (_readiness.OnTimeChanged(args.Time) is { } state)
        {
            SetState(state);
        }
    }

    private void LogPlaybackTiming(string phase, DateTimeOffset? startedAt)
    {
        double? elapsed = startedAt is { } start
            ? (DateTimeOffset.Now - start).TotalSeconds
            : null;
        Log.Information(
            "播放阶段：{Phase}，距拉取流 {Elapsed}",
            phase,
            elapsed is { } seconds ? $"{seconds:F1} 秒" : "未知");
    }

    private WasapiOut EnsureAudioOutput()
    {
        if (_audioOutput is not null)
        {
            return _audioOutput;
        }

        var output = new WasapiOut(AudioClientShareMode.Shared, useEventSync: true, latency: 100);
        output.Init(_audioBuffer);
        _audioOutput = output;
        return output;
    }

    private void OnAudioPlay(IntPtr samples, uint count)
    {
        var byteCount = checked((int)(count * Channels * (BitsPerSample / 8)));
        if (byteCount == 0)
        {
            return;
        }

        var managedSamples = ArrayPool<byte>.Shared.Rent(byteCount);
        try
        {
            Marshal.Copy(samples, managedSamples, 0, byteCount);
            _audioBuffer.AddSamples(managedSamples, 0, byteCount);
            _spectrumAnalyzer.PushPcm16Stereo(managedSamples, byteCount);
        }
        catch (Exception exception)
        {
            Log.Error(exception, "写入 LibVLC PCM 音频回调失败");
        }
        finally
        {
            ArrayPool<byte>.Shared.Return(managedSamples);
        }
    }

    private static void OnVlcLog(object? sender, LogEventArgs args)
    {
        var message = VlcLogSanitizer.Sanitize(args.Message);
        switch (args.Level)
        {
            // LibVLC calls its informational level "Notice".
            case LogLevel.Notice:
                Log.Information("LibVLC [{Module}] {Message}", args.Module, message);
                break;
            case LogLevel.Warning:
                Log.Warning("LibVLC [{Module}] {Message}", args.Module, message);
                break;
            case LogLevel.Error:
                Log.Error("LibVLC [{Module}] {Message}", args.Module, message);
                break;
        }
    }

    public void Dispose()
    {
        _vlc.Log -= OnVlcLog;
        _player.Playing -= OnPlaying;
        _player.Buffering -= OnBuffering;
        _player.ESSelected -= OnElementaryStreamSelected;
        _player.TimeChanged -= OnTimeChanged;
        _player.Dispose();
        _vlc.Dispose();
        _audioOutput?.Dispose();
        _spectrumAnalyzer.Dispose();
    }
}

internal sealed class PlaybackReadiness
{
    private bool _audioTrackSelected;
    private bool _clockStarted;
    private bool _playerStarted;

    public void Reset()
    {
        _audioTrackSelected = false;
        _clockStarted = false;
        _playerStarted = false;
    }

    public PlaybackState? OnPlaying()
    {
        _playerStarted = true;
        return ReadyState();
    }

    public PlaybackState OnBuffering(float cache) => _clockStarted
        ? PlaybackState.Playing
        : PlaybackState.Buffering;

    public PlaybackState? OnAudioTrackSelected()
    {
        _audioTrackSelected = true;
        return ReadyState();
    }

    public PlaybackState? OnTimeChanged(long time)
    {
        if (time < 0)
        {
            return null;
        }

        _clockStarted = true;
        return ReadyState();
    }

    private PlaybackState? ReadyState() => _playerStarted && _audioTrackSelected && _clockStarted
        ? PlaybackState.Playing
        : null;
}

internal static partial class VlcLogSanitizer
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

internal static class VlcRequestOptions
{
    public static IReadOnlyList<string> Create(
        StreamDescriptor stream)
    {
        var referrer = $"https://live.bilibili.com/{stream.RoomId}";
        var options = new List<string>
        {
            $":http-user-agent={BiliHttp.DesktopBrowserUserAgent}",
            $":http-referrer={referrer}",
            ":http-forward-cookies",
            ":network-caching=500"
        };

        if (stream.Protocol.Equals("http_hls", StringComparison.OrdinalIgnoreCase))
        {
            options.Add(":adaptive-livedelay=2000");
            options.Add(":adaptive-maxbuffer=2000");
            options.Add(":adaptive-lowlatency=1");
        }

        return options;
    }
}
