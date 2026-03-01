using System.Runtime.InteropServices;
using System.Windows.Forms;
using TranslatorTray.Models;
using TranslatorTray.Native;

namespace TranslatorTray.Services;

public sealed class KeyboardHookService : IDisposable
{
    private readonly KeyboardLayoutService _layoutService;
    private readonly TextTransformService _transformService;
    private readonly SpellCorrectionService _spellCorrectionService;
    private readonly InputSimulator _input;
    private readonly Func<AppSettings> _settingsProvider;
    private readonly NativeMethods.LowLevelKeyboardProc _proc;
    private readonly object _sync = new();
    private readonly HashSet<uint> _keysDown = new();
    private readonly WordTracker _wordTracker = new(64);
    private readonly Dictionary<HookAction, uint> _armedActions = new();
    private readonly Dictionary<HookAction, DateTime> _lastActionFireUtc = new();

    private IntPtr _hookHandle;
    private bool _disposed;
    private bool _manualSuppressUntilDelimiter;
    private PendingAutoReplacement? _pendingAutoReplacement;

    private bool _oneKeyCandidateActive;
    private bool _oneKeyCandidateCanceled;
    private uint _oneKeyCandidateVk;
    private DateTime _lastOneKeyTriggerUtc = DateTime.MinValue;

    private static readonly HashSet<uint> NavigationVks =
    [
        0x25, // left
        0x26, // up
        0x27, // right
        0x28, // down
        0x21, // page up
        0x22, // page down
        0x23, // end
        0x24, // home
        0x2E  // delete
    ];

    public event Action<string>? Status;
    public event Action? ToggleAutoSwitchRequested;
    public event Action? ConvertSelectionRequested;
    public event Action? InvertSelectionRequested;
    public event Action<AutoReplaceRequest>? AutoReplaceRequested;

    public KeyboardHookService(
        KeyboardLayoutService layoutService,
        TextTransformService transformService,
        SpellCorrectionService spellCorrectionService,
        InputSimulator input,
        Func<AppSettings> settingsProvider)
    {
        _layoutService = layoutService;
        _transformService = transformService;
        _spellCorrectionService = spellCorrectionService;
        _input = input;
        _settingsProvider = settingsProvider;
        _proc = HookCallback;
    }

    public void Start()
    {
        if (_hookHandle != IntPtr.Zero) return;
        _hookHandle = NativeMethods.SetWindowsHookEx(NativeMethods.WH_KEYBOARD_LL, _proc, IntPtr.Zero, 0);
    }

    public void NotifyManualSwitch()
    {
        var settings = _settingsProvider();
        if (!settings.SuspendAutoSwitchUntilDelimiterAfterManualSwitch) return;

        _manualSuppressUntilDelimiter = true;
        Status?.Invoke("Auto switch suspended until delimiter");
    }

    private IntPtr HookCallback(int nCode, IntPtr wParam, IntPtr lParam)
    {
        if (nCode != NativeMethods.HC_ACTION)
        {
            return NativeMethods.CallNextHookEx(_hookHandle, nCode, wParam, lParam);
        }

        var msg = (int)wParam;
        if (msg is not (NativeMethods.WM_KEYDOWN or NativeMethods.WM_SYSKEYDOWN or NativeMethods.WM_KEYUP or NativeMethods.WM_SYSKEYUP))
        {
            return NativeMethods.CallNextHookEx(_hookHandle, nCode, wParam, lParam);
        }

        var data = Marshal.PtrToStructure<NativeMethods.KBDLLHOOKSTRUCT>(lParam);
        if (IsSynthetic(data))
        {
            return NativeMethods.CallNextHookEx(_hookHandle, nCode, wParam, lParam);
        }

        var settings = _settingsProvider();
        var key = NormalizeKey((Keys)data.vkCode);
        var isKeyDown = msg is NativeMethods.WM_KEYDOWN or NativeMethods.WM_SYSKEYDOWN;

        bool firstDown;
        bool wasDownBeforeKeyUp;
        lock (_sync)
        {
            if (isKeyDown)
            {
                firstDown = _keysDown.Add(data.vkCode);
                wasDownBeforeKeyUp = false;
            }
            else
            {
                firstDown = false;
                wasDownBeforeKeyUp = _keysDown.Contains(data.vkCode);
                _keysDown.Remove(data.vkCode);
            }
        }

        if (isKeyDown)
        {
            if (!firstDown)
            {
                return NativeMethods.CallNextHookEx(_hookHandle, nCode, wParam, lParam);
            }

            HandleOneKeyCandidateOnKeyDown(settings, data.vkCode);
            HandleManualCycleCombo(settings, key);
            HandleCustomHotkeysArm(settings, key, data.vkCode);
            HandleWordTrackingAndAutoSwitchOnKeyDown(settings, data, key);
        }
        else if (wasDownBeforeKeyUp)
        {
            HandleAutoReplacementOnKeyUp(data.vkCode);
            HandleCustomHotkeysFireOnKeyUp(data.vkCode);
            HandleOneKeyCandidateOnKeyUp(settings, data.vkCode);
        }

        return NativeMethods.CallNextHookEx(_hookHandle, nCode, wParam, lParam);
    }

