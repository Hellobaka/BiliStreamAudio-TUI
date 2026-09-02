using BiliStreamAudio.Tui.Core;
using LibVLCSharp.Shared;
using Serilog;
using System.Text.RegularExpressions;

namespace BiliStreamAudio.Tui.Infrastructure;

public sealed class AudioPlayer : IAudioPlayer
{
    private const string LiveReferrer = "https://live.bilibili.com/";

    private readonly LibVLC _vlc;
    private readonly MediaPlayer _player;
    private PlaybackState _state = PlaybackState.Stopped;
    private int _volume = 70;
    private bool _muted;

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
        _player.Playing += (_, _) => SetState(PlaybackState.Playing);
        _player.Buffering += (_, _) => SetState(PlaybackState.Buffering);
        _player.EncounteredError += (_, _) => SetState(PlaybackState.Error);
        _player.Stopped += (_, _) => SetState(PlaybackState.Stopped);
    }

    public event EventHandler<PlaybackState>? StateChanged;

    public PlaybackState State => _state;
    public int Volume => _volume;
    public bool IsMuted => _muted;

    public Task PlayAsync(StreamDescriptor stream, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        using var media = new Media(_vlc, stream.Url);
        foreach (var option in VlcRequestOptions.Create(stream))
        {
            media.AddOption(option);
        }

        if (!_player.Play(media))
        {
            throw new InvalidOperationException("LibVLC could not start the audio stream.");
        }

        return Task.CompletedTask;
    }

    public Task StopAsync()
    {
        _player.Stop();
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
        _state = state;
        StateChanged?.Invoke(this, state);
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
        _player.Dispose();
        _vlc.Dispose();
    }
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
            ":http-forward-cookies"
        };

        return options;
    }
}
