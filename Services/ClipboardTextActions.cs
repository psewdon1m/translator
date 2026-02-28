using System.Windows.Forms;
using TranslatorTray.Native;

namespace TranslatorTray.Services;

public sealed class ClipboardTextActions
{
    private readonly InputSimulator _input;
    private readonly TextTransformService _transform;
    private readonly object _opLock = new();

    public ClipboardTextActions(InputSimulator input, TextTransformService transform)
    {
        _input = input;
        _transform = transform;
    }

    public bool ConvertSelectedTextLayout()
    {
        return TransformSelection(text =>
        {
            var converted = _transform.ConvertLayout(text, out var changed);
            return changed ? converted : text;
        });
    }

    public bool InvertSelectedTextCase()
    {
        return TransformSelection(_transform.InvertCase);
    }

    public bool ReplaceLastWord(int sourceWordLength, string replacementWithDelimiter)
    {
        if (sourceWordLength <= 0 || string.IsNullOrEmpty(replacementWithDelimiter))
        {
            return false;
        }

        lock (_opLock)
        {
            try
            {
                _input.ReleaseModifiers();
                Thread.Sleep(8);
                _input.SendBackspaces(sourceWordLength + 1);
                Thread.Sleep(12);

                if (!InsertTextViaClipboard(replacementWithDelimiter))
                {
                    return false;
                }

                return true;
            }
            catch
            {
                return false;
            }
        }
    }

    private bool TransformSelection(Func<string, string> transform)
    {
        lock (_opLock)
        {
            try
            {
                // Hotkeys fire on key-up, but Ctrl may still be physically held for a short time.
                Thread.Sleep(20);
                WaitForModifiersRelease(220);
                _input.ReleaseModifiers();
                Thread.Sleep(8);
                if (!TryCopySelectionToClipboard())
                {
                    return false;
                }

                if (!Clipboard.ContainsText())
                {
                    return false;
                }

                var text = Clipboard.GetText();
                if (string.IsNullOrEmpty(text))
                {
                    return false;
                }

                var transformed = transform(text);
                if (transformed == text)
                {
                    return false;
                }

                _input.ReleaseModifiers();
                Thread.Sleep(8);
                if (!ReplaceSelectionDirect(transformed))
                {
                    return false;
                }

                return true;
            }
            catch
            {
                return false;
            }
        }
    }

    private bool TryCopySelectionToClipboard()
    {
        for (var attempt = 0; attempt < 1; attempt++)
        {
            try
            {
                Clipboard.Clear();
            }
            catch
            {
                // best-effort
            }

            var seqBefore = NativeMethods.GetClipboardSequenceNumber();

            _input.SendCtrlCombo(Keys.C);
            if (WaitForClipboardUpdate(seqBefore, 120))
            {
                return true;
            }

            _input.SendCtrlInsert();
            if (WaitForClipboardUpdate(seqBefore, 120))
            {
                return true;
            }

            try
            {
                SendKeys.SendWait("^c");
                if (WaitForClipboardUpdate(seqBefore, 120))
                {
                    return true;
                }
            }
            catch
            {
            }

        }

        return false;
    }

    private static void WaitForModifiersRelease(int timeoutMs)
    {
        var start = Environment.TickCount;
        while (Environment.TickCount - start < timeoutMs)
        {
            var ctrl = (NativeMethods.GetAsyncKeyState(NativeMethods.VK_CONTROL) & 0x8000) != 0;
            var alt = (NativeMethods.GetAsyncKeyState(NativeMethods.VK_MENU) & 0x8000) != 0;
            var shift = (NativeMethods.GetAsyncKeyState(NativeMethods.VK_SHIFT) & 0x8000) != 0;
            if (!ctrl && !alt && !shift)
            {
                return;
            }

            Thread.Sleep(1);
        }
    }

    private static bool WaitForClipboardUpdate(uint previousSequence, int timeoutMs)
    {
        var start = Environment.TickCount;
        while (Environment.TickCount - start < timeoutMs)
        {
            if (NativeMethods.GetClipboardSequenceNumber() != previousSequence)
            {
                return true;
            }
            Thread.Sleep(4);
        }

        return false;
    }

    private bool ReplaceSelectionDirect(string text)
    {
        _input.SendDeleteSelection();
        Thread.Sleep(6);
        return InsertTextViaClipboard(text);
    }

    private bool InsertTextViaClipboard(string text)
    {
        try
        {
            var seqBefore = NativeMethods.GetClipboardSequenceNumber();
            Clipboard.SetText(text, TextDataFormat.UnicodeText);
            if (!WaitForClipboardUpdate(seqBefore, 120))
            {
            }

            _input.ReleaseModifiers();
            Thread.Sleep(8);
            _input.SendCtrlCombo(Keys.V);
            Thread.Sleep(24);
            return true;
        }
        catch
        {
            return false;
        }
    }

}