    private static bool IsSynthetic(NativeMethods.KBDLLHOOKSTRUCT data)
    {
        if ((data.flags & NativeMethods.LLKHF_INJECTED) == 0)
        {
            return false;
        }

        if (InputSimulator.IsOurSyntheticTag(data.dwExtraInfo))
        {
            return true;
        }

        // External injected events also shouldn't affect our tracking/hotkeys.
        return true;
    }

    private void HandleOneKeyCandidateOnKeyDown(AppSettings settings, uint vkCode)
    {
        if (settings.OneKeySwitchKey == Keys.None)
        {
            return;
        }

        if (vkCode == (uint)settings.OneKeySwitchKey)
        {
            lock (_sync)
            {
                // Candidate can start only when the chosen key is the only pressed key.
                if (_keysDown.Count == 1 && _keysDown.Contains(vkCode))
                {
                    _oneKeyCandidateActive = true;
                    _oneKeyCandidateCanceled = false;
                    _oneKeyCandidateVk = vkCode;
                }
            }
            return;
        }

        CancelOneKeyCandidate();
    }

    private void HandleOneKeyCandidateOnKeyUp(AppSettings settings, uint vkCode)
    {
        bool shouldTrigger = false;

        lock (_sync)
        {
            if (!_oneKeyCandidateActive || vkCode != _oneKeyCandidateVk)
            {
                return;
            }

            shouldTrigger = !_oneKeyCandidateCanceled;
            _oneKeyCandidateActive = false;
            _oneKeyCandidateCanceled = false;
            _oneKeyCandidateVk = 0;
        }

        if (!shouldTrigger)
        {
            return;
        }

        if ((DateTime.UtcNow - _lastOneKeyTriggerUtc).TotalMilliseconds < 20)
        {
            return;
        }

        _lastOneKeyTriggerUtc = DateTime.UtcNow;
        if (_layoutService.SwitchRuEn())
        {
            NotifyManualSwitch();
            Status?.Invoke("Switched RU/EN");
        }
    }

    private void HandleManualCycleCombo(AppSettings settings, Keys key)
    {
        if (!MatchesHotkey(settings.ManualCycleLayoutCombo, key))
        {
            return;
        }

        CancelOneKeyCandidate();
        NotifyManualSwitch();
    }

    private void HandleCustomHotkeysArm(AppSettings settings, Keys key, uint vkCode)
    {
        TryArm(HookAction.ToggleAuto, settings.ToggleAutoSwitchHotkey, key, vkCode);
        TryArm(HookAction.ConvertSelection, settings.ConvertSelectedTextHotkey, key, vkCode);
        TryArm(HookAction.InvertSelection, settings.InvertCaseSelectedTextHotkey, key, vkCode);
    }

    private void TryArm(HookAction action, Hotkey hotkey, Keys actualKey, uint actualVkCode)
    {
        if (!MatchesHotkey(hotkey, actualKey))
        {
            return;
        }

        CancelOneKeyCandidate();
        lock (_sync)
        {
            _armedActions[action] = actualVkCode;
        }
    }

    private void HandleCustomHotkeysFireOnKeyUp(uint vkCode)
    {
        HookAction? fire = null;
        lock (_sync)
        {
            foreach (var pair in _armedActions)
            {
                if (pair.Value == vkCode)
                {
                    fire = pair.Key;
                    break;
                }
            }

            if (fire.HasValue)
            {
                _armedActions.Remove(fire.Value);
            }

            // If any random non-modifier is released after an armed combo, clear stale arms.
            if (!IsModifierVk(vkCode) && fire is null && _armedActions.Count > 0)
            {
                _armedActions.Clear();
            }
        }

        switch (fire)
        {
            case HookAction.ToggleAuto:
                if (IsDebounced(fire.Value, 220)) break;
                FireAfterModifiersRelease(ToggleAutoSwitchRequested);
                break;
            case HookAction.ConvertSelection:
                if (IsDebounced(fire.Value, 900)) break;
                FireAfterModifiersRelease(ConvertSelectionRequested);
                break;
            case HookAction.InvertSelection:
                if (IsDebounced(fire.Value, 900)) break;
                FireAfterModifiersRelease(InvertSelectionRequested);
                break;
        }
    }

