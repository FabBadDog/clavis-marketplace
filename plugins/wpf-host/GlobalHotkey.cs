using System.Diagnostics.CodeAnalysis;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;

namespace FabioSoft.Nucleus.Plugins.WpfHost;

/// Registers system-scope key bindings as OS global hotkeys on the primary window's HWND, so they fire
/// even when Clavis is not focused (e.g. summon-to-foreground). On a hotkey message it routes the bound
/// command string back through RunCommand, the same execution path as every other binding. Only gestures
/// whose key maps to a virtual-key code are registered; others are skipped.
[ExcludeFromCodeCoverage] // win32 interop + window message pump
internal sealed class GlobalHotkey : IDisposable
{
    private const int WmHotkey = 0x0312;
    private const int FirstHotkeyId = 0x4B00;
    private const uint ModAlt = 0x1;
    private const uint ModControl = 0x2;
    private const uint ModShift = 0x4;
    private const uint ModWin = 0x8;
    private const uint ModNoRepeat = 0x4000;

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool RegisterHotKey(IntPtr hWnd, int id, uint fsModifiers, uint vk);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool UnregisterHotKey(IntPtr hWnd, int id);

    private readonly Action<string> _runCommand;
    private readonly Dictionary<int, string> _idToCommand = [];
    private HwndSource? _source;
    private IReadOnlyList<KeyBinding> _systemBindings = [];

    public GlobalHotkey(Window window, Action<string> runCommand)
    {
        _runCommand = runCommand;
        if (new WindowInteropHelper(window).Handle != IntPtr.Zero)
        {
            Attach(window);
        }
        else
        {
            window.SourceInitialized += (_, _) => Attach(window);
        }
    }

    public void SetSystemBindings(IReadOnlyList<KeyBinding> systemBindings)
    {
        _systemBindings = systemBindings;
        Apply();
    }

    private void Attach(Window window)
    {
        _source = HwndSource.FromHwnd(new WindowInteropHelper(window).Handle);
        _source?.AddHook(WndProc);
        Apply();
    }

    private void Apply()
    {
        if (_source is null)
        {
            return;
        }

        foreach (var id in _idToCommand.Keys)
        {
            UnregisterHotKey(_source.Handle, id);
        }

        _idToCommand.Clear();

        var nextId = FirstHotkeyId;
        foreach (var binding in _systemBindings)
        {
            if (TryParse(binding.Gesture, out var modifiers, out var virtualKey)
                && RegisterHotKey(_source.Handle, nextId, modifiers | ModNoRepeat, virtualKey))
            {
                _idToCommand[nextId] = binding.Command;
                nextId++;
            }
        }
    }

    private IntPtr WndProc(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam, ref bool handled)
    {
        if (msg == WmHotkey && _idToCommand.TryGetValue(wParam.ToInt32(), out var command))
        {
            _runCommand(command);
            handled = true;
        }

        return IntPtr.Zero;
    }

    private static bool TryParse(string gesture, out uint modifiers, out uint virtualKey)
    {
        modifiers = 0;
        virtualKey = 0;

        string? key = null;
        foreach (var token in gesture.Split('+', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            switch (token)
            {
                case "Ctrl": modifiers |= ModControl; break;
                case "Alt": modifiers |= ModAlt; break;
                case "Shift": modifiers |= ModShift; break;
                case "Win": modifiers |= ModWin; break;
                default: key = token; break;
            }
        }

        return key is not null && TryVirtualKey(key, out virtualKey);
    }

    private static bool TryVirtualKey(string key, out uint virtualKey)
    {
        virtualKey = 0;

        if (key.Length == 1)
        {
            var character = char.ToUpperInvariant(key[0]);
            if (character is >= 'A' and <= 'Z' or >= '0' and <= '9')
            {
                virtualKey = character;
                return true;
            }
        }

        if (key.Length is 2 or 3 && key[0] == 'F' && int.TryParse(key.AsSpan(1), out var functionNumber)
            && functionNumber is >= 1 and <= 24)
        {
            virtualKey = (uint)(0x70 + functionNumber - 1); // VK_F1 = 0x70
            return true;
        }

        switch (key)
        {
            case "Space": virtualKey = 0x20; return true;
            case "Enter": virtualKey = 0x0D; return true;
            case "Tab": virtualKey = 0x09; return true;
            case "Escape": virtualKey = 0x1B; return true;
            default: return false;
        }
    }

    public void Dispose()
    {
        if (_source is null)
        {
            return;
        }

        foreach (var id in _idToCommand.Keys)
        {
            UnregisterHotKey(_source.Handle, id);
        }

        _idToCommand.Clear();
        _source.RemoveHook(WndProc);
    }
}
