using System.Collections.ObjectModel;
using System.Reflection;
using BiliStreamAudio.Tui.Core;
using Terminal.Gui.App;
using Terminal.Gui.Drawing;
using Terminal.Gui.Input;
using Terminal.Gui.Text;
using Terminal.Gui.ViewBase;
using Terminal.Gui.Views;
using GuiAttribute = Terminal.Gui.Drawing.Attribute;
using GuiButton = Terminal.Gui.Views.Button;
using GuiCheckBox = Terminal.Gui.Views.CheckBox;
using GuiCheckState = Terminal.Gui.Views.CheckState;
using GuiColor = Terminal.Gui.Drawing.Color;
using GuiLabel = Terminal.Gui.Views.Label;
using GuiLine = Terminal.Gui.Views.Line;
using GuiListView = Terminal.Gui.Views.ListView;
using GuiMessageBox = Terminal.Gui.Views.MessageBox;
using GuiTextField = Terminal.Gui.Views.TextField;
using GuiView = Terminal.Gui.ViewBase.View;

namespace BiliStreamAudio.Tui.Views;

internal sealed class SettingsWindow : ApplicationWindow
{
    protected override bool SuppressEscape => _isEditingStatusBar;

    private static readonly string[] CategoryNames = ["账户", "通用配置", "状态栏设置", "关于"];
    private const int AboutBoxWidth = 48;

    // Color palette (matching LiveRoomWindow aesthetic)
    private static readonly GuiColor BilibiliPink = new("#fb7299");
    private static readonly GuiColor AccentGold = new("#F2D06B");
    private static readonly GuiColor SoftCyan = new("#8ED8FF");
    private static readonly GuiColor MutedGray = new("#808080");
    private static readonly GuiColor SuccessGreen = new("#50FA7B");
    private static readonly GuiColor WarningOrange = new("#FFB86C");

    private static readonly Scheme HeaderScheme = new(
        new GuiAttribute(AccentGold, GuiColor.None, TextStyle.Bold));
    private static readonly Scheme SubHeaderScheme = new(
        new GuiAttribute(SoftCyan, GuiColor.None, TextStyle.Bold));
    private static readonly Scheme MutedScheme = new(
        new GuiAttribute(MutedGray, GuiColor.None));
    private static readonly Scheme SuccessScheme = new(
        new GuiAttribute(SuccessGreen, GuiColor.None, TextStyle.Bold));
    private static readonly Scheme WarningScheme = new(
        new GuiAttribute(WarningOrange, GuiColor.None, TextStyle.Bold));

    private readonly LiveRoomDisplayOptions _displayOptions;
    private readonly IAudioPlayer _audio;
    private readonly Action _refreshDisplay;
    private readonly IAuthService _auth;
    private readonly ITokenRefreshService _tokenRefresh;
    private readonly ISettingsStore _settingsStore;
    private readonly IApplication _app;
    private readonly Action<bool> _setStatusBarEditing;

    private readonly GuiListView _categoryList;
    private readonly GuiView _contentArea;

    // 账户
    private GuiLabel? _loginStatusLabel;
    private GuiLabel? _refreshStatusLabel;

    // 通用配置 — 屏蔽词编辑器
    private GuiTextField? _blockedWordInput;
    private GuiListView? _blockedWordsListView;
    private ObservableCollection<string> _blockedWordsSource = [];
    private GuiTextField? _spectrumBandCountInput;
    private bool _isEditingStatusBar;
    private int _editingStatusBarRow;
    private List<StatusBarElement> _statusBarDraftFirstRow = [];
    private List<StatusBarElement> _statusBarDraftSecondRow = [];
    private List<StatusBarElement> _statusBarOriginalFirstRow = [];
    private List<StatusBarElement> _statusBarOriginalSecondRow = [];
    private GuiListView? _availableStatusElements;
    private GuiListView? _selectedStatusElements;

    public bool IsTextInputFocused => _blockedWordInput?.HasFocus == true
                                     || _spectrumBandCountInput?.HasFocus == true;

