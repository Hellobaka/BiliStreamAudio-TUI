using BiliStreamAudio.Tui.Core;

namespace BiliStreamAudio.Tests;

public sealed class SpectrumPresentationTests
{
    [Fact]
    public void Spectrum_uses_eight_bands_by_default()
    {
        var options = new LiveRoomDisplayOptions();

        Assert.Equal(8, options.SpectrumBandCount);
        Assert.Equal(SpectrumColorMode.Rainbow, options.SpectrumColorMode);
    }

    [Theory]
    [InlineData(-1, LiveRoomDisplayOptions.MinimumSpectrumBandCount)]
    [InlineData(1000, LiveRoomDisplayOptions.MaximumSpectrumBandCount)]
    public void Spectrum_band_count_stays_within_supported_range(int requested, int expected)
    {
        var options = new LiveRoomDisplayOptions { SpectrumBandCount = requested };

        Assert.Equal(expected, options.SpectrumBandCount);
    }

    [Fact]
    public void Spectrum_render_downsamples_each_band_by_its_peak()
    {
        var output = SpectrumPresentation.Render([0f, 0.1f, 0.2f, 1f], 2);

        Assert.Equal("▁█", output);
    }

    [Fact]
    public void Spectrum_render_expands_a_single_input_band()
    {
        var output = SpectrumPresentation.Render([0.5f], 4);

        Assert.Equal("▅▅▅▅", output);
    }

    [Fact]
    public void Rainbow_colors_cover_the_full_spectrum_evenly()
    {
        var colors = Enumerable.Range(0, 8)
            .Select(index => SpectrumPresentation.GetColor(SpectrumColorMode.Rainbow, index, 8))
            .ToArray();

        Assert.Equal(new SpectrumRgbColor(255, 51, 51), colors[0]);
        Assert.Equal(new SpectrumRgbColor(255, 51, 255), colors[^1]);
        Assert.Equal(8, colors.Distinct().Count());
    }

    [Fact]
    public void Single_color_mode_uses_one_consistent_accent()
    {
        var first = SpectrumPresentation.GetColor(SpectrumColorMode.SingleColor, 0, 8);
        var last = SpectrumPresentation.GetColor(SpectrumColorMode.SingleColor, 7, 8);

        Assert.Equal(new SpectrumRgbColor(109, 213, 250), first);
        Assert.Equal(first, last);
    }
}
