using BiliStreamAudio.Tui.Core;
using System.Text;
using Terminal.Gui.Drawing;
using Terminal.Gui.Text;
using Terminal.Gui.ViewBase;
using GuiAttribute = Terminal.Gui.Drawing.Attribute;
using GuiColor = Terminal.Gui.Drawing.Color;
using GuiView = Terminal.Gui.ViewBase.View;

namespace BiliStreamAudio.Tui.Views;

/// <summary>一个可按用户顺序显示任意状态栏元素的单行视图。</summary>
internal sealed class SpectrumStatusBarView : GuiView
{
    private const string Separator = " · ";
    private IReadOnlyList<StatusBarElement> _elements = [];
    private StatusBarContent _content = StatusBarContent.Preview;
    private IReadOnlyList<float> _magnitudes = [];
    private int _bandCount = 8;
    private SpectrumColorMode _colorMode = SpectrumColorMode.Rainbow;

    public void SetElements(IEnumerable<StatusBarElement> elements)
    {
        _elements = elements.Distinct().ToArray();
        SetNeedsDraw();
    }

    public void SetContent(StatusBarContent content)
    {
        _content = content;
        SetNeedsDraw();
    }

    public void SetSpectrum(SpectrumFrame? spectrum)
    {
        _magnitudes = spectrum?.Magnitudes.ToArray() ?? [];
        SetNeedsDraw();
    }

    public void SetBandCount(int bandCount)
    {
        _bandCount = Math.Clamp(bandCount, LiveRoomDisplayOptions.MinimumSpectrumBandCount, LiveRoomDisplayOptions.MaximumSpectrumBandCount);
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

        var columnOffset = 0;
        foreach (var element in _elements)
        {
            var separatorWidth = columnOffset == 0 ? 0 : Separator.GetColumns();
            var remaining = width - columnOffset - separatorWidth;
            if (remaining <= 0)
            {
                break;
            }

            if (columnOffset > 0)
            {
                AddStr(columnOffset, 0, Separator);
                columnOffset += separatorWidth;
            }

            if (element == StatusBarElement.Spectrum)
            {
                var bands = _magnitudes.Count == 0 ? 0 : Math.Min(_bandCount, remaining);
                if (bands == 0)
                {
                    var placeholder = FitToColumns("频谱：--", remaining);
                    AddStr(columnOffset, 0, placeholder);
                    columnOffset += placeholder.GetColumns();
                }
                else
                {
                    var bars = SpectrumPresentation.Render(_magnitudes, bands);
                    for (var index = 0; index < bars.Length; index++)
                    {
                        var color = SpectrumPresentation.GetColor(_colorMode, index, bars.Length);
                        SetAttribute(new GuiAttribute(new GuiColor(color.Red, color.Green, color.Blue), GuiColor.None));
                        AddRune(columnOffset + index, 0, new Rune(bars[index]));
                    }

                    columnOffset += bars.Length;
                }
            }
            else
            {
                var text = FitToColumns(StatusBarFormatter.Format(element, _content), remaining);
                AddStr(columnOffset, 0, text);
                columnOffset += text.GetColumns();
            }

            if (columnOffset >= width)
            {
                break;
            }
        }

        return true;
    }

    internal static string FitToColumns(string value, int width)
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
        if (width <= ellipsis.GetColumns())
        {
            return ellipsis;
        }

        var result = new StringBuilder();
        var usedWidth = 0;
        foreach (var rune in value.EnumerateRunes())
        {
            var runeText = rune.ToString();
            var runeWidth = runeText.GetColumns();
            if (usedWidth + runeWidth > width - ellipsis.GetColumns())
            {
                break;
            }

            result.Append(runeText);
            usedWidth += runeWidth;
        }

        return result.Append(ellipsis).ToString();
    }
}