    public SettingsWindow(
        IApplication app,
        LiveRoomDisplayOptions displayOptions,
        IAudioPlayer audio,
        Action refreshDisplay,
        Action<bool> setStatusBarEditing,
        IAuthService auth,
        ITokenRefreshService tokenRefresh,
        ISettingsStore settingsStore)
    {
        Title = " 设置 ";
        _app = app;
        _displayOptions = displayOptions;
        _audio = audio;
        _refreshDisplay = refreshDisplay;
        _setStatusBarEditing = setStatusBarEditing;
        _auth = auth;
        _tokenRefresh = tokenRefresh;
        _settingsStore = settingsStore;

        var saved = settingsStore.Load();
        ApplySettings(saved);

        var categoryHeader = new GuiLabel
        {
            Text = "设置分类  Alt+1-4",
            X = 1,
            Y = 1,
            Width = 17
        };
        categoryHeader.SetScheme(SubHeaderScheme);

        // Keep the navigation anchored to the top so it remains easy to scan on tall terminals.
        _categoryList = new GuiListView
        {
            X = 1,
            Y = 3,
            Width = 17,
            Height = CategoryNames.Length
        };
        _categoryList.SetSource(new ObservableCollection<string>(CategoryNames));
        _categoryList.RowRender += OnCategoryRowRender;
        _categoryList.HasFocusChanged += (_, _) => _categoryList.SetNeedsDraw();
        _categoryList.KeyDown += (_, key) =>
        {
            if (key == Key.CursorRight)
            {
                FocusCurrentPanel();
                key.Handled = true;
            }
        };

        // Vertical separator line
        var separator = new GuiLine
        {
            X = 18,
            Y = 0,
            Orientation = Terminal.Gui.ViewBase.Orientation.Vertical,
            Style = LineStyle.Single,
            SuperViewRendersLineCanvas = false
        };

        // Right content area
        _contentArea = new GuiView
        {
            X = 20,
            Y = 1,
            Width = Dim.Fill(1),
            Height = Dim.Fill(1),
            // A focusable ancestor is required before Terminal.Gui can focus the input controls it owns.
            CanFocus = true
        };

        _categoryList.ValueChanged += (_, args) =>
        {
            if (args.NewValue is { } index)
            {
                if (_isEditingStatusBar && index != 2)
                {
                    _categoryList.SetSelection(2, false);
                    return;
                }

                SwitchCategory(index);
            }
        };
        KeyDown += (_, key) =>
        {
            if (_isEditingStatusBar)
            {
                if (key == Key.S.WithCtrl)
                {
                    SaveStatusBarDraft();
                    key.Handled = true;
                }
                else if (key == Key.Esc)
                {
                    ConfirmExitStatusBarEditor();
                    key.Handled = true;
                }

                return;
            }

            var categoryIndex = key switch
            {
                var value when value == Key.D1.WithAlt => 0,
                var value when value == Key.D2.WithAlt => 1,
                var value when value == Key.D3.WithAlt => 2,
                var value when value == Key.D4.WithAlt => 3,
                _ => -1
            };

            if (categoryIndex < 0)
            {
                return;
            }

            SelectCategory(categoryIndex);
            key.Handled = true;
        };

        Add(categoryHeader, _categoryList, separator, _contentArea);
        _categoryList.SetSelection(0, false);
        SwitchCategory(0);
    }

    private void OnCategoryRowRender(object? sender, ListViewRowEventArgs args)
    {
        if (args.Row == _categoryList.SelectedItem)
        {
            args.RowAttribute = _categoryList.HasFocus
                ? new GuiAttribute(GuiColor.Black, AccentGold, TextStyle.Bold)
                : new GuiAttribute(GuiColor.Black, GuiColor.White, TextStyle.Bold);
        }
        else
        {
            args.RowAttribute = new GuiAttribute(GuiColor.White, GuiColor.None);
        }
    }

    private void ApplySettings(AppSettings settings)
    {
        _displayOptions.ShowDanmaku = settings.ShowDanmaku;
        _displayOptions.ShowSuperChats = settings.ShowSuperChats;
        _displayOptions.ShowGifts = settings.ShowGifts;
        _displayOptions.ShowGuards = settings.ShowGuards;
        _displayOptions.ShowGiftAmount = settings.ShowGiftAmount;
        _displayOptions.ShowFanMedals = settings.ShowFanMedals;
        _displayOptions.SpectrumBandCount = settings.SpectrumBandCount;
        _displayOptions.SpectrumColorMode = Enum.TryParse<SpectrumColorMode>(
            settings.SpectrumColorMode, out var mode) ? mode : SpectrumColorMode.Rainbow;
        _displayOptions.DanmakuBlockedList = [.. settings.DanmakuBlockedList];
        _displayOptions.SyncWordsFromBlockedList();
    }

    private AppSettings BuildCurrentSettings()
    {
        return new AppSettings
        {
            Volume = _audio.Volume,
            ShowDanmaku = _displayOptions.ShowDanmaku,
            ShowSuperChats = _displayOptions.ShowSuperChats,
            ShowGifts = _displayOptions.ShowGifts,
            ShowGuards = _displayOptions.ShowGuards,
            ShowGiftAmount = _displayOptions.ShowGiftAmount,
            ShowFanMedals = _displayOptions.ShowFanMedals,
            SpectrumBandCount = _displayOptions.SpectrumBandCount,
            SpectrumColorMode = _displayOptions.SpectrumColorMode.ToString(),
            DanmakuBlockedList = [.. _displayOptions.DanmakuBlockedList],
            StatusBarFirstRow = [.. _settingsStore.Load().StatusBarFirstRow],
            StatusBarSecondRow = [.. _settingsStore.Load().StatusBarSecondRow],
            StatusBarLayoutVersion = _settingsStore.Load().StatusBarLayoutVersion
        };
    }

    private void PersistSettings()
    {
        _settingsStore.Save(BuildCurrentSettings());
    }

