using System.Text.Json.Serialization;
using System.Windows.Forms;

namespace TranslatorTray.Models;

public sealed class AppSettings
{
    public int SettingsVersion { get; set; } = 4;
    public bool AutoSwitchEnabled { get; set; } = true;
    public bool SuspendAutoSwitchUntilDelimiterAfterManualSwitch { get; set; } = true;
    public bool OneKeySwitchRuEnOnly { get; set; } = true;
    public bool StartWithWindows { get; set; } = false;
    public bool PlaySoundOnLayoutSwitch { get; set; } = true;
    public bool PlaySoundOnAutoToggle { get; set; } = true;
    public bool PlaySoundOnTextActions { get; set; } = true;
    public Keys OneKeySwitchKey { get; set; } = Keys.LControlKey;
    // Windows RegisterHotKey does not distinguish left/right modifiers for combos.
    public Hotkey ToggleAutoSwitchHotkey { get; set; } = new(Keys.Menu, ctrl: true);
    public Hotkey ConvertSelectedTextHotkey { get; set; } = new(Keys.ShiftKey, ctrl: true);
    public Hotkey InvertCaseSelectedTextHotkey { get; set; } = new(Keys.CapsLock, ctrl: true);
    public Hotkey ManualCycleLayoutCombo { get; set; } = new(Keys.Menu, ctrl: true, alt: false);
    public HashSet<string> ProtectedWords { get; set; } = new(StringComparer.OrdinalIgnoreCase);

    [JsonIgnore]
    public int Version => 4;
}

public sealed class Hotkey
{
    public Hotkey()
    {
    }

    public Hotkey(Keys key, bool ctrl = false, bool alt = false, bool shift = false, bool win = false)
    {
        Key = key;
        Ctrl = ctrl;
        Alt = alt;
        Shift = shift;
        Win = win;
    }

    public Keys Key { get; set; } = Keys.None;
    public bool Ctrl { get; set; }
    public bool Alt { get; set; }
    public bool Shift { get; set; }
    public bool Win { get; set; }

    public override string ToString()
    {
        if (Key == Keys.None)
        {
            return "Disabled";
        }

        var parts = new List<string>(4);
        if (Ctrl) parts.Add("Ctrl");
        if (Alt) parts.Add("Alt");
        if (Shift) parts.Add("Shift");
        if (Win) parts.Add("Win");

        var keyText = NormalizeKeyText(Key);
        var isDuplicateModifierKey =
            (Ctrl && keyText == "Ctrl") ||
            (Alt && keyText == "Alt") ||
            (Shift && keyText == "Shift");

        if (!isDuplicateModifierKey)
        {
            parts.Add(keyText);
        }

        return string.Join(" + ", parts);
    }

    private static string NormalizeKeyText(Keys key) => key switch
    {
        Keys.ControlKey => "Ctrl",
        Keys.LControlKey => "Left Ctrl",
        Keys.RControlKey => "Right Ctrl",
        Keys.ShiftKey => "Shift",
        Keys.LShiftKey => "Left Shift",
        Keys.RShiftKey => "Right Shift",
        Keys.Menu => "Alt",
        Keys.LMenu => "Left Alt",
        Keys.RMenu => "Right Alt",
        Keys.Capital => "CapsLock",
        _ => key.ToString()
    };
}
