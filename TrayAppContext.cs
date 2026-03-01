using Microsoft.Win32;
using System.Drawing;
using System.Threading;
using System.Windows.Forms;
using TranslatorTray.Models;
using TranslatorTray.Services;
using TranslatorTray.UI;

namespace TranslatorTray;

public sealed class TrayAppContext : ApplicationContext
{
    private const string RunRegistryName = "TranslatorTray";
    private const string TrayTooltipText = "Translator is running";

    private readonly NotifyIcon _trayIcon;
    private readonly SettingsStore _settingsStore;
    private readonly KeyboardLayoutService _layoutService;
    private readonly LexiconService _lexiconService;
    private readonly HunspellDictionaryService _hunspellDictionaryService;
    private readonly WindowsSpellCheckerService _windowsSpellChecker;
    private readonly ProtectedTermsService _protectedTermsService;
    private readonly PuntoDataService _puntoDataService;
    private readonly TextTransformService _transformService;
    private readonly SpellCorrectionService _spellCorrectionService;
    private readonly ToneService _toneService;
    private readonly InputSimulator _inputSimulator;
    private readonly ClipboardTextActions _clipboardActions;
    private readonly GlobalHotkeyWindow _hotkeys;
    private readonly KeyboardHookService _hook;
    private readonly SynchronizationContext _uiContext;

    private AppSettings _settings;
    private ToolStripMenuItem _autoSwitchMenuItem = null!;
    private ToolStripMenuItem _statusMenuItem = null!;
    private int _textActionBusy;
    private int _autoReplaceBusy;

    public TrayAppContext()
    {
        _uiContext = SynchronizationContext.Current ?? new WindowsFormsSynchronizationContext();
        _settingsStore = new SettingsStore();
        _settings = _settingsStore.Load();

        _layoutService = new KeyboardLayoutService();
        _lexiconService = new LexiconService(AppContext.BaseDirectory);
        _hunspellDictionaryService = new HunspellDictionaryService(AppContext.BaseDirectory);
        _windowsSpellChecker = new WindowsSpellCheckerService();
        _protectedTermsService = new ProtectedTermsService(AppContext.BaseDirectory);
        _puntoDataService = new PuntoDataService(AppContext.BaseDirectory);
        _transformService = new TextTransformService(_lexiconService, _puntoDataService, _protectedTermsService);
        _spellCorrectionService = new SpellCorrectionService(_lexiconService, _hunspellDictionaryService, _windowsSpellChecker, _protectedTermsService);
        _toneService = new ToneService(AppContext.BaseDirectory);
        _inputSimulator = new InputSimulator();
        _clipboardActions = new ClipboardTextActions(_inputSimulator, _transformService);
        _hotkeys = new GlobalHotkeyWindow();
        _hook = new KeyboardHookService(_layoutService, _transformService, _spellCorrectionService, _inputSimulator, () => _settings);
        _hook.Status += message =>
        {
            _uiContext.Post(_ =>
            {
                if (message.StartsWith("Switched", StringComparison.Ordinal) ||
                    message.StartsWith("Auto converted", StringComparison.Ordinal))
                {
                    PlayLayoutSwitchSound();
                }
                else if (message.StartsWith("Auto corrected", StringComparison.Ordinal))
                {
                    PlayTextActionSound();
                }

                OnStatus(message, showBalloon: false);
            }, null);
        };
        _hook.ToggleAutoSwitchRequested += () => _uiContext.Post(_ => ToggleAutoSwitchFromHotkey(), null);
        _hook.ConvertSelectionRequested += () => _uiContext.Post(_ => RunClipboardAction(_clipboardActions.ConvertSelectedTextLayout, "Selection layout converted"), null);
        _hook.InvertSelectionRequested += () => _uiContext.Post(_ => RunClipboardAction(_clipboardActions.InvertSelectedTextCase, "Selection case inverted"), null);
        _hook.AutoReplaceRequested += request => _uiContext.Post(_ => RunAutoReplacement(request), null);
        _hook.Start();

        _trayIcon = new NotifyIcon
        {
            Icon = AppIconService.LoadTrayIcon(AppContext.BaseDirectory),
            Visible = true,
            Text = TrayTooltipText
        };

        _trayIcon.ContextMenuStrip = BuildMenu();
        _trayIcon.DoubleClick += (_, _) => OpenSettings();

        RegisterGlobalHotkeys();
        RefreshMenuState();
        OnStatus(
            $"Translator Tray started (lex EN:{_lexiconService.EnglishCount} RU:{_lexiconService.RussianCount}; punto map:{_puntoDataService.EnRuCount + _puntoDataService.RuEnCount} trig:{_puntoDataService.TriggerCount})",
            showBalloon: false);
    }