    private void SwitchCategory(int index)
    {
        _contentArea.RemoveAll();
        switch (index)
        {
            case 0:
                BuildAccountPanel();
                break;
            case 1:
                BuildGeneralPanel();
                break;
            case 2:
                BuildStatusBarPanel();
                break;
            case 3:
                BuildAboutPanel();
                break;
        }

        _contentArea.SetNeedsDraw();
    }

    private void SelectCategory(int index)
    {
        if (_isEditingStatusBar && index != 2)
        {
            return;
        }

        _categoryList.SetSelection(index, false);
        SwitchCategory(index);
        _categoryList.SetFocus();
    }

    private void FocusCurrentPanel()
    {
        _contentArea.SubViews
            .FirstOrDefault(view => view.CanFocus && view.Visible && view.Enabled)
            ?.SetFocus();
    }

    private void ConfigurePanelNavigation(GuiView control)
    {
        control.KeyDown += (_, key) =>
        {
            if (key == Key.CursorLeft)
            {
                // Text fields keep their normal cursor movement until the cursor reaches the left edge.
                if (control is GuiTextField input
                    && (input.InsertionPoint != 0 || input.SelectedLength > 0))
                {
                    return;
                }

                _categoryList.SetFocus();
                key.Handled = true;
            }
            else if (key == Key.CursorRight)
            {
                // Right arrow belongs to text fields so the user can move the insertion point.
                if (control is GuiTextField)
                {
                    return;
                }

                MoveFocusWithinPanel(control, 1);
                key.Handled = true;
            }
            else if (key == Key.CursorUp && control is not GuiListView)
            {
                MoveFocusWithinPanel(control, -1);
                key.Handled = true;
            }
            else if (key == Key.CursorDown && control is not GuiListView)
            {
                MoveFocusWithinPanel(control, 1);
                key.Handled = true;
            }
        };
    }

    private void MoveFocusWithinPanel(GuiView current, int offset)
    {
        var controls = _contentArea.SubViews
            .Where(view => view.CanFocus && view.Visible && view.Enabled)
            .ToList();
        var currentIndex = controls.IndexOf(current);
        if (currentIndex < 0)
        {
            return;
        }

        var targetIndex = currentIndex + offset;
        if (targetIndex < 0)
        {
            _categoryList.SetFocus();
            return;
        }

        if (targetIndex >= controls.Count)
        {
            return;
        }

        controls[targetIndex].SetFocus();
    }

    /// <summary>Creates a concise section header.</summary>
    private static GuiLabel CreateSectionHeader(string title, int y)
    {
        var header = new GuiLabel
        {
            Text = title,
            X = 1,
            Y = y,
            Width = Dim.Fill(2)
        };
        header.SetScheme(HeaderScheme);
        return header;
    }

    /// <summary>Creates a sub-section label with accent color.</summary>
    private static GuiLabel CreateSubHeader(string text, int y)
    {
        var label = new GuiLabel
        {
            Text = text,
            X = 1,
            Y = y,
            Width = Dim.Fill(2)
        };
        label.SetScheme(SubHeaderScheme);
        return label;
    }

    /// <summary>Creates a muted info label.</summary>
    private static GuiLabel CreateInfoLabel(string text, int y)
    {
        var label = new GuiLabel
        {
            Text = text,
            X = 2,
            Y = y,
            Width = Dim.Fill(3)
        };
        label.SetScheme(MutedScheme);
        return label;
    }

    #region 账户

    private void BuildAccountPanel()
    {
        var header = CreateSectionHeader("账户信息", 0);

        var isLoggedIn = _auth.Current?.IsAuthenticated == true;
        var userName = _auth.Current?.UserName;

        // Login status card
        var statusText = isLoggedIn
            ? string.IsNullOrWhiteSpace(userName) ? "已登录" : $"已登录（{userName}）"
            : "未登录";

        _loginStatusLabel = new GuiLabel
        {
            Text = $"登录状态：{statusText}",
            X = 2,
            Y = 2,
            Width = Dim.Fill(3)
        };
        _loginStatusLabel.SetScheme(isLoggedIn ? SuccessScheme : WarningScheme);

        _refreshStatusLabel = new GuiLabel
        {
            Text = $"上次刷新检查：{FormatRefreshDate(_auth.Current?.LastRefreshCheck)}",
            X = 2,
            Y = 4,
            Width = Dim.Fill(3)
        };
        _refreshStatusLabel.SetScheme(MutedScheme);

        var reloginButton = new GuiButton
        {
            Text = isLoggedIn ? "重新登录" : "立即登录",
            X = 2,
            Y = 6,
            Width = 18
        };
        reloginButton.Accepted += (_, _) => _ = ReLoginAsync();
        ConfigurePanelNavigation(reloginButton);

        // Hint text
        var hint = CreateInfoLabel("登录后可发送弹幕、查看关注列表等功能", 8);

        _contentArea.Add(header, _loginStatusLabel, _refreshStatusLabel, reloginButton, hint);
    }

