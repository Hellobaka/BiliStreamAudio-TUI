using BiliStreamAudio.Tui.Core;
using Terminal.Gui.ViewBase;
using Terminal.Gui.Views;
using GuiCheckBox = Terminal.Gui.Views.CheckBox;
using GuiCheckState = Terminal.Gui.Views.CheckState;
using GuiLabel = Terminal.Gui.Views.Label;
using GuiTextField = Terminal.Gui.Views.TextField;

namespace BiliStreamAudio.Tui.Views;

internal sealed class SettingsWindow : Window
{
    public SettingsWindow(LiveRoomDisplayOptions displayOptions, Action refreshDisplay)
    {
        Title = "设置";

        var blockedWordsLabel = new GuiLabel
        {
            Text = "弹幕屏蔽词（用逗号、分号或换行分隔）",
            X = 1,
            Y = 1
        };
        var blockedWordsInput = new GuiTextField
        {
            Text = displayOptions.DanmakuBlockedWords,
            X = 1,
            Y = 2,
            Width = Dim.Fill(2)
        };
        blockedWordsInput.TextChanged += (_, _) =>
        {
            displayOptions.DanmakuBlockedWords = blockedWordsInput.Text.ToString() ?? string.Empty;
            refreshDisplay();
        };

        var showDanmakuToggle = CreateToggle(
            "显示弹幕",
            displayOptions.ShowDanmaku,
            value => displayOptions.ShowDanmaku = value,
            y: 4);
        var showSuperChatsToggle = CreateToggle(
            "显示 SC",
            displayOptions.ShowSuperChats,
            value => displayOptions.ShowSuperChats = value,
            y: 5);
        var showGiftsToggle = CreateToggle(
            "显示礼物",
            displayOptions.ShowGifts,
            value => displayOptions.ShowGifts = value,
            y: 6);
        var showGuardsToggle = CreateToggle(
            "显示上舰",
            displayOptions.ShowGuards,
            value => displayOptions.ShowGuards = value,
            y: 7);
        var fanMedalToggle = new GuiCheckBox
        {
            Text = "渲染粉丝勋章",
            X = 1,
            Y = 8,
            Value = displayOptions.ShowFanMedals ? GuiCheckState.Checked : GuiCheckState.UnChecked
        };
        fanMedalToggle.ValueChanged += (_, args) =>
        {
            displayOptions.ShowFanMedals = args.NewValue == GuiCheckState.Checked;
            refreshDisplay();
        };

        Add(
            blockedWordsLabel,
            blockedWordsInput,
            showDanmakuToggle,
            showSuperChatsToggle,
            showGiftsToggle,
            showGuardsToggle,
            fanMedalToggle,
            new GuiLabel
            {
                Text = "屏蔽词只匹配普通弹幕；关闭后，相应类型的消息不显示。",
                X = 1,
                Y = 10
            });

        GuiCheckBox CreateToggle(string text, bool value, Action<bool> update, int y)
        {
            var toggle = new GuiCheckBox
            {
                Text = text,
                X = 1,
                Y = y,
                Value = value ? GuiCheckState.Checked : GuiCheckState.UnChecked
            };
            toggle.ValueChanged += (_, args) =>
            {
                update(args.NewValue == GuiCheckState.Checked);
                refreshDisplay();
            };
            return toggle;
        }
    }
}