    private ContextMenuStrip BuildMenu()
    {
        var menu = new ContextMenuStrip();
        _statusMenuItem = new ToolStripMenuItem("Status: Ready") { Enabled = false };
        _autoSwitchMenuItem = new ToolStripMenuItem("Auto switch", null, (_, _) =>
        {
            _settings.AutoSwitchEnabled = !_settings.AutoSwitchEnabled;
            SaveSettings();
            RefreshMenuState();
            PlayAutoToggleSound();
            OnStatus($"Auto switch: {(_settings.AutoSwitchEnabled ? "ON" : "OFF")}");
        });

        menu.Items.Add(_statusMenuItem);
        menu.Items.Add(new ToolStripSeparator());
        menu.Items.Add(_autoSwitchMenuItem);
        menu.Items.Add(new ToolStripMenuItem("Switch RU/EN now", null, (_, _) => ManualSwitchRuEn()));
        menu.Items.Add(new ToolStripMenuItem("Convert selected text layout", null, (_, _) => RunClipboardAction(_clipboardActions.ConvertSelectedTextLayout, "Selection layout converted")));
        menu.Items.Add(new ToolStripMenuItem("Invert case of selected text", null, (_, _) => RunClipboardAction(_clipboardActions.InvertSelectedTextCase, "Selection case inverted")));
        menu.Items.Add(new ToolStripMenuItem("Open settings", null, (_, _) => OpenSettings()));
        menu.Items.Add(new ToolStripSeparator());
        menu.Items.Add(new ToolStripMenuItem("Exit", null, (_, _) => ExitThread()));
        return menu;
    }

    private void RegisterGlobalHotkeys()
    {
        _hotkeys.UnregisterAll();
        // Modifier-based global hotkeys are processed by KeyboardHookService.
    }

    private void RunClipboardAction(Func<bool> action, string successMessage)
    {
        if (Interlocked.Exchange(ref _textActionBusy, 1) == 1)
        {
            return;
        }

        var ok = action();
        try
        {
            if (ok)
            {
                PlayTextActionSound();
                OnStatus(successMessage);
            }
            else
            {
                OnStatus("Text action failed", showBalloon: false);
            }
        }
        finally
        {
            Interlocked.Exchange(ref _textActionBusy, 0);
        }
    }

    private void ManualSwitchRuEn()
    {
        if (_layoutService.SwitchRuEn())
        {
            _hook.NotifyManualSwitch();
            PlayLayoutSwitchSound();
            OnStatus("Switched RU/EN", showBalloon: false);
        }
    }

    private void RunAutoReplacement(KeyboardHookService.AutoReplaceRequest request)
    {
        if (Interlocked.Exchange(ref _autoReplaceBusy, 1) == 1)
        {
            return;
        }

        try
        {
            // Let the delimiter key settle in the target app before replacement.
            Thread.Sleep(90);

            var ok = _clipboardActions.ReplaceLastWord(request.SourceWord.Length, request.ConvertedWord + request.Delimiter);
            if (ok && request.SwitchRuEnOnly && (request.Target is LanguageScript.Russian or LanguageScript.English))
            {
                Thread.Sleep(10);
                _layoutService.SwitchTo(request.Target);
            }
            if (ok)
            {
                if (request.Kind == KeyboardHookService.AutoReplaceKind.LayoutConversion)
                {
                    PlayLayoutSwitchSound();
                    OnStatus($"Auto converted: {request.SourceWord} -> {request.ConvertedWord}", showBalloon: false);
                }
                else
                {
                    PlayTextActionSound();
                    OnStatus($"Auto corrected: {request.SourceWord} -> {request.ConvertedWord}", showBalloon: false);
                }
            }
            else
            {
                OnStatus("Auto replace failed", showBalloon: false);
            }
        }
        finally
        {
            Interlocked.Exchange(ref _autoReplaceBusy, 0);
        }
    }

    private void OpenSettings()
    {
        using var form = new SettingsForm(CloneSettings(_settings), _layoutService, _toneService);
        if (form.ShowDialog() != DialogResult.OK)
        {
            return;
        }

        _settings = CloneSettings(form.Settings);
        ApplyStartupSetting();
        RegisterGlobalHotkeys();
        SaveSettings();
        RefreshMenuState();
        OnStatus("Settings saved");
    }