    private async Task ReLoginAsync()
    {
        try
        {
            await _auth.LoginAsync(CancellationToken.None).ConfigureAwait(false);
            var result = await _tokenRefresh
                .RefreshIfNeededAsync(_auth.Current!, CancellationToken.None)
                .ConfigureAwait(false);
            if (result.Success && result.Session is not null)
            {
                await _auth.SaveAsync(result.Session, CancellationToken.None).ConfigureAwait(false);
            }

            _app.Invoke(() =>
            {
                if (_loginStatusLabel is not null)
                {
                    var isLoggedIn = _auth.Current?.IsAuthenticated == true;
                    var userName = _auth.Current?.UserName;
                    var statusText = isLoggedIn
                        ? string.IsNullOrWhiteSpace(userName) ? "已登录" : $"已登录（{userName}）"
                        : "未登录";

                    _loginStatusLabel.Text = $"登录状态：{statusText}";
                    _loginStatusLabel.SetScheme(isLoggedIn ? SuccessScheme : WarningScheme);
                    _loginStatusLabel.SetNeedsDraw();
                }

                if (_refreshStatusLabel is not null)
                {
                    _refreshStatusLabel.Text = $"上次刷新检查：{FormatRefreshDate(_auth.Current?.LastRefreshCheck)}";
                    _refreshStatusLabel.SetNeedsDraw();
                }
            });
        }
        catch
        {
            // Login cancelled or failed — status labels remain unchanged
        }
    }

    private static string FormatRefreshDate(DateOnly? date)
    {
        return date is { } d ? d.ToString("yyyy-MM-dd") : "未检查";
    }

    #endregion

    #region 通用配置

    private void BuildGeneralPanel()
    {
        var header = CreateSectionHeader("通用配置", 0);

        // Display settings group
        var displayHeader = CreateSubHeader("显示设置", 2);
        var showDanmaku = CreateToggle("显示弹幕", _displayOptions.ShowDanmaku,
            v => _displayOptions.ShowDanmaku = v, 3);
        var showGifts = CreateToggle("显示礼物", _displayOptions.ShowGifts,
            v => _displayOptions.ShowGifts = v, 4);
        var showSuperChats = CreateToggle("显示 SC", _displayOptions.ShowSuperChats,
            v => _displayOptions.ShowSuperChats = v, 5);
        var showGuards = CreateToggle("显示舰长", _displayOptions.ShowGuards,
            v => _displayOptions.ShowGuards = v, 6);
        var showGiftAmount = CreateToggle("显示礼物金额", _displayOptions.ShowGiftAmount,
            v => _displayOptions.ShowGiftAmount = v, 7);
        var showFanMedals = CreateToggle("渲染粉丝勋章", _displayOptions.ShowFanMedals,
            v => _displayOptions.ShowFanMedals = v, 8);

        // Separator between display and blocked words
        var separatorLine = new GuiLine
        {
            X = 1,
            Y = 10,
            Width = Dim.Fill(2),
            Style = LineStyle.Single
        };

        // Blocked words section
        var blockedHeader = CreateSubHeader("弹幕屏蔽词", 11);
        var blockedHint = CreateInfoLabel("输入后按 Enter 添加；Del 删除，PgUp/PgDn 排序。", 12);

        _blockedWordInput = new GuiTextField
        {
            X = 2,
            Y = 13,
            Width = Dim.Fill(3)
        };
        _blockedWordInput.KeyDown += (_, key) =>
        {
            if (key == Key.Enter)
            {
                AddBlockedWord();
                key.Handled = true;
            }
        };

        _blockedWordsSource = new ObservableCollection<string>(_displayOptions.DanmakuBlockedList);
        _blockedWordsListView = new GuiListView
        {
            X = 2,
            Y = 15,
            Width = Dim.Fill(3),
            Height = Dim.Fill(3),
            BorderStyle = LineStyle.Single,
            Title = "已添加的屏蔽词",
            ViewportSettings = ViewportSettingsFlags.HasVerticalScrollBar
        };
        _blockedWordsListView.SetSource(_blockedWordsSource);
        _blockedWordsListView.KeyDown += (_, key) =>
        {
            if (key == Key.DeleteChar)
            {
                DeleteSelectedBlockedWord();
                key.Handled = true;
            }
            else if (key == Key.PageUp)
            {
                MoveSelectedBlockedWord(-1);
                key.Handled = true;
            }
            else if (key == Key.PageDown)
            {
                MoveSelectedBlockedWord(1);
                key.Handled = true;
            }
        };

        var buttonY = Pos.AnchorEnd(2);
        var deleteButton = new GuiButton
        {
            Text = "删除 (Del)",
            X = 2,
            Y = buttonY,
            Width = 15
        };
        deleteButton.Accepted += (_, _) => DeleteSelectedBlockedWord();

        var moveUpButton = new GuiButton
        {
            Text = "上移 (PgUp)",
            X = 18,
            Y = buttonY,
            Width = 15
        };
        moveUpButton.Accepted += (_, _) => MoveSelectedBlockedWord(-1);

        var moveDownButton = new GuiButton
        {
            Text = "下移 (PgDn)",
            X = 34,
            Y = buttonY,
            Width = 15
        };
        moveDownButton.Accepted += (_, _) => MoveSelectedBlockedWord(1);

        ConfigurePanelNavigation(showDanmaku);
        ConfigurePanelNavigation(showGifts);
        ConfigurePanelNavigation(showSuperChats);
        ConfigurePanelNavigation(showGuards);
        ConfigurePanelNavigation(showGiftAmount);
        ConfigurePanelNavigation(showFanMedals);
        ConfigurePanelNavigation(_blockedWordInput);
        ConfigurePanelNavigation(_blockedWordsListView);
        ConfigurePanelNavigation(deleteButton);
        ConfigurePanelNavigation(moveUpButton);
        ConfigurePanelNavigation(moveDownButton);

        _contentArea.Add(
            header, displayHeader,
            showDanmaku, showGifts, showSuperChats, showGuards, showGiftAmount, showFanMedals,
            separatorLine, blockedHeader, blockedHint,
            _blockedWordInput, _blockedWordsListView,
            deleteButton, moveUpButton, moveDownButton);
    }

