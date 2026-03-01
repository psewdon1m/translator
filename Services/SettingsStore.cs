using System.Text.Json;
using TranslatorTray.Models;
using System.Windows.Forms;

namespace TranslatorTray.Services;

public sealed class SettingsStore
{
    private const int CurrentSettingsVersion = 7;
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true
    };

    private readonly string _settingsPath;

    public SettingsStore()
    {
        var dir = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "TranslatorTray");
        Directory.CreateDirectory(dir);
        _settingsPath = Path.Combine(dir, "settings.json");
    }

    public string SettingsPath => _settingsPath;

    public AppSettings Load()
    {
        try
        {
            if (!File.Exists(_settingsPath))
            {
                return new AppSettings();
            }

            var json = File.ReadAllText(_settingsPath);
            var settings = JsonSerializer.Deserialize<AppSettings>(json, JsonOptions) ?? new AppSettings();
            var migrated = Migrate(settings);
            if (migrated)
            {
                Save(settings);
            }
            return settings;
        }
        catch
        {
            return new AppSettings();
        }
    }

    public void Save(AppSettings settings)
    {
        settings.SettingsVersion = CurrentSettingsVersion;
        var json = JsonSerializer.Serialize(settings, JsonOptions);
        File.WriteAllText(_settingsPath, json);
    }

    private static bool Migrate(AppSettings settings)
    {
        if (settings.SettingsVersion >= CurrentSettingsVersion)
        {
            return false;
        }

        if (settings.SettingsVersion < 2)
        {
            settings.OneKeySwitchKey = Keys.LControlKey;
            settings.ToggleAutoSwitchHotkey = new Hotkey(Keys.Menu, ctrl: true);
            settings.ConvertSelectedTextHotkey = new Hotkey(Keys.ShiftKey, ctrl: true);
            settings.InvertCaseSelectedTextHotkey = new Hotkey(Keys.CapsLock, ctrl: true);
            settings.SettingsVersion = 2;
        }

        if (settings.SettingsVersion < 3)
        {
            // Migrate old single sound toggle into separate toggles.
            var legacy = true;
            try
            {
                var prop = typeof(AppSettings).GetProperty("PlaySoundOnSwitch");
                if (prop is not null && prop.PropertyType == typeof(bool))
                {
                    legacy = (bool)(prop.GetValue(settings) ?? true);
                }
            }
            catch
            {
                legacy = true;
            }

            settings.PlaySoundOnLayoutSwitch = legacy;
            settings.PlaySoundOnAutoToggle = legacy;
            settings.SettingsVersion = 3;
        }

        if (settings.SettingsVersion < 4)
        {
            settings.PlaySoundOnTextActions = true;
            settings.SettingsVersion = 4;
        }

        if (settings.SettingsVersion < 5)
        {
            settings.LayoutSwitchSound = SoundPreset.LayoutSwitch;
            settings.AutoToggleSound = SoundPreset.AutoToggle;
            settings.TextActionSound = SoundPreset.TextAction;
            settings.SettingsVersion = 5;
        }

        if (settings.SettingsVersion < 6)
        {
            settings.LayoutSwitchSoundId = MapLegacySoundPreset(settings.LayoutSwitchSound, ToneService.BuiltinLayoutSwitchId);
            settings.AutoToggleSoundId = MapLegacySoundPreset(settings.AutoToggleSound, ToneService.BuiltinAutoToggleId);
            settings.TextActionSoundId = MapLegacySoundPreset(settings.TextActionSound, ToneService.BuiltinTextActionId);
            settings.SettingsVersion = 6;
        }

        if (settings.SettingsVersion < 7)
        {
            settings.AutoCorrectEnabled = true;
            settings.SettingsVersion = 7;
        }

        // Keep requested defaults for MVP if user still has old modifier-only captures.
        if (settings.ToggleAutoSwitchHotkey.Key == Keys.ControlKey)
        {
            settings.ToggleAutoSwitchHotkey = new Hotkey(Keys.Menu, ctrl: true);
        }
        if (settings.ConvertSelectedTextHotkey.Key == Keys.ControlKey)
        {
            settings.ConvertSelectedTextHotkey = new Hotkey(Keys.ShiftKey, ctrl: true);
        }
        if (settings.InvertCaseSelectedTextHotkey.Key == Keys.ControlKey)
        {
            settings.InvertCaseSelectedTextHotkey = new Hotkey(Keys.CapsLock, ctrl: true);
        }

        settings.SettingsVersion = CurrentSettingsVersion;
        return true;
    }

    private static string MapLegacySoundPreset(SoundPreset preset, string fallbackId)
    {
        return preset switch
        {
            SoundPreset.LayoutSwitch => ToneService.BuiltinLayoutSwitchId,
            SoundPreset.AutoToggle => ToneService.BuiltinAutoToggleId,
            SoundPreset.TextAction => ToneService.BuiltinTextActionId,
            SoundPreset.SoftClick => ToneService.BuiltinSoftClickId,
            SoundPreset.BrightPing => ToneService.BuiltinBrightPingId,
            SoundPreset.DoubleTick => ToneService.BuiltinDoubleTickId,
            _ => fallbackId
        };
    }
}
