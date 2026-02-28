using System.Globalization;
using System.Runtime.InteropServices;
using TranslatorTray.Native;

namespace TranslatorTray.Services;

public sealed class KeyboardLayoutService
{
    private const string RuKlid = "00000419";
    private const string EnKlid = "00000409";

    public IReadOnlyList<InstalledLayoutInfo> GetInstalledLayouts()
    {
        var count = (int)NativeMethods.GetKeyboardLayoutList(0, null);
        if (count <= 0)
        {
            return Array.Empty<InstalledLayoutInfo>();
        }

        var list = new IntPtr[count];
        NativeMethods.GetKeyboardLayoutList(count, list);
        return list
            .Select(hkl => new InstalledLayoutInfo(hkl, GetLanguageId(hkl)))
            .ToList();
    }

    public bool TryGetRuEnLayouts(out IntPtr ruHkl, out IntPtr enHkl)
    {
        ruHkl = IntPtr.Zero;
        enHkl = IntPtr.Zero;
        foreach (var layout in GetInstalledLayouts())
        {
            if (layout.CultureName.StartsWith("ru", StringComparison.OrdinalIgnoreCase))
            {
                ruHkl = layout.Handle;
            }
            else if (layout.CultureName.StartsWith("en", StringComparison.OrdinalIgnoreCase))
            {
                enHkl = layout.Handle;
            }
        }

        if (ruHkl == IntPtr.Zero)
        {
            ruHkl = NativeMethods.LoadKeyboardLayout(RuKlid, 1);
        }

        if (enHkl == IntPtr.Zero)
        {
            enHkl = NativeMethods.LoadKeyboardLayout(EnKlid, 1);
        }

        return ruHkl != IntPtr.Zero && enHkl != IntPtr.Zero;
    }

    public IntPtr GetForegroundLayout()
    {
        var hwnd = NativeMethods.GetForegroundWindow();
        if (hwnd == IntPtr.Zero)
        {
            return IntPtr.Zero;
        }

        var threadId = NativeMethods.GetWindowThreadProcessId(hwnd, out _);
        return NativeMethods.GetKeyboardLayout(threadId);
    }

    public LanguageScript GetForegroundScript()
    {
        var lang = GetLanguageId(GetForegroundLayout());
        return lang.StartsWith("ru", StringComparison.OrdinalIgnoreCase)
            ? LanguageScript.Russian
            : lang.StartsWith("en", StringComparison.OrdinalIgnoreCase)
                ? LanguageScript.English
                : LanguageScript.Unknown;
    }

    public bool SwitchRuEn()
    {
        if (!TryGetRuEnLayouts(out var ruHkl, out var enHkl))
        {
            return false;
        }

        var current = GetForegroundScript();
        return SwitchTo(current == LanguageScript.Russian ? enHkl : ruHkl);
    }

    public bool SwitchTo(LanguageScript target)
    {
        if (!TryGetRuEnLayouts(out var ruHkl, out var enHkl))
        {
            return false;
        }

        return target switch
        {
            LanguageScript.Russian => SwitchTo(ruHkl),
            LanguageScript.English => SwitchTo(enHkl),
            _ => false
        };
    }

    public bool SwitchTo(IntPtr hkl)
    {
        if (hkl == IntPtr.Zero)
        {
            return false;
        }

        var targetHwnd = ResolveFocusedTargetWindow();
        if (targetHwnd == IntPtr.Zero)
        {
            targetHwnd = NativeMethods.GetForegroundWindow();
        }

        if (targetHwnd != IntPtr.Zero && NativeMethods.IsWindow(targetHwnd))
        {
            NativeMethods.PostMessage(targetHwnd, NativeMethods.WM_INPUTLANGCHANGEREQUEST, IntPtr.Zero, hkl);
        }
        else
        {
            NativeMethods.ActivateKeyboardLayout(hkl, 0);
        }

        return true;
    }

    public char? TranslateKeyToChar(uint vkCode, uint scanCode)
    {
        var layout = GetForegroundLayout();
        if (layout == IntPtr.Zero)
        {
            return null;
        }

        var keyState = new byte[256];
        if (IsPressed(NativeMethods.VK_SHIFT))
        {
            keyState[NativeMethods.VK_SHIFT] = 0x80;
        }

        if (IsPressed(NativeMethods.VK_CONTROL))
        {
            keyState[NativeMethods.VK_CONTROL] = 0x80;
        }

        if (IsPressed(NativeMethods.VK_MENU))
        {
            keyState[NativeMethods.VK_MENU] = 0x80;
        }

        var sb = new System.Text.StringBuilder(4);
        var rc = NativeMethods.ToUnicodeEx(vkCode, scanCode, keyState, sb, sb.Capacity, 0, layout);
        if (rc <= 0 || sb.Length == 0)
        {
            return null;
        }

        return sb[0];
    }

    private static bool IsPressed(int vk) => (NativeMethods.GetKeyState(vk) & 0x8000) != 0;

    private static IntPtr ResolveFocusedTargetWindow()
    {
        var hwnd = NativeMethods.GetForegroundWindow();
        if (hwnd == IntPtr.Zero)
        {
            return IntPtr.Zero;
        }

        var threadId = NativeMethods.GetWindowThreadProcessId(hwnd, out _);
        var gti = new NativeMethods.GUITHREADINFO { cbSize = Marshal.SizeOf<NativeMethods.GUITHREADINFO>() };
        if (NativeMethods.GetGUIThreadInfo(threadId, ref gti))
        {
            if (gti.hwndCaret != IntPtr.Zero)
            {
                return gti.hwndCaret;
            }

            if (gti.hwndFocus != IntPtr.Zero)
            {
                return gti.hwndFocus;
            }

            if (gti.hwndActive != IntPtr.Zero)
            {
                return gti.hwndActive;
            }
        }

        var currentThreadId = NativeMethods.GetCurrentThreadId();
        if (threadId != 0 && threadId != currentThreadId)
        {
            if (NativeMethods.AttachThreadInput(threadId, currentThreadId, true))
            {
                try
                {
                    var focus = NativeMethods.GetFocus();
                    if (focus != IntPtr.Zero)
                    {
                        return focus;
                    }
                }
                finally
                {
                    NativeMethods.AttachThreadInput(threadId, currentThreadId, false);
                }
            }
        }

        return hwnd;
    }

    private static string GetLanguageId(IntPtr hkl)
    {
        var langId = (ushort)((long)hkl & 0xFFFF);
        try
        {
            return new CultureInfo(langId).Name;
        }
        catch
        {
            return $"0x{langId:X4}";
        }
    }
}

public readonly record struct InstalledLayoutInfo(IntPtr Handle, string CultureName);