    private GuiCheckBox CreateToggle(string text, bool value, Action<bool> update, int y)
    {
        var toggle = new GuiCheckBox
        {
            Text = $"  {text}",
            X = 2,
            Y = y,
            Width = Dim.Fill(3),
            Value = value ? GuiCheckState.Checked : GuiCheckState.UnChecked
        };
        toggle.ValueChanged += (_, args) =>
        {
            update(args.NewValue == GuiCheckState.Checked);
            _refreshDisplay();
            PersistSettings();
        };
        return toggle;
    }

    private void AddBlockedWord()
    {
        if (_blockedWordInput is null)
        {
            return;
        }

        var word = _blockedWordInput.Text.ToString()?.Trim() ?? string.Empty;
        if (string.IsNullOrEmpty(word))
        {
            return;
        }

        if (_displayOptions.DanmakuBlockedList.Contains(word, StringComparer.OrdinalIgnoreCase))
        {
            _blockedWordInput.Text = string.Empty;
            return;
        }

        _displayOptions.DanmakuBlockedList.Add(word);
        _displayOptions.SyncWordsFromBlockedList();
        _blockedWordInput.Text = string.Empty;
        SyncBlockedWordsSource();
        _refreshDisplay();
        PersistSettings();
    }

    private void DeleteSelectedBlockedWord()
    {
        if (_blockedWordsListView?.SelectedItem is not { } index)
        {
            return;
        }

        if (index < 0 || index >= _displayOptions.DanmakuBlockedList.Count)
        {
            return;
        }

        _displayOptions.DanmakuBlockedList.RemoveAt(index);
        _displayOptions.SyncWordsFromBlockedList();
        SyncBlockedWordsSource();
        _refreshDisplay();
        PersistSettings();
    }

    private void MoveSelectedBlockedWord(int direction)
    {
        if (_blockedWordsListView?.SelectedItem is not { } index)
        {
            return;
        }

        var target = index + direction;
        if (index < 0 || index >= _displayOptions.DanmakuBlockedList.Count
            || target < 0 || target >= _displayOptions.DanmakuBlockedList.Count)
        {
            return;
        }

        (_displayOptions.DanmakuBlockedList[index], _displayOptions.DanmakuBlockedList[target]) =
            (_displayOptions.DanmakuBlockedList[target], _displayOptions.DanmakuBlockedList[index]);
        _displayOptions.SyncWordsFromBlockedList();
        SyncBlockedWordsSource();
        _blockedWordsListView.SelectedItem = target;
        _refreshDisplay();
        PersistSettings();
    }

    private void SyncBlockedWordsSource()
    {
        _blockedWordsSource.Clear();
        foreach (var word in _displayOptions.DanmakuBlockedList)
        {
            _blockedWordsSource.Add(word);
        }
    }

    #endregion

    #region 状态栏设置

