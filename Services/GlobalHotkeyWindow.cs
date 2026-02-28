using System.Windows.Forms;
using TranslatorTray.Models;
using TranslatorTray.Native;

namespace TranslatorTray.Services;

public sealed class GlobalHotkeyWindow : NativeWindow, IDisposable
{
    private readonly Dictionary<int, Action> _handlers = new();
    private int _nextId = 1;
    private bool _disposed;

    public GlobalHotkeyWindow()
    {
        CreateHandle(new CreateParams());
    }

    public bool Register(Hotkey hotkey, Action handler, out int id)
    {
        id = 0;
        if (hotkey.Key == Keys.None)
        {
            return false;
        }

        var mods = 0u;
        if (hotkey.Ctrl) mods |= NativeMethods.MOD_CONTROL;
        if (hotkey.Alt) mods |= NativeMethods.MOD_ALT;
        if (hotkey.Shift) mods |= NativeMethods.MOD_SHIFT;
        if (hotkey.Win) mods |= NativeMethods.MOD_WIN;

        id = _nextId++;
        var ok = NativeMethods.RegisterHotKey(Handle, id, mods, (uint)hotkey.Key);
        if (!ok)
        {
            return false;
        }

        _handlers[id] = handler;
        return true;
    }

    public void UnregisterAll()
    {
        foreach (var id in _handlers.Keys.ToArray())
        {
            NativeMethods.UnregisterHotKey(Handle, id);
        }

        _handlers.Clear();
    }

    protected override void WndProc(ref Message m)
    {
        if (m.Msg == NativeMethods.WM_HOTKEY)
        {
            var id = m.WParam.ToInt32();
            if (_handlers.TryGetValue(id, out var handler))
            {
                handler();
            }
        }

        base.WndProc(ref m);
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        UnregisterAll();
        DestroyHandle();
        GC.SuppressFinalize(this);
    }
}
