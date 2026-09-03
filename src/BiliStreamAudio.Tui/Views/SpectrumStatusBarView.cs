using BiliStreamAudio.Tui.Core;
using System.Text;
using Terminal.Gui.Drawing;
using Terminal.Gui.Text;
using Terminal.Gui.ViewBase;
using GuiAttribute = Terminal.Gui.Drawing.Attribute;
using GuiColor = Terminal.Gui.Drawing.Color;
using GuiView = Terminal.Gui.ViewBase.View;

namespace BiliStreamAudio.Tui.Views;

internal sealed class SpectrumStatusBarView : GuiView
{
    private const int MinimumStatusColumns = 20;
    private const string Separator = " · ";
    private string _statusText = string.Empty;
    private IReadOnlyList<float> _magnitudes = [];
    private int _bandCount = 8;
    private SpectrumColorMode _colorMode = SpectrumColorMode.Rainbow;

    public void SetStatus(string statusText)
    {
        _statusText = statusText;
        SetNeedsDraw();
    }

    public void SetSpectrum(SpectrumFrame? spectrum)
    {
        _magnitudes = spectrum?.Magnitudes.ToArray() ?? [];
        SetNeedsDraw();
    }

    public void SetBandCount(int bandCount)
    {
        _bandCount = Math.Clamp(
            bandCount,
            LiveRoomDisplayOptions.MinimumSpectrumBandCount,
            LiveRoomDisplayOptions.MaximumSpectrumBandCount);
        SetNeedsDraw();
    }

    public void SetColorMode(SpectrumColorMode colorMode)
    {
        _colorMode = colorMode;
        SetNeedsDraw();
    }

    protected override bool OnDrawingContent(DrawContext? context)
    {
        var width = Viewport.Width;
        SetAttribute(new GuiAttribute(GuiColor.None, GuiColor.None));
        for (var column = 0; column < width; column++)
        {
            AddRune(column, 0, new Rune(' '));
        }

        var spectrumWidth = _magnitudes.Count == 0
            ? 0
            : Math.Min(_bandCount, Math.Max(0, width - MinimumStatusColumns - Separator.GetColumns()));
        if (spectrumWidth == 0)
        {
            AddStr(0, 0, FitToColumns(_statusText, width));
            return true;
        }

        var spectrumStart = width - spectrumWidth;
        var statusWidth = spectrumStart - Separator.GetColumns();
        var status = FitToColumns(_statusText, statusWidth);
        AddStr(0, 0, status);
        AddStr(status.GetColumns(), 0, Separator);
        var bars = SpectrumPresentation.Render(_magnitudes, spectrumWidth);
        for (var index = 0; index < bars.Length; index++)
        {
            var color = SpectrumPresentation.GetColor(_colorMode, index, bars.Length);
            SetAttribute(new GuiAttribute(new GuiColor(color.Red, color.Green, color.Blue), GuiColor.None));
            AddRune(spectrumStart + index, 0, new Rune(bars[index]));
        }

        return true;
    }

    private static string FitToColumns(string value, int width)
    {
        if (width <= 0)
        {
            return string.Empty;
        }

        if (value.GetColumns() <= width)
        {
            return value;
        }

        const string ellipsis = "…";
        var contentWidth = Math.Max(0, width - ellipsis.GetColumns());
        var result = new StringBuilder();
        var usedWidth = 0;
        foreach (var rune in value.EnumerateRunes())
        {
            var runeText = rune.ToString();
            var runeWidth = runeText.GetColumns();
            if (usedWidth + runeWidth > contentWidth)
            {
                break;
            }

            result.Append(runeText);
            usedWidth += runeWidth;
        }

        return result.Append(ellipsis).ToString();
    }
}