    private void BuildStatusBarPanel()
    {
        if (_isEditingStatusBar)
        {
            BuildStatusBarEditor();
            return;
        }

        var header = CreateSectionHeader("状态栏设置", 0);

        var settings = _settingsStore.Load();
        var layout = StatusBarLayout.Normalize(
            settings.StatusBarFirstRow,
            settings.StatusBarSecondRow,
            settings.StatusBarLayoutVersion is null);
        var firstSummary = CreateInfoLabel($"第一层：{FormatStatusBarRow(layout.FirstRow)}", 2);
        var secondSummary = CreateInfoLabel($"第二层：{FormatStatusBarRow(layout.SecondRow)}", 3);
        var editButton = new GuiButton
        {
            Text = "编辑状态栏",
            X = 2,
            Y = 5,
            Width = 16
        };
        editButton.Accepted += (_, _) => BeginStatusBarEditing();

        var spectrumLabel = CreateSubHeader("频谱显示", 7);
        var spectrumInputLabel = new GuiLabel
        {
            Text = $"频谱段数（{LiveRoomDisplayOptions.MinimumSpectrumBandCount}-{LiveRoomDisplayOptions.MaximumSpectrumBandCount}）",
            X = 2,
            Y = 8,
            Width = Dim.Fill(3)
        };
        var spectrumInput = _spectrumBandCountInput = new GuiTextField
        {
            Text = _displayOptions.SpectrumBandCount.ToString(),
            X = 2,
            Y = 9,
            Width = 10
        };
        spectrumInput.TextChanged += (_, _) =>
        {
            if (int.TryParse(spectrumInput.Text.ToString(), out var value))
            {
                _displayOptions.SpectrumBandCount = value;
                var normalized = _displayOptions.SpectrumBandCount.ToString();
                if (!string.Equals(spectrumInput.Text.ToString(), normalized, StringComparison.Ordinal))
                {
                    spectrumInput.Text = normalized;
                }

                _refreshDisplay();
                PersistSettings();
            }
        };

        var rainbowToggle = new GuiCheckBox
        {
            Text = "  彩虹色频谱（关闭为单色）",
            X = 2,
            Y = 11,
            Width = Dim.Fill(3),
            Value = _displayOptions.SpectrumColorMode == SpectrumColorMode.Rainbow
                ? GuiCheckState.Checked
                : GuiCheckState.UnChecked
        };
        rainbowToggle.ValueChanged += (_, args) =>
        {
            _displayOptions.SpectrumColorMode = args.NewValue == GuiCheckState.Checked
                ? SpectrumColorMode.Rainbow
                : SpectrumColorMode.SingleColor;
            _refreshDisplay();
            PersistSettings();
        };

        ConfigurePanelNavigation(spectrumInput);
        ConfigurePanelNavigation(rainbowToggle);

        var hint = CreateInfoLabel("编辑布局时会在此显示两行静态预览。", 13);

        ConfigurePanelNavigation(editButton);
        _contentArea.Add(header, firstSummary, secondSummary, editButton, spectrumLabel, spectrumInputLabel, spectrumInput, rainbowToggle, hint);
    }

    private void BeginStatusBarEditing()
    {
        var settings = _settingsStore.Load();
        var layout = StatusBarLayout.Normalize(
            settings.StatusBarFirstRow,
            settings.StatusBarSecondRow,
            settings.StatusBarLayoutVersion is null);
        _statusBarOriginalFirstRow = [.. layout.FirstRow];
        _statusBarOriginalSecondRow = [.. layout.SecondRow];
        _statusBarDraftFirstRow = [.. layout.FirstRow];
        _statusBarDraftSecondRow = [.. layout.SecondRow];
        _editingStatusBarRow = 0;
        _isEditingStatusBar = true;
        _setStatusBarEditing(true);
        SwitchCategory(2);
    }

    private void BuildStatusBarEditor()
    {
        var header = CreateSectionHeader("编辑状态栏布局", 0);
        var hint = CreateInfoLabel("选择一层后添加、移除或排序。Ctrl+S 保存；Esc 退出。", 1);
        var firstPreviewLabel = CreateSubHeader("静态预览 · 第一层", 3);
        var previewFirst = CreateStatusBarPreview(_statusBarDraftFirstRow, 4);
        var secondPreviewLabel = CreateSubHeader("静态预览 · 第二层", 6);
        var previewSecond = CreateStatusBarPreview(_statusBarDraftSecondRow, 7);
        var firstRowButton = new GuiButton
        {
            Text = _editingStatusBarRow == 0 ? "● 第一层" : "○ 第一层",
            X = 2,
            Y = 9,
            Width = 15
        };
        firstRowButton.Accepted += (_, _) => SelectStatusBarEditingRow(0);
        var secondRowButton = new GuiButton
        {
            Text = _editingStatusBarRow == 1 ? "● 第二层" : "○ 第二层",
            X = 18,
            Y = 9,
            Width = 15
        };
        secondRowButton.Accepted += (_, _) => SelectStatusBarEditingRow(1);

        var availableHeader = CreateSubHeader("可用元素", 11);
        var selectedHeader = CreateSubHeader($"当前层已选元素（第{_editingStatusBarRow + 1}层）", 11);
        selectedHeader.X = 32;
        var used = _statusBarDraftFirstRow.Concat(_statusBarDraftSecondRow).ToHashSet();
        var available = StatusBarLayout.AllElements.Where(element => !used.Contains(element)).ToList();
        _availableStatusElements = new GuiListView
        {
            X = 2,
            Y = 12,
            Width = 28,
            Height = 7,
            BorderStyle = LineStyle.Single,
            Title = "未使用",
            ViewportSettings = ViewportSettingsFlags.HasVerticalScrollBar
        };
        _availableStatusElements.SetSource(new ObservableCollection<string>(available.Select(StatusBarLayout.GetDisplayName)));
        _selectedStatusElements = new GuiListView
        {
            X = 32,
            Y = 12,
            Width = Dim.Fill(3),
            Height = 7,
            BorderStyle = LineStyle.Single,
            Title = "当前层",
            ViewportSettings = ViewportSettingsFlags.HasVerticalScrollBar
        };
        _selectedStatusElements.SetSource(new ObservableCollection<string>(GetEditingStatusBarRow().Select(StatusBarLayout.GetDisplayName)));
        _availableStatusElements.KeyDown += (_, key) =>
        {
            if (key == Key.Enter)
            {
                AddSelectedStatusElement();
                key.Handled = true;
            }
        };
        _selectedStatusElements.KeyDown += (_, key) =>
        {
            if (key == Key.DeleteChar)
            {
                RemoveSelectedStatusElement();
                key.Handled = true;
            }
            else if (key == Key.PageUp)
            {
                MoveSelectedStatusElement(-1);
                key.Handled = true;
            }
            else if (key == Key.PageDown)
            {
                MoveSelectedStatusElement(1);
                key.Handled = true;
            }
        };

        var add = CreateStatusBarEditorButton("添加 (Enter)", 2, 20, AddSelectedStatusElement);
        var remove = CreateStatusBarEditorButton("移除 (Del)", 18, 20, RemoveSelectedStatusElement);
        var up = CreateStatusBarEditorButton("上移 (PgUp)", 34, 20, () => MoveSelectedStatusElement(-1));
        var down = CreateStatusBarEditorButton("下移 (PgDn)", 50, 20, () => MoveSelectedStatusElement(1));
        var save = CreateStatusBarEditorButton("保存 (Ctrl+S)", 2, 22, SaveStatusBarDraft);
        var exit = CreateStatusBarEditorButton("退出 (Esc)", 20, 22, ConfirmExitStatusBarEditor);
        foreach (var control in new GuiView[] { firstRowButton, secondRowButton, _availableStatusElements, _selectedStatusElements, add, remove, up, down, save, exit })
        {
            ConfigurePanelNavigation(control);
        }

        _contentArea.Add(header, hint, firstPreviewLabel, previewFirst, secondPreviewLabel, previewSecond, firstRowButton, secondRowButton,
            availableHeader, selectedHeader, _availableStatusElements, _selectedStatusElements, add, remove, up, down, save, exit);
    }

