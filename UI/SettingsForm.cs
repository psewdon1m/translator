using System.Windows.Forms;
using TranslatorTray.Models;
using TranslatorTray.Services;

namespace TranslatorTray.UI;

public sealed class SettingsForm : Form
{
    private readonly AppSettings _settings;
    private readonly KeyboardLayoutService _layoutService;
    private readonly ToneService _toneService;
    private readonly IReadOnlyList<SoundOption> _soundOptions;
    private readonly CheckBox _autoSwitch;
    private readonly CheckBox _autoCorrect;
    private readonly CheckBox _suspendAfterManual;
    private readonly CheckBox _ruEnOnly;
    private readonly CheckBox _playLayoutSound;
    private readonly CheckBox _playAutoToggleSound;
    private readonly CheckBox _playTextActionsSound;
    private readonly ComboBox _layoutSoundPreset;
    private readonly ComboBox _autoToggleSoundPreset;
    private readonly ComboBox _textActionSoundPreset;
    private readonly CheckBox _startWithWindows;
    private readonly ComboBox _oneKeyCombo;
    private readonly TextBox _toggleAutoHotkey;
    private readonly TextBox _convertHotkey;
    private readonly TextBox _invertHotkey;
    private readonly TextBox _manualComboHotkey;
    private readonly TextBox _protectedWordInput;
    private readonly ListBox _protectedWords;
    private readonly ListBox _layouts;

    private TextBox? _captureTarget;

