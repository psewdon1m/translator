using System.Runtime.InteropServices;
using System.Windows.Forms;
using TranslatorTray.Native;

namespace TranslatorTray.Services;

public sealed class InputSimulator
{
    public const long SyntheticExtraInfoTag = 0x0000000087654321L;
    public static bool IsOurSyntheticTag(IntPtr extraInfo) => extraInfo.ToInt64() == SyntheticExtraInfoTag;

    public void SendBackspaces(int count)
    {
        if (count <= 0) return;

        for (var i = 0; i < count; i++)
        {
            KeyTap((byte)NativeMethods.VK_BACK);
            Thread.Sleep(2);
        }
    }

    public void SendText(string text)
    {
        if (string.IsNullOrEmpty(text)) return;

        var inputs = new List<NativeMethods.INPUT>(text.Length * 2);
        foreach (var c in text)
        {
            inputs.Add(new NativeMethods.INPUT
            {
                type = NativeMethods.INPUT_KEYBOARD,
                U = new NativeMethods.InputUnion
                {
                    ki = new NativeMethods.KEYBDINPUT
                    {
                        wVk = 0,
                        wScan = c,
                        dwFlags = NativeMethods.KEYEVENTF_UNICODE,
                        dwExtraInfo = new IntPtr(SyntheticExtraInfoTag)
                    }
                }
            });
            inputs.Add(new NativeMethods.INPUT
            {
                type = NativeMethods.INPUT_KEYBOARD,
                U = new NativeMethods.InputUnion
                {
                    ki = new NativeMethods.KEYBDINPUT
                    {
                        wVk = 0,
                        wScan = c,
                        dwFlags = NativeMethods.KEYEVENTF_UNICODE | NativeMethods.KEYEVENTF_KEYUP,
                        dwExtraInfo = new IntPtr(SyntheticExtraInfoTag)
                    }
                }
            });
        }

        Send(inputs);
    }

    public void SendCtrlCombo(Keys key)
    {
        SendChordLegacy((byte)NativeMethods.VK_LCONTROL, (byte)key);
    }

    public void SendCtrlInsert()
    {
        SendChordLegacy((byte)NativeMethods.VK_LCONTROL, 0x2D); // VK_INSERT
    }

    public void SendShiftInsert()
    {
        SendChordLegacy((byte)NativeMethods.VK_LSHIFT, 0x2D); // VK_INSERT
    }

    public void SendDeleteSelection()
    {
        KeyTap(0x2E); // VK_DELETE
    }

    public void ReleaseModifiers()
    {
        KeyUpLegacy((byte)NativeMethods.VK_LCONTROL);
        KeyUpLegacy((byte)NativeMethods.VK_RCONTROL);
        KeyUpLegacy((byte)NativeMethods.VK_CONTROL);
        KeyUpLegacy((byte)NativeMethods.VK_LSHIFT);
        KeyUpLegacy((byte)NativeMethods.VK_RSHIFT);
        KeyUpLegacy((byte)NativeMethods.VK_SHIFT);
        KeyUpLegacy((byte)NativeMethods.VK_LMENU);
        KeyUpLegacy((byte)NativeMethods.VK_RMENU);
        KeyUpLegacy((byte)NativeMethods.VK_MENU);
    }

    private static NativeMethods.INPUT KeyDown(ushort vk) =>
        new()
        {
            type = NativeMethods.INPUT_KEYBOARD,
            U = new NativeMethods.InputUnion
            {
                ki = new NativeMethods.KEYBDINPUT
                {
                    wVk = vk,
                    wScan = (ushort)NativeMethods.MapVirtualKey(vk, NativeMethods.MAPVK_VK_TO_VSC),
                    dwExtraInfo = new IntPtr(SyntheticExtraInfoTag)
                }
            }
        };

    private static NativeMethods.INPUT KeyUp(ushort vk) =>
        new()
        {
            type = NativeMethods.INPUT_KEYBOARD,
            U = new NativeMethods.InputUnion
            {
                ki = new NativeMethods.KEYBDINPUT
                {
                    wVk = vk,
                    wScan = (ushort)NativeMethods.MapVirtualKey(vk, NativeMethods.MAPVK_VK_TO_VSC),
                    dwFlags = NativeMethods.KEYEVENTF_KEYUP,
                    dwExtraInfo = new IntPtr(SyntheticExtraInfoTag)
                }
            }
        };

    private static void Send(List<NativeMethods.INPUT> inputs)
    {
        if (inputs.Count == 0) return;
        NativeMethods.SendInput((uint)inputs.Count, inputs.ToArray(), Marshal.SizeOf<NativeMethods.INPUT>());
    }

    private static void SendOne(NativeMethods.INPUT input)
    {
        NativeMethods.SendInput(1, [input], Marshal.SizeOf<NativeMethods.INPUT>());
    }

    private static void SendChordLegacy(byte modifierVk, byte keyVk)
    {
        KeyDownLegacy(modifierVk);
        Thread.Sleep(4);
        KeyDownLegacy(keyVk);
        Thread.Sleep(4);
        KeyUpLegacy(keyVk);
        Thread.Sleep(4);
        KeyUpLegacy(modifierVk);
    }

    private static void KeyTap(byte vk)
    {
        KeyDownLegacy(vk);
        Thread.Sleep(2);
        KeyUpLegacy(vk);
    }

    private static void KeyDownLegacy(byte vk)
    {
        NativeMethods.keybd_event(vk, (byte)NativeMethods.MapVirtualKey(vk, NativeMethods.MAPVK_VK_TO_VSC), 0, (UIntPtr)SyntheticExtraInfoTag);
    }

    private static void KeyUpLegacy(byte vk)
    {
        NativeMethods.keybd_event(vk, (byte)NativeMethods.MapVirtualKey(vk, NativeMethods.MAPVK_VK_TO_VSC), NativeMethods.KEYEVENTF_KEYUP, (UIntPtr)SyntheticExtraInfoTag);
    }
}