    private SpectrumStatusBarView CreateStatusBarPreview(IReadOnlyList<StatusBarElement> elements, int y)
    {
        var preview = new SpectrumStatusBarView
        {
            X = 2,
            Y = y,
            Width = Dim.Fill(3),
            Height = 1
        };
        preview.SetElements(elements);
        preview.SetContent(StatusBarContent.Preview);
        preview.SetBandCount(_displayOptions.SpectrumBandCount);
        preview.SetColorMode(_displayOptions.SpectrumColorMode);
        preview.SetSpectrum(new SpectrumFrame([0.2f, 0.5f, 0.7f, 0.4f, 0.9f, 0.3f, 0.6f, 0.8f]));
        return preview;
    }

    private GuiButton CreateStatusBarEditorButton(string text, int x, int y, Action action)
    {
        var button = new GuiButton { Text = text, X = x, Y = y, Width = 15 };
        button.Accepted += (_, _) => action();
        return button;
    }

    private void SelectStatusBarEditingRow(int row)
    {
        _editingStatusBarRow = row;
        SwitchCategory(2);
    }

    private List<StatusBarElement> GetEditingStatusBarRow() =>
        _editingStatusBarRow == 0 ? _statusBarDraftFirstRow : _statusBarDraftSecondRow;

    private void AddSelectedStatusElement()
    {
        var index = _availableStatusElements?.SelectedItem ?? -1;
        var used = _statusBarDraftFirstRow.Concat(_statusBarDraftSecondRow).ToHashSet();
        var available = StatusBarLayout.AllElements.Where(candidate => !used.Contains(candidate)).ToList();
        if (index < 0 || index >= available.Count)
        {
            return;
        }

        GetEditingStatusBarRow().Add(available[index]);
        SwitchCategory(2);
    }

    private void RemoveSelectedStatusElement()
    {
        var index = _selectedStatusElements?.SelectedItem ?? -1;
        var row = GetEditingStatusBarRow();
        if (index < 0 || index >= row.Count)
        {
            return;
        }

        row.RemoveAt(index);
        SwitchCategory(2);
    }

    private void MoveSelectedStatusElement(int offset)
    {
        var index = _selectedStatusElements?.SelectedItem ?? -1;
        var row = GetEditingStatusBarRow();
        var target = index + offset;
        if (index < 0 || target < 0 || target >= row.Count)
        {
            return;
        }

        (row[index], row[target]) = (row[target], row[index]);
        SwitchCategory(2);
    }

    private void SaveStatusBarDraft()
    {
        var layout = StatusBarLayout.Normalize(_statusBarDraftFirstRow, _statusBarDraftSecondRow, useDefaultWhenEmpty: false);
        var settings = BuildCurrentSettings();
        settings.StatusBarFirstRow = layout.FirstRow;
        settings.StatusBarSecondRow = layout.SecondRow;
        _settingsStore.Save(settings);
        ExitStatusBarEditing();
    }