    private void HandleWordTrackingAndAutoSwitchOnKeyDown(AppSettings settings, NativeMethods.KBDLLHOOKSTRUCT data, Keys key)
    {
        // External commands alter text state; reset last-word tracking.
        if (IsTextMutatingShortcutStart(data.vkCode))
        {
            _wordTracker.Clear();
            return;
        }

        if (NavigationVks.Contains(data.vkCode))
        {
            _wordTracker.Clear();
            return;
        }

        if (data.vkCode == NativeMethods.VK_BACK)
        {
            _wordTracker.Backspace();
            return;
        }

        var ch = _layoutService.TranslateKeyToChar(data.vkCode, data.scanCode);
        if (ch is null)
        {
            if (data.vkCode is NativeMethods.VK_RETURN or NativeMethods.VK_TAB)
            {
                if (_manualSuppressUntilDelimiter)
                {
                    _manualSuppressUntilDelimiter = false;
                }
                _wordTracker.Clear();
            }
            return;
        }

        var c = ch.Value;
        if (_wordTracker.IsBoundary(c))
        {
            var word = _wordTracker.Consume();

            if (_manualSuppressUntilDelimiter && IsSuspendResetDelimiter(c))
            {
                _manualSuppressUntilDelimiter = false;
                return;
            }

            if (!IsAutoConvertTriggerDelimiter(c) || string.IsNullOrEmpty(word))
            {
                return;
            }

            if (settings.AutoSwitchEnabled &&
                _transformService.TryAutoConvertWord(word, settings.ProtectedWords, out var converted, out var target))
            {
                if (settings.AutoCorrectEnabled &&
                    _spellCorrectionService.TryAutoCorrectWord(converted, target, out var correctedAfterLayout))
                {
                    converted = correctedAfterLayout;
                }

                _pendingAutoReplacement = new PendingAutoReplacement(
                    word,
                    converted,
                    c,
                    data.vkCode,
                    target,
                    settings.OneKeySwitchRuEnOnly,
                    AutoReplaceKind.LayoutConversion);
                return;
            }

            if (!settings.AutoCorrectEnabled ||
                !_spellCorrectionService.TryAutoCorrectWord(word, settings.ProtectedWords, out var corrected, out var correctedLanguage))
            {
                return;
            }

            _pendingAutoReplacement = new PendingAutoReplacement(
                word,
                corrected,
                c,
                data.vkCode,
                correctedLanguage,
                false,
                AutoReplaceKind.SpellCorrection);
            return;
        }

        // Ignore text when Ctrl/Alt/Win are held; keep Shift for uppercase.
        if (IsModifierPressedExceptShift())
        {
            _wordTracker.Clear();
            return;
        }

        _wordTracker.Add(c);
    }

    private void HandleAutoReplacementOnKeyUp(uint vkCode)
    {
        var pending = _pendingAutoReplacement;
        if (pending is null || pending.DelimiterVkCode != vkCode)
        {
            return;
        }

        _pendingAutoReplacement = null;
        AutoReplaceRequested?.Invoke(new AutoReplaceRequest(
            pending.SourceWord,
            pending.ConvertedWord,
            pending.Delimiter,
            pending.Target,
            pending.SwitchRuEnOnly,
            pending.Kind));
    }

    private void CancelOneKeyCandidate()
    {
        lock (_sync)
        {
            if (_oneKeyCandidateActive)
            {
                _oneKeyCandidateCanceled = true;
            }
        }
    }

    private bool IsDebounced(HookAction action, int windowMs)
    {
        lock (_sync)
        {
            var now = DateTime.UtcNow;
            if (_lastActionFireUtc.TryGetValue(action, out var last) &&
                (now - last).TotalMilliseconds < windowMs)
            {
                return true;
            }

            _lastActionFireUtc[action] = now;
            return false;
        }
    }

    private bool MatchesHotkey(Hotkey hotkey, Keys actualKey)
    {
        if (hotkey.Key == Keys.None) return false;
        if (!HotkeyPrimaryMatches(hotkey.Key, actualKey)) return false;
        return ModifiersMatchForHotkey(hotkey, actualKey);
    }

    private bool ModifiersMatchForHotkey(Hotkey hotkey, Keys primaryKey)
    {
        bool ctrl, alt, shift;
        lock (_sync)
        {
            ctrl = IsAnyDown(NativeMethods.VK_CONTROL, NativeMethods.VK_LCONTROL, NativeMethods.VK_RCONTROL);
            alt = IsAnyDown(NativeMethods.VK_MENU, NativeMethods.VK_LMENU, NativeMethods.VK_RMENU);
            shift = IsAnyDown(NativeMethods.VK_SHIFT, NativeMethods.VK_LSHIFT, NativeMethods.VK_RSHIFT);
        }

        var primaryIsCtrl = primaryKey is Keys.ControlKey or Keys.LControlKey or Keys.RControlKey;
        var primaryIsAlt = primaryKey is Keys.Menu or Keys.LMenu or Keys.RMenu;
        var primaryIsShift = primaryKey is Keys.ShiftKey or Keys.LShiftKey or Keys.RShiftKey;

        if (!primaryIsCtrl && hotkey.Ctrl != ctrl) return false;
        if (!primaryIsAlt && hotkey.Alt != alt) return false;
        if (!primaryIsShift && hotkey.Shift != shift) return false;
        if (hotkey.Win) return false; // Win-mod hotkeys are not supported in hook path yet.
        return true;
    }

