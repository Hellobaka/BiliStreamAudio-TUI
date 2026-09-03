using BiliStreamAudio.Tui.Core;
using BiliStreamAudio.Tui.Infrastructure;

namespace BiliStreamAudio.Tests;

public sealed class AudioSpectrumAnalyzerTests
{
    [Fact]
    public async Task Analyzer_does_not_sample_or_emit_when_spectrum_is_disabled()
    {
        using var analyzer = new AudioSpectrumAnalyzer();
        var emitted = false;
        analyzer.SpectrumChanged += (_, _) => emitted = true;

        analyzer.Start();
        analyzer.PushPcm16Stereo(CreateStereoSineWave(440, 0.8, 4_096), 4_096 * 4);
        await Task.Delay(TimeSpan.FromMilliseconds(150));

        Assert.False(emitted);
        Assert.Null(analyzer.CurrentSpectrum);
    }

    [Fact]
    public async Task Analyzer_emits_normalized_frequency_bands_from_pcm()
    {
        using var analyzer = new AudioSpectrumAnalyzer();
        var received = new TaskCompletionSource<SpectrumFrame>(TaskCreationOptions.RunContinuationsAsynchronously);
        analyzer.SpectrumChanged += (_, frame) =>
        {
            if (frame.Magnitudes.Count > 0)
            {
                received.TrySetResult(frame);
            }
        };

        analyzer.SetSpectrumEnabled(true);
        analyzer.Start();
        analyzer.PushPcm16Stereo(CreateStereoSineWave(440, 0.8, 4_096), 4_096 * 4);

        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(3));
        var spectrum = await received.Task.WaitAsync(timeout.Token);

        Assert.Equal(64, spectrum.Magnitudes.Count);
        Assert.All(spectrum.Magnitudes, magnitude => Assert.InRange(magnitude, 0f, 1f));
        Assert.True(spectrum.Magnitudes.Max() > 0.5f);
    }

    private static byte[] CreateStereoSineWave(double frequency, double amplitude, int frames)
    {
        var pcm = new byte[frames * 4];
        for (var frame = 0; frame < frames; frame++)
        {
            var sample = (short)(Math.Sin(2 * Math.PI * frequency * frame / 48_000) * amplitude * short.MaxValue);
            var offset = frame * 4;
            pcm[offset] = (byte)sample;
            pcm[offset + 1] = (byte)(sample >> 8);
            pcm[offset + 2] = pcm[offset];
            pcm[offset + 3] = pcm[offset + 1];
        }

        return pcm;
    }
}