    private void ConfirmExitStatusBarEditor()
    {
        var result = GuiMessageBox.Query(_app, "退出状态栏编辑", "是否保存布局修改？", "保存", "放弃", "继续编辑");
        if (result == 0)
        {
            SaveStatusBarDraft();
        }
        else if (result == 1)
        {
            _statusBarDraftFirstRow = [.. _statusBarOriginalFirstRow];
            _statusBarDraftSecondRow = [.. _statusBarOriginalSecondRow];
            ExitStatusBarEditing();
        }
    }

    private void ExitStatusBarEditing()
    {
        _isEditingStatusBar = false;
        _setStatusBarEditing(false);
        SwitchCategory(2);
    }

    private static string FormatStatusBarRow(IReadOnlyList<StatusBarElement> row) =>
        row.Count == 0 ? "（空）" : string.Join("、", row.Select(StatusBarLayout.GetDisplayName));

    #endregion

    #region 关于

    private void BuildAboutPanel()
    {
        var assembly = Assembly.GetEntryAssembly() ?? Assembly.GetExecutingAssembly();
        var version = assembly.GetName().Version?.ToString() ?? "未知";
        var buildDate = File.Exists(assembly.Location)
            ? File.GetCreationTime(assembly.Location).ToString("yyyy-MM-dd HH:mm")
            : "未知";

        var topBorder = new GuiLabel
        {
            Text = CreateAboutBoxBorder('╔', '╗'),
            X = Pos.Center(),
            Y = 0,
            Width = AboutBoxWidth
        };
        topBorder.SetScheme(HeaderScheme);

        var title = CreateAboutBoxLine("BiliStreamAudio-TUI", 1);
        title.SetScheme(new Scheme(new GuiAttribute(BilibiliPink, GuiColor.None, TextStyle.Bold)));

        var subtitle = CreateAboutBoxLine("哔哩哔哩直播间黑听工具", 2);
        subtitle.SetScheme(SubHeaderScheme);

        var bottomBorder = new GuiLabel
        {
            Text = CreateAboutBoxBorder('╚', '╝'),
            X = Pos.Center(),
            Y = 3,
            Width = AboutBoxWidth
        };
        bottomBorder.SetScheme(HeaderScheme);

        // Version info
        var versionLabel = new GuiLabel
        {
            Text = $"版本：v{version}",
            X = 2,
            Y = 5,
            Width = Dim.Fill(3)
        };
        var buildDateLabel = new GuiLabel
        {
            Text = $"构建日期：{buildDate}",
            X = 2,
            Y = 6,
            Width = Dim.Fill(3)
        };

        // Links
        var linksHeader = CreateSubHeader("链接", 8);
        var githubLink = new GuiLabel
        {
            Text = "GitHub：https://github.com/Hellobaka/BiliStreamAudio-TUI",
            X = 2,
            Y = 9,
            Width = Dim.Fill(3)
        };
        var licenseLabel = new GuiLabel
        {
            Text = "许可证：GPL-3.0",
            X = 2,
            Y = 10,
            Width = Dim.Fill(3)
        };

        // Libraries
        var libsHeader = CreateSubHeader("使用的开源库", 12);
        string[] libraries =
        [
            "  • Terminal.Gui — 终端 UI 框架",
            "  • LibVLCSharp / VideoLAN.LibVLC — 音频播放",
            "  • NAudio — Windows 音频输出",
            "  • Microsoft.Web.WebView2 — 登录窗口",
            "  • Serilog — 日志框架",
            "  • LiteDB — 本地数据库",
        ];
        var libY = 13;
        var libLabels = new List<GuiView> { libsHeader };
        foreach (var lib in libraries)
        {
            var libLabel = new GuiLabel
            {
                Text = lib,
                X = 2,
                Y = libY++,
                Width = Dim.Fill(3)
            };
            libLabel.SetScheme(MutedScheme);
            libLabels.Add(libLabel);
        }

        _contentArea.Add(topBorder, title, subtitle, bottomBorder);
        _contentArea.Add(versionLabel, buildDateLabel, linksHeader, githubLink, licenseLabel);
        _contentArea.Add(libLabels.ToArray());
    }

    private static string CreateAboutBoxBorder(char left, char right)
    {
        return $"{left}{new string('═', AboutBoxWidth - 2)}{right}";
    }

    private static GuiLabel CreateAboutBoxLine(string text, int y)
    {
        var contentWidth = AboutBoxWidth - 2;
        var textWidth = text.GetColumns(ignoreLessThanZero: true);
        var leftPadding = Math.Max(0, (contentWidth - textWidth) / 2);
        var rightPadding = Math.Max(0, contentWidth - textWidth - leftPadding);

        return new GuiLabel
        {
            Text = $"║{new string(' ', leftPadding)}{text}{new string(' ', rightPadding)}║",
            X = Pos.Center(),
            Y = y,
            Width = AboutBoxWidth
        };
    }

    #endregion
}