    private bool IsAnyDown(params int[] vks)
    {
        foreach (var vk in vks)
        {
            if (_keysDown.Contains((uint)vk))
            {
                return true;
            }
        }
        return false;
    }

    private static Keys NormalizeKey(Keys key) => key switch
    {
        Keys.CapsLock => Keys.Capital,
        _ => key
    };

    private static bool HotkeyPrimaryMatches(Keys configured, Keys actual)
    {
        if (configured == actual) return true;
        return (configured, actual) switch
        {
            (Keys.ControlKey, Keys.LControlKey or Keys.RControlKey) => true,
            (Keys.ShiftKey, Keys.LShiftKey or Keys.RShiftKey) => true,
            (Keys.Menu, Keys.LMenu or Keys.RMenu) => true,
            (Keys.Capital, Keys.CapsLock) => true,
            _ => false
        };
    }

    private static bool IsModifierVk(uint vk) =>
        vk is NativeMethods.VK_CONTROL or NativeMethods.VK_LCONTROL or NativeMethods.VK_RCONTROL
            or NativeMethods.VK_MENU or NativeMethods.VK_LMENU or NativeMethods.VK_RMENU
            or NativeMethods.VK_SHIFT or NativeMethods.VK_LSHIFT or NativeMethods.VK_RSHIFT;

    private static bool IsAutoConvertTriggerDelimiter(char c) => c == ' ';
    private static bool IsSuspendResetDelimiter(char c) => c is ' ' or '\r' or '\n' or '\t';

    private static bool IsModifierPressedExceptShift()
    {
        var ctrl = (NativeMethods.GetAsyncKeyState(NativeMethods.VK_CONTROL) & 0x8000) != 0;
        var alt = (NativeMethods.GetAsyncKeyState(NativeMethods.VK_MENU) & 0x8000) != 0;
        return ctrl || alt;
    }

    private static void FireAfterModifiersRelease(Action? action)
    {
        if (action is null) return;

        ThreadPool.QueueUserWorkItem(_ =>
        {
            var start = Environment.TickCount;
            while (Environment.TickCount - start < 140)
            {
                var ctrl = (NativeMethods.GetAsyncKeyState(NativeMethods.VK_CONTROL) & 0x8000) != 0;
                var alt = (NativeMethods.GetAsyncKeyState(NativeMethods.VK_MENU) & 0x8000) != 0;
                var shift = (NativeMethods.GetAsyncKeyState(NativeMethods.VK_SHIFT) & 0x8000) != 0;
                if (!ctrl && !alt && !shift)
                {
                    break;
                }
                Thread.Sleep(5);
            }

            action();
        });
    }

    private static bool IsTextMutatingShortcutStart(uint vkCode)
    {
        var ctrl = (NativeMethods.GetAsyncKeyState(NativeMethods.VK_CONTROL) & 0x8000) != 0;
        var alt = (NativeMethods.GetAsyncKeyState(NativeMethods.VK_MENU) & 0x8000) != 0;
        if (!ctrl || alt)
        {
            return false;
        }

        return vkCode is 0x41 or 0x43 or 0x56 or 0x58 or 0x59 or 0x5A; // A,C,V,X,Y,Z
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;

        if (_hookHandle != IntPtr.Zero)
        {
            NativeMethods.UnhookWindowsHookEx(_hookHandle);
            _hookHandle = IntPtr.Zero;
        }

        GC.SuppressFinalize(this);
    }

    private enum HookAction
    {
        ToggleAuto,
        ConvertSelection,
        InvertSelection
    }

    private sealed record PendingAutoReplacement(
        string SourceWord,
        string ConvertedWord,
        char Delimiter,
        uint DelimiterVkCode,
        LanguageScript Target,
        bool SwitchRuEnOnly,
        AutoReplaceKind Kind);

    public sealed record AutoReplaceRequest(
        string SourceWord,
        string ConvertedWord,
        char Delimiter,
        LanguageScript Target,
        bool SwitchRuEnOnly,
        AutoReplaceKind Kind);

    public enum AutoReplaceKind
    {
        LayoutConversion = 0,
        SpellCorrection = 1
    }
}
