using BiliStreamAudio.Tui.Core;
using Terminal.Gui.ViewBase;
using Terminal.Gui.Views;
using GuiCheckBox = Terminal.Gui.Views.CheckBox;
using GuiCheckState = Terminal.Gui.Views.CheckState;
using GuiLabel = Terminal.Gui.Views.Label;

namespace BiliStreamAudio.Tui.Views;

internal sealed class SettingsWindow : Window
{
    public SettingsWindow(LiveRoomDisplayOptions displayOptions, Action refreshDanmaku)
    {
        Title = "设置";

        var fanMedalToggle = new GuiCheckBox
        {
            Text = "渲染粉丝勋章",
            X = 1,
            Y = 1,
            Value = displayOptions.ShowFanMedals ? GuiCheckState.Checked : GuiCheckState.UnChecked
        };
        fanMedalToggle.ValueChanged += (_, args) =>
        {
            displayOptions.ShowFanMedals = args.NewValue == GuiCheckState.Checked;
            refreshDanmaku();
        };

        Add(
            fanMedalToggle,
            new GuiLabel
            {
                Text = "关闭后，所有弹幕均不显示粉丝勋章。",
                X = 1,
                Y = 3
            });
    }
}