    private static AppSettings CloneSettings(AppSettings source)
    {
        return new AppSettings
        {
            AutoSwitchEnabled = source.AutoSwitchEnabled,
            AutoCorrectEnabled = source.AutoCorrectEnabled,
            SuspendAutoSwitchUntilDelimiterAfterManualSwitch = source.SuspendAutoSwitchUntilDelimiterAfterManualSwitch,
            OneKeySwitchRuEnOnly = source.OneKeySwitchRuEnOnly,
            StartWithWindows = source.StartWithWindows,
            SettingsVersion = source.SettingsVersion,
            PlaySoundOnLayoutSwitch = source.PlaySoundOnLayoutSwitch,
            PlaySoundOnAutoToggle = source.PlaySoundOnAutoToggle,
            PlaySoundOnTextActions = source.PlaySoundOnTextActions,
            LayoutSwitchSound = source.LayoutSwitchSound,
            AutoToggleSound = source.AutoToggleSound,
            TextActionSound = source.TextActionSound,
            LayoutSwitchSoundId = source.LayoutSwitchSoundId,
            AutoToggleSoundId = source.AutoToggleSoundId,
            TextActionSoundId = source.TextActionSoundId,
            OneKeySwitchKey = source.OneKeySwitchKey,
            ToggleAutoSwitchHotkey = CloneHotkey(source.ToggleAutoSwitchHotkey),
            ConvertSelectedTextHotkey = CloneHotkey(source.ConvertSelectedTextHotkey),
            InvertCaseSelectedTextHotkey = CloneHotkey(source.InvertCaseSelectedTextHotkey),
            ManualCycleLayoutCombo = CloneHotkey(source.ManualCycleLayoutCombo),
            ProtectedWords = new HashSet<string>(source.ProtectedWords, StringComparer.OrdinalIgnoreCase)
        };
    }

    private static Hotkey CloneHotkey(Hotkey h) => new(h.Key, h.Ctrl, h.Alt, h.Shift, h.Win);

    private void RefreshMenuState()
    {
        _autoSwitchMenuItem.Checked = _settings.AutoSwitchEnabled;
        _autoSwitchMenuItem.Text = $"Auto switch ({(_settings.AutoSwitchEnabled ? "ON" : "OFF")})";
    }

    private void SaveSettings()
    {
        _settingsStore.Save(_settings);
    }

    private void OnStatus(string message, bool showBalloon = true)
    {
        _statusMenuItem.Text = $"Status: {message}";
        _trayIcon.Text = TrayTooltipText;
        if (showBalloon)
        {
            _trayIcon.ShowBalloonTip(700, "Translator Tray", message, ToolTipIcon.Info);
        }
    }

    private void PlayLayoutSwitchSound()
    {
        if (_settings.PlaySoundOnLayoutSwitch)
        {
            _toneService.Play(_settings.LayoutSwitchSoundId);
        }
    }

    private void PlayAutoToggleSound()
    {
        if (_settings.PlaySoundOnAutoToggle)
        {
            _toneService.Play(_settings.AutoToggleSoundId);
        }
    }

    private void PlayTextActionSound()
    {
        if (_settings.PlaySoundOnTextActions)
        {
            _toneService.Play(_settings.TextActionSoundId);
        }
    }

    private void ToggleAutoSwitchFromHotkey()
    {
        _settings.AutoSwitchEnabled = !_settings.AutoSwitchEnabled;
        SaveSettings();
        RefreshMenuState();
        PlayAutoToggleSound();
        OnStatus($"Auto switch: {(_settings.AutoSwitchEnabled ? "ON" : "OFF")}", showBalloon: false);
    }

    private void ApplyStartupSetting()
    {
        try
        {
            using var key = Registry.CurrentUser.OpenSubKey(@"Software\Microsoft\Windows\CurrentVersion\Run", writable: true);
            if (key is null) return;

            if (_settings.StartWithWindows)
            {
                key.SetValue(RunRegistryName, $"\"{Application.ExecutablePath}\"");
            }
            else
            {
                key.DeleteValue(RunRegistryName, false);
            }
        }
        catch
        {
            // Ignore registry failures; the app remains functional.
        }
    }

    protected override void ExitThreadCore()
    {
        _trayIcon.Visible = false;
        _trayIcon.Dispose();
        _hook.Dispose();
        _hotkeys.Dispose();
        _windowsSpellChecker.Dispose();
        base.ExitThreadCore();
    }

}
