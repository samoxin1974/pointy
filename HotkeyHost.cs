using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows.Input;
using System.Windows.Interop;

namespace Pointy
{
    /// <summary>A keyboard shortcut expressed as Win32 modifiers plus a virtual key.</summary>
    public sealed class HotkeySpec
    {
        public uint Modifiers { get; init; }
        public uint Key { get; init; }
        public string Display { get; init; } = "";

        public static readonly HotkeySpec DefaultToggle = new()
        {
            Modifiers = Native.MOD_CONTROL | Native.MOD_ALT,
            Key = 0x48, // H
            Display = "Ctrl+Alt+H",
        };

        public static readonly HotkeySpec DefaultClear = new()
        {
            Modifiers = Native.MOD_CONTROL | Native.MOD_ALT,
            Key = 0x58, // X
            Display = "Ctrl+Alt+X",
        };

        public static readonly HotkeySpec Escape = new() { Modifiers = 0, Key = 0x1B, Display = "Esc" };

        public static HotkeySpec Parse(string? text, HotkeySpec fallback)
        {
            if (string.IsNullOrWhiteSpace(text)) return fallback;

            uint mods = 0;
            uint vk = 0;

            foreach (var raw in text.Split('+', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
            {
                switch (raw.ToLowerInvariant())
                {
                    case "ctrl":
                    case "control":
                        mods |= Native.MOD_CONTROL;
                        break;
                    case "alt":
                        mods |= Native.MOD_ALT;
                        break;
                    case "shift":
                        mods |= Native.MOD_SHIFT;
                        break;
                    case "win":
                        mods |= Native.MOD_WIN;
                        break;
                    default:
                        if (Enum.TryParse<Key>(raw, true, out var key))
                        {
                            vk = (uint)KeyInterop.VirtualKeyFromKey(key);
                        }
                        break;
                }
            }

            if (vk == 0 || mods == 0) return fallback;

            var display = new List<string>();
            if ((mods & Native.MOD_CONTROL) != 0) display.Add("Ctrl");
            if ((mods & Native.MOD_ALT) != 0) display.Add("Alt");
            if ((mods & Native.MOD_SHIFT) != 0) display.Add("Shift");
            if ((mods & Native.MOD_WIN) != 0) display.Add("Win");
            display.Add(KeyInterop.KeyFromVirtualKey((int)vk).ToString());

            return new HotkeySpec { Modifiers = mods, Key = vk, Display = string.Join("+", display) };
        }
    }

    /// <summary>Message-only window that owns the global hotkey registrations.</summary>
    public sealed class HotkeyHost : IDisposable
    {
        private readonly HwndSource _source;
        private readonly Dictionary<int, Action> _actions = new();

        public HotkeyHost()
        {
            var parameters = new HwndSourceParameters("PointyHotkeys")
            {
                Width = 0,
                Height = 0,
                PositionX = 0,
                PositionY = 0,
                WindowStyle = 0,
                ExtendedWindowStyle = Native.WS_EX_TOOLWINDOW,
            };
            _source = new HwndSource(parameters);
            _source.AddHook(WndProc);
        }

        private IntPtr WndProc(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam, ref bool handled)
        {
            const int WM_HOTKEY = 0x0312;
            if (msg == WM_HOTKEY)
            {
                int id = wParam.ToInt32();
                if (_actions.TryGetValue(id, out var action))
                {
                    action();
                    handled = true;
                }
            }
            return IntPtr.Zero;
        }

        public bool Register(int id, HotkeySpec spec, Action action)
        {
            Unregister(id);
            if (!Native.RegisterHotKey(_source.Handle, id, spec.Modifiers, spec.Key))
            {
                return false;
            }
            _actions[id] = action;
            return true;
        }

        public void Unregister(int id)
        {
            if (_actions.Remove(id))
            {
                Native.UnregisterHotKey(_source.Handle, id);
            }
        }

        public void UnregisterAll()
        {
            foreach (var id in _actions.Keys.ToList())
            {
                Unregister(id);
            }
        }

        public void Dispose()
        {
            UnregisterAll();
            _source.Dispose();
        }
    }
}