    public SettingsForm(AppSettings settings, KeyboardLayoutService layoutService, ToneService toneService)
    {
        _settings = settings;
        _layoutService = layoutService;
        _toneService = toneService;
        _soundOptions = _toneService.GetAvailableSounds();

        Text = "Translator Tray Settings";
        Icon = AppIconService.LoadTrayIcon(AppContext.BaseDirectory);
        Width = 700;
        Height = 620;
        StartPosition = FormStartPosition.CenterScreen;
        KeyPreview = true;

        var panel = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 2,
            RowCount = 15,
            AutoScroll = true,
            Padding = new Padding(12)
        };
        panel.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 38));
        panel.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 62));
        Controls.Add(panel);

        _autoSwitch = AddCheck(panel, "Auto switch after delimiter", _settings.AutoSwitchEnabled);
        _autoCorrect = AddCheck(panel, "Offline autocorrect spelling (safe mode)", _settings.AutoCorrectEnabled);
        _suspendAfterManual = AddCheck(panel, "Suspend auto until delimiter after manual switch", _settings.SuspendAutoSwitchUntilDelimiterAfterManualSwitch);
        _ruEnOnly = AddCheck(panel, "One-key switch only RU/EN", _settings.OneKeySwitchRuEnOnly);
        _playLayoutSound = AddCheck(panel, "Play sound on layout switch", _settings.PlaySoundOnLayoutSwitch);
        _layoutSoundPreset = AddSoundPresetRow(panel, "Layout switch sound", _settings.LayoutSwitchSoundId);
        _playAutoToggleSound = AddCheck(panel, "Play sound on auto-switch toggle", _settings.PlaySoundOnAutoToggle);
        _autoToggleSoundPreset = AddSoundPresetRow(panel, "Auto-switch toggle sound", _settings.AutoToggleSoundId);
        _playTextActionsSound = AddCheck(panel, "Play sound on text actions", _settings.PlaySoundOnTextActions);
        _textActionSoundPreset = AddSoundPresetRow(panel, "Text action sound", _settings.TextActionSoundId);
        _startWithWindows = AddCheck(panel, "Start with Windows", _settings.StartWithWindows);

        _oneKeyCombo = new ComboBox { Dock = DockStyle.Fill, DropDownStyle = ComboBoxStyle.DropDownList };
        _oneKeyCombo.Items.AddRange(new object[] { Keys.LControlKey, Keys.RControlKey, Keys.CapsLock, Keys.F12, Keys.Pause, Keys.Scroll, Keys.RMenu, Keys.None });
        _oneKeyCombo.SelectedItem = _settings.OneKeySwitchKey;
        AddRow(panel, "One-key RU/EN switch key", _oneKeyCombo);

        _toggleAutoHotkey = AddHotkeyBox(panel, "Toggle auto-switch hotkey", _settings.ToggleAutoSwitchHotkey);
        _convertHotkey = AddHotkeyBox(panel, "Convert selected text hotkey", _settings.ConvertSelectedTextHotkey);
        _invertHotkey = AddHotkeyBox(panel, "Invert case hotkey", _settings.InvertCaseSelectedTextHotkey);
        _manualComboHotkey = AddHotkeyBox(panel, "Manual switch combo (for auto-suspend detect)", _settings.ManualCycleLayoutCombo);

        _layouts = new ListBox { Dock = DockStyle.Fill, Height = 90 };
        foreach (var layout in _layoutService.GetInstalledLayouts())
        {
            _layouts.Items.Add(layout.CultureName);
        }
        AddRow(panel, "Installed system layouts", _layouts);

        _protectedWords = new ListBox { Dock = DockStyle.Fill, Height = 100 };
        foreach (var word in _settings.ProtectedWords.OrderBy(x => x))
        {
            _protectedWords.Items.Add(word);
        }

        var protectedPanel = new FlowLayoutPanel { Dock = DockStyle.Fill, AutoSize = true };
        _protectedWordInput = new TextBox { Width = 220 };
        var addProtected = new Button { Text = "Add", Width = 60 };
        addProtected.Click += (_, _) =>
        {
            var word = _protectedWordInput.Text.Trim();
            if (string.IsNullOrEmpty(word)) return;
            if (_settings.ProtectedWords.Add(word))
            {
                _protectedWords.Items.Add(word);
            }
            _protectedWordInput.Clear();
        };
        var removeProtected = new Button { Text = "Remove Selected", Width = 120 };
        removeProtected.Click += (_, _) =>
        {
            if (_protectedWords.SelectedItem is not string item) return;
            _settings.ProtectedWords.Remove(item);
            _protectedWords.Items.Remove(item);
        };
        protectedPanel.Controls.Add(_protectedWordInput);
        protectedPanel.Controls.Add(addProtected);
        protectedPanel.Controls.Add(removeProtected);
        AddRow(panel, "Protected words", protectedPanel);

        AddRow(panel, "Protected words list", _protectedWords);

        var buttons = new FlowLayoutPanel { Dock = DockStyle.Fill, FlowDirection = FlowDirection.RightToLeft };
        var ok = new Button { Text = "Save", Width = 100 };
        var cancel = new Button { Text = "Cancel", Width = 100 };
        ok.Click += (_, _) =>
        {
            ApplyToSettings();
            DialogResult = DialogResult.OK;
            Close();
        };
        cancel.Click += (_, _) =>
        {
            DialogResult = DialogResult.Cancel;
            Close();
        };
        buttons.Controls.Add(ok);
        buttons.Controls.Add(cancel);
        AddRow(panel, string.Empty, buttons);

    }

    public AppSettings Settings => _settings;

    private void HotkeyCapture_KeyDown(object? sender, KeyEventArgs e)
    {
        if (sender is not TextBox tb) return;
        _captureTarget = tb;

        e.SuppressKeyPress = true;
        e.Handled = true;

        if (e.KeyCode == Keys.Escape)
        {
            _captureTarget = null;
            return;
        }

        // Don't finalize capture on a single modifier key press; wait for combo completion.
        if (IsPureModifier(e.KeyCode) && !HasAnotherModifier(e))
        {
            return;
        }

        var hk = new Hotkey(
            e.KeyCode,
            ctrl: e.Control,
            alt: e.Alt,
            shift: e.Shift,
            win: false);
        tb.Tag = hk;
        tb.Text = hk.ToString();
        _captureTarget = null;
    }

    private static bool IsPureModifier(Keys key) =>
        key is Keys.ControlKey or Keys.LControlKey or Keys.RControlKey
            or Keys.ShiftKey or Keys.LShiftKey or Keys.RShiftKey
            or Keys.Menu or Keys.LMenu or Keys.RMenu;

    private static bool HasAnotherModifier(KeyEventArgs e)
    {
        var modifiers = 0;
        if (e.Control) modifiers++;
        if (e.Shift) modifiers++;
        if (e.Alt) modifiers++;
        return modifiers >= 2;
    }

    private CheckBox AddCheck(TableLayoutPanel panel, string label, bool value)
    {
        var cb = new CheckBox { Text = label, Checked = value, Dock = DockStyle.Fill, AutoSize = true };
        panel.Controls.Add(cb);
        panel.SetColumnSpan(cb, 2);
        panel.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        return cb;
    }

    private TextBox AddHotkeyBox(TableLayoutPanel panel, string label, Hotkey initial)
    {
        var tb = new TextBox { Dock = DockStyle.Fill, ReadOnly = true, Text = initial.ToString(), Tag = initial };
        tb.ShortcutsEnabled = false;
        tb.Enter += (_, _) => _captureTarget = tb;
        tb.Click += (_, _) => _captureTarget = tb;
        tb.PreviewKeyDown += HotkeyCapture_PreviewKeyDown;
        tb.KeyDown += HotkeyCapture_KeyDown;
        AddRow(panel, label, tb);
        return tb;
    }

    private static void HotkeyCapture_PreviewKeyDown(object? sender, PreviewKeyDownEventArgs e)
    {
        if (IsPureModifier(e.KeyCode) || e.KeyCode is Keys.CapsLock or Keys.Menu or Keys.ShiftKey or Keys.ControlKey)
        {
            e.IsInputKey = true;
        }
    }

    private void AddRow(TableLayoutPanel panel, string label, Control control)
    {
        panel.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        if (!string.IsNullOrEmpty(label))
        {
            panel.Controls.Add(new Label { Text = label, AutoSize = true, Dock = DockStyle.Fill, TextAlign = ContentAlignment.MiddleLeft });
        }
        else
        {
            panel.Controls.Add(new Label { Text = string.Empty, AutoSize = true });
        }

        panel.Controls.Add(control);
    }

    private ComboBox AddSoundPresetRow(TableLayoutPanel panel, string label, string selectedId)
    {
        var wrapper = new FlowLayoutPanel
        {
            Dock = DockStyle.Fill,
            AutoSize = true,
            WrapContents = false
        };

        var combo = new ComboBox
        {
            Width = 260,
            DropDownStyle = ComboBoxStyle.DropDownList
        };

        foreach (var option in _soundOptions)
        {
            combo.Items.Add(option);
        }

        var selectedIndex = -1;
        for (var i = 0; i < combo.Items.Count; i++)
        {
            if (combo.Items[i] is SoundOption option &&
                string.Equals(option.Id, selectedId, StringComparison.OrdinalIgnoreCase))
            {
                selectedIndex = i;
                break;
            }
        }

        if (selectedIndex >= 0)
        {
            combo.SelectedIndex = selectedIndex;
        }
        else if (combo.Items.Count > 0)
        {
            combo.SelectedIndex = 0;
        }

        var preview = new Button
        {
            Text = "Preview",
            Width = 80
        };
        preview.Click += (_, _) =>
        {
            if (combo.SelectedItem is SoundOption option)
            {
                _toneService.Play(option.Id);
            }
        };

        wrapper.Controls.Add(combo);
        wrapper.Controls.Add(preview);
        AddRow(panel, label, wrapper);
        return combo;
    }

    private void ApplyToSettings()
    {
        _settings.AutoSwitchEnabled = _autoSwitch.Checked;
        _settings.AutoCorrectEnabled = _autoCorrect.Checked;
        _settings.SuspendAutoSwitchUntilDelimiterAfterManualSwitch = _suspendAfterManual.Checked;
        _settings.OneKeySwitchRuEnOnly = _ruEnOnly.Checked;
        _settings.PlaySoundOnLayoutSwitch = _playLayoutSound.Checked;
        _settings.PlaySoundOnAutoToggle = _playAutoToggleSound.Checked;
        _settings.PlaySoundOnTextActions = _playTextActionsSound.Checked;
        _settings.LayoutSwitchSoundId = _layoutSoundPreset.SelectedItem is SoundOption layoutOption ? layoutOption.Id : ToneService.BuiltinLayoutSwitchId;
        _settings.AutoToggleSoundId = _autoToggleSoundPreset.SelectedItem is SoundOption autoOption ? autoOption.Id : ToneService.BuiltinAutoToggleId;
        _settings.TextActionSoundId = _textActionSoundPreset.SelectedItem is SoundOption textOption ? textOption.Id : ToneService.BuiltinTextActionId;
        _settings.StartWithWindows = _startWithWindows.Checked;
        _settings.OneKeySwitchKey = _oneKeyCombo.SelectedItem is Keys k ? k : Keys.RControlKey;
        _settings.ToggleAutoSwitchHotkey = (Hotkey)(_toggleAutoHotkey.Tag ?? _settings.ToggleAutoSwitchHotkey);
        _settings.ConvertSelectedTextHotkey = (Hotkey)(_convertHotkey.Tag ?? _settings.ConvertSelectedTextHotkey);
        _settings.InvertCaseSelectedTextHotkey = (Hotkey)(_invertHotkey.Tag ?? _settings.InvertCaseSelectedTextHotkey);
        _settings.ManualCycleLayoutCombo = (Hotkey)(_manualComboHotkey.Tag ?? _settings.ManualCycleLayoutCombo);
    }
}
