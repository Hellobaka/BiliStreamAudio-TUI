namespace BiliStreamAudio.Tui.Core;

internal static class SpectrumPresentation
{
    private const string BarGlyphs = "▁▂▃▄▅▆▇█";

    public static string Render(IReadOnlyList<float> magnitudes, int bandCount)
    {
        if (magnitudes.Count == 0 || bandCount <= 0)
        {
            return string.Empty;
        }

        var bars = new char[bandCount];
        for (var band = 0; band < bandCount; band++)
        {
            var first = band * magnitudes.Count / bandCount;
            var lastExclusive = Math.Max(first + 1, (band + 1) * magnitudes.Count / bandCount);
            var peak = 0f;
            for (var index = first; index < lastExclusive && index < magnitudes.Count; index++)
            {
                peak = Math.Max(peak, magnitudes[index]);
            }

            var glyphIndex = Math.Clamp((int)(Math.Clamp(peak, 0f, 1f) * BarGlyphs.Length), 0, BarGlyphs.Length - 1);
            bars[band] = BarGlyphs[glyphIndex];
        }

        return new string(bars);
    }

    public static SpectrumRgbColor GetColor(SpectrumColorMode mode, int index, int count)
    {
        if (mode == SpectrumColorMode.SingleColor)
        {
            return new SpectrumRgbColor(109, 213, 250);
        }

        var fraction = count <= 1 ? 0d : Math.Clamp(index / (double)(count - 1), 0d, 1d);
        return HsvToRgb(fraction * 300d, 0.8d, 1d);
    }

    private static SpectrumRgbColor HsvToRgb(double hue, double saturation, double value)
    {
        var chroma = value * saturation;
        var secondary = chroma * (1d - Math.Abs(hue / 60d % 2d - 1d));
        var match = value - chroma;
        var (red, green, blue) = hue switch
        {
            < 60d => (chroma, secondary, 0d),
            < 120d => (secondary, chroma, 0d),
            < 180d => (0d, chroma, secondary),
            < 240d => (0d, secondary, chroma),
            < 300d => (secondary, 0d, chroma),
            _ => (chroma, 0d, secondary)
        };

        return new SpectrumRgbColor(
            (byte)Math.Round((red + match) * byte.MaxValue),
            (byte)Math.Round((green + match) * byte.MaxValue),
            (byte)Math.Round((blue + match) * byte.MaxValue));
    }
}

internal readonly record struct SpectrumRgbColor(byte Red, byte Green, byte Blue);
