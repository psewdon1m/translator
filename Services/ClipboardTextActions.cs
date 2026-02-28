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
        DebugLog.Write("TextAction: ConvertSelectedTextLayout requested");
        return TransformSelection(text =>
        {
            var converted = _transform.ConvertLayout(text, out var changed);
            return changed ? converted : text;
        });
    }

    public bool InvertSelectedTextCase()
    {
        DebugLog.Write("TextAction: InvertSelectedTextCase requested");
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
                DebugLog.Write($"AutoReplace: begin sourceLen={sourceWordLength} replLen={replacementWithDelimiter.Length}");
                _input.ReleaseModifiers();
                Thread.Sleep(8);
                _input.SendBackspaces(sourceWordLength + 1);
                Thread.Sleep(12);

                if (!InsertTextViaClipboard(replacementWithDelimiter))
                {
                    DebugLog.Write("AutoReplace: clipboard insert failed");
                    return false;
                }

                DebugLog.Write("AutoReplace: success");
                return true;
            }
            catch (Exception ex)
            {
                DebugLog.Write($"AutoReplace: exception {ex.GetType().Name}: {ex.Message}");
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
                    DebugLog.Write("TextAction: copy selection failed (timeout)");
                    return false;
                }

                if (!Clipboard.ContainsText())
                {
                    DebugLog.Write("TextAction: clipboard has no text");
                    return false;
                }

                var text = Clipboard.GetText();
                if (string.IsNullOrEmpty(text))
                {
                    DebugLog.Write("TextAction: clipboard text empty");
                    return false;
                }

                var transformed = transform(text);
                if (transformed == text)
                {
                    DebugLog.Write("TextAction: transform made no changes");
                    return false;
                }

                _input.ReleaseModifiers();
                Thread.Sleep(8);
                DebugLog.Write($"TextAction: direct replace len={transformed.Length}");
                if (!ReplaceSelectionDirect(transformed))
                {
                    DebugLog.Write("TextAction: replace selection failed");
                    return false;
                }

                DebugLog.Write("TextAction: success");
                return true;
            }
            catch (Exception ex)
            {
                DebugLog.Write($"TextAction: exception {ex.GetType().Name}: {ex.Message}");
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
                DebugLog.Write($"TextAction: copy success on attempt {attempt + 1}");
                return true;
            }

            _input.SendCtrlInsert();
            if (WaitForClipboardUpdate(seqBefore, 120))
            {
                DebugLog.Write($"TextAction: copy success via Ctrl+Insert on attempt {attempt + 1}");
                return true;
            }

            try
            {
                SendKeys.SendWait("^c");
                if (WaitForClipboardUpdate(seqBefore, 120))
                {
                    DebugLog.Write($"TextAction: copy success via SendKeys Ctrl+C on attempt {attempt + 1}");
                    return true;
                }
            }
            catch (Exception ex)
            {
                DebugLog.Write($"TextAction: SendKeys copy fallback exception {ex.GetType().Name}: {ex.Message}");
            }

            DebugLog.Write($"TextAction: copy timeout on attempt {attempt + 1}");
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
                DebugLog.Write("Insert: clipboard sequence did not change");
            }

            _input.ReleaseModifiers();
            Thread.Sleep(8);
            _input.SendCtrlCombo(Keys.V);
            Thread.Sleep(24);
            DebugLog.Write($"Insert: paste sent len={text.Length}");
            return true;
        }
        catch (Exception ex)
        {
            DebugLog.Write($"Insert: exception {ex.GetType().Name}: {ex.Message}");
            return false;
        }
    }

}
