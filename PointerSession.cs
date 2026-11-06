using System;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Threading;

namespace Pointy
{
    /// <summary>
    /// Runs the pointer: owns the overlay window, the low-level mouse hook that turns left
    /// clicks into taps, and the frame loop that keeps the hand glued to the cursor.
    /// </summary>
    public sealed class PointerSession : IDisposable
    {
        private const double GripFollow = 0.08;
        private const double BusyIntervalMs = 8;
        private const double IdleIntervalMs = 40;
        private const double IdleAfterSeconds = 0.5;
        private const int EscapeHotkeyId = 99;

        private readonly OverlayWindow _window = new();
        private readonly CursorHider _hider = new();
        private readonly TapSound _sound = new();
        private readonly DispatcherTimer _timer;

        private Native.LowLevelMouseProc? _hookProc;
        private IntPtr _hook;
        private Native.WinEventDelegate? _menuProc;
        private IntPtr _menuHook;
        private int _menuOpen;

        private Native.RECT _taskbar;
        private DateTime _taskbarChecked = DateTime.MinValue;

        private bool _active;
        private bool _down;
        private bool _realCursor;
        private bool _gripInit;
        private bool _idle;
        private IntPtr _monitor;

        private double _lastX = double.NaN;
        private double _lastY = double.NaN;
        private double _lastPress = -1;
        private DateTime _lastChange = DateTime.UtcNow;

        public PointerSession()
        {
            _timer = new DispatcherTimer(DispatcherPriority.Normal)
            {
                Interval = TimeSpan.FromMilliseconds(BusyIntervalMs),
            };
            _timer.Tick += (_, _) => Tick();

            _window.MonitorsChanged += () =>
            {
                _taskbarChecked = DateTime.MinValue;
                if (_active) UpdateMonitor(force: true);
            };

            Prefs.Changed += OnPrefsChanged;
            OnPrefsChanged();
        }

        public bool IsActive => _active;

        public PointerStyle Style => _window.Surface.State.Style;

        public bool SoundOn
        {
            get => Prefs.Current.SoundFor(Style.IsLaser);
            set
            {
                Prefs.Current.SetSoundFor(Style.IsLaser, value);
                TrayIcon.RefreshMenu?.Invoke();
            }
        }

        private void OnPrefsChanged()
        {
            var style = Pointers.Get(Prefs.Current.Style);
            if (!_window.Surface.State.Style.Id.Equals(style.Id, StringComparison.OrdinalIgnoreCase))
            {
                _window.Surface.State.Style = style;
                _window.Surface.InvalidateMaterial();
            }
            Snapshot();
        }

        private void Snapshot()
        {
            _window.Surface.InvalidateVisual();
        }

        public void Toggle()
        {
            if (_active) Deactivate();
            else Activate();
        }

        public void Activate()
        {
            if (_active) return;
            _active = true;
            Log.Write("activate");

            _window.Show();
            UpdateMonitor(force: true);

            InstallHook();
            InstallMenuHook();

            // Esc only grabs the keyboard while the pointer is actually running.
            App.Hotkeys?.Register(EscapeHotkeyId, HotkeySpec.Escape, Deactivate);

            UpdateTaskbar();
            _realCursor = false;
            _hider.Enabled = true;
            _gripInit = false;
            _down = false;
            _idle = false;
            _lastChange = DateTime.UtcNow;
            _timer.Interval = TimeSpan.FromMilliseconds(BusyIntervalMs);
            _timer.Start();

            Snapshot();
            TrayIcon.RefreshMenu?.Invoke();
        }

        public void Deactivate()
        {
            if (!_active) return;
            _active = false;

            Log.Write("deactivate");
            _timer.Stop();
            UninstallHook();
            UninstallMenuHook();
            App.Hotkeys?.Unregister(EscapeHotkeyId);

            _down = false;
            _window.Surface.State.Held = false;
            _window.Surface.State.Press = 0;
            _window.Surface.State.HideHand = false;
            _window.Surface.Ink.Clear();
            _window.Surface.InvalidateVisual();

            _window.Hide();
            _hider.Hide();
            _realCursor = false;

            TrayIcon.RefreshMenu?.Invoke();
        }

        public void ClearInk()
        {
            _window.Surface.Ink.Clear();
            Snapshot();
        }

        // ---------------------------------------------------------------- input

        private void InstallHook()
        {
            if (_hook != IntPtr.Zero) return;
            _hookProc = HookCallback;
            _hook = Native.SetWindowsHookEx(Native.WH_MOUSE_LL, _hookProc,
                Native.GetModuleHandle(null), 0);
        }

        private void UninstallHook()
        {
            if (_hook == IntPtr.Zero) return;
            Native.UnhookWindowsHookEx(_hook);
            _hook = IntPtr.Zero;
            _hookProc = null;
        }

        private IntPtr HookCallback(int nCode, IntPtr wParam, IntPtr lParam)
        {
            if (nCode < 0 || !_active)
            {
                return Native.CallNextHookEx(_hook, nCode, wParam, lParam);
            }

            int msg = wParam.ToInt32();
            var data = Marshal.PtrToStructure<Native.MSLLHOOKSTRUCT>(lParam);
            var screenPoint = data.pt;

            if (msg == Native.WM_LBUTTONDOWN)
            {
                if (ShouldTap(screenPoint))
                {
                    BeginTap(ToLocal(screenPoint));
                    return new IntPtr(1);
                }
            }
            else if (msg == Native.WM_LBUTTONUP)
            {
                if (_down)
                {
                    EndTap(ToLocal(screenPoint));
                    return new IntPtr(1);
                }
            }
            else if (msg == Native.WM_MOUSEMOVE && _down)
            {
                _window.Surface.Ink.Extend(ToLocal(screenPoint));
                Snapshot();
            }

            return Native.CallNextHookEx(_hook, nCode, wParam, lParam);
        }

        private bool ShouldTap(Native.POINT screenPoint)
        {
            if (_realCursor) return false;
            if (Pointers.InteractHeld(Prefs.Current.InteractKey)) return false;
            if (_menuOpen > 0) return false;
            if (InTaskbar(screenPoint)) return false;
            return true;
        }

        private void BeginTap(Point local)
        {
            var style = _window.Surface.State.Style;

            _down = true;
            _window.Surface.State.Held = true;
            _window.Surface.State.Press = 1;
            _window.Surface.State.Cursor = local;

            _sound.Enabled = Prefs.Current.SoundFor(style.IsLaser);
            _sound.Play(Prefs.Current.TapSound);

            double h = _window.Surface.ActualHeight;
            double width = Math.Max(3, h * 0.0055);
            _window.Surface.Ink.Begin(local, width, Pointers.ResolveInk(Prefs.Current.InkChoice, style));

            _lastChange = DateTime.UtcNow;
            Snapshot();
        }

        private void EndTap(Point local)
        {
            _down = false;
            _window.Surface.State.Held = false;
            _window.Surface.State.Cursor = local;
            _window.Surface.Ink.End();
            Snapshot();
        }

        private Point ToLocal(Native.POINT screenPoint)
        {
            var rect = _window.WindowRect;
            double scale = _window.DpiScale;
            if (scale <= 0) scale = 1;
            return new Point((screenPoint.X - rect.Left) / scale, (screenPoint.Y - rect.Top) / scale);
        }

        // ---------------------------------------------------------------- frame loop

        private void Tick()
        {
            Native.GetCursorPos(out var point);
            UpdateMonitor();
            UpdateTaskbar();

            var local = ToLocal(point);
            var state = _window.Surface.State;
            state.Cursor = local;

            double w = _window.Surface.ActualWidth;
            double h = _window.Surface.ActualHeight;
            if (w < 2 || h < 2) return;

            double target = Math.Clamp(local.X + GripLeanFrac(w), w * 0.15, w * 1.05);
            if (!_gripInit)
            {
                state.GripX = target;
                _gripInit = true;
            }
            else
            {
                state.GripX += (target - state.GripX) * GripFollow;
            }

            if (!_down)
            {
                state.Press += (0 - state.Press) * 0.18;
                if (state.Press < 0.002) state.Press = 0;
            }

            bool realCursor = !ShouldShowPointer(point);
            if (realCursor != _realCursor)
            {
                _realCursor = realCursor;
                _hider.Enabled = !realCursor;
                state.HideHand = realCursor;
                _lastChange = DateTime.UtcNow;
            }
            else if (_hider.Enabled)
            {
                // Applications set their own cursor on every mouse move, so the null cursor
                // has to be put back each frame or the arrow flickers through.
                _hider.Reassert();
            }

            bool inkChanged = _window.Surface.Ink.Tick(DateTime.UtcNow, Prefs.Current.FadeSeconds);

            bool moved = double.IsNaN(_lastX)
                || Math.Abs(local.X - _lastX) > 0.05
                || Math.Abs(local.Y - _lastY) > 0.05
                || Math.Abs(state.Press - _lastPress) > 0.002
                || Math.Abs(target - state.GripX) > 0.4
                || _down
                || _window.Surface.Ink.HasFading
                || inkChanged;

            if (moved)
            {
                _lastX = local.X;
                _lastY = local.Y;
                _lastPress = state.Press;
                _lastChange = DateTime.UtcNow;
                Snapshot();
            }

            UpdatePace();
        }

        /// <summary>True while the hand should be shown (system cursor hidden).</summary>
        private bool ShouldShowPointer(Native.POINT point)
        {
            if (Pointers.InteractHeld(Prefs.Current.InteractKey)) return false;
            if (_menuOpen > 0) return false;
            if (InTaskbar(point)) return false;
            return true;
        }

        private static double GripLeanFrac(double width) => 0.30 * width;

        private void UpdatePace()
        {
            bool still = (DateTime.UtcNow - _lastChange).TotalSeconds > IdleAfterSeconds
                         && !_down && !_window.Surface.Ink.HasFading;

            if (still && !_idle)
            {
                _idle = true;
                _timer.Interval = TimeSpan.FromMilliseconds(IdleIntervalMs);
            }
            else if (!still && _idle)
            {
                _idle = false;
                _timer.Interval = TimeSpan.FromMilliseconds(BusyIntervalMs);
            }
        }

        private void UpdateMonitor(bool force = false)
        {
            Native.GetCursorPos(out var point);
            var monitor = Native.MonitorFromPoint(point, Native.MONITOR_DEFAULTTONEAREST);
            if (!force && monitor == _monitor) return;

            _monitor = monitor;
            var info = new Native.MONITORINFO { cbSize = Marshal.SizeOf<Native.MONITORINFO>() };
            if (!Native.GetMonitorInfo(monitor, ref info)) return;

            _window.PlaceOn(info.rcMonitor);
            _hider.Show(info.rcMonitor);
            _gripInit = false;
            _lastX = double.NaN;
            Snapshot();
        }

        // ---------------------------------------------------------------- shell awareness

        private void UpdateTaskbar()
        {
            if ((DateTime.UtcNow - _taskbarChecked).TotalSeconds < 2) return;
            _taskbarChecked = DateTime.UtcNow;

            var data = new Native.APPBARDATA { cbSize = Marshal.SizeOf<Native.APPBARDATA>() };
            if (Native.SHAppBarMessage(Native.ABM_GETTASKBARPOS, ref data) != IntPtr.Zero)
            {
                _taskbar = data.rc;
            }
        }

        private bool InTaskbar(Native.POINT point)
        {
            var r = _taskbar;
            if (r.Right <= r.Left || r.Bottom <= r.Top) return false;
            return point.X >= r.Left && point.X < r.Right && point.Y >= r.Top && point.Y < r.Bottom;
        }

        private void InstallMenuHook()
        {
            if (_menuHook != IntPtr.Zero) return;
            _menuOpen = 0;
            _menuProc = OnShellEvent;
            _menuHook = Native.SetWinEventHook(
                Native.EVENT_SYSTEM_MENUPOPUPSTART, Native.EVENT_SYSTEM_MENUPOPUPEND,
                IntPtr.Zero, _menuProc, 0, 0,
                Native.WINEVENT_OUTOFCONTEXT | Native.WINEVENT_SKIPOWNPROCESS);
        }

        private void OnShellEvent(IntPtr hook, uint eventType, IntPtr hwnd, int idObject, int idChild,
            uint thread, uint time)
        {
            if (eventType == Native.EVENT_SYSTEM_MENUPOPUPSTART && _menuOpen < 64) _menuOpen++;
            else if (eventType == Native.EVENT_SYSTEM_MENUPOPUPEND && _menuOpen > 0) _menuOpen--;
        }

        private void UninstallMenuHook()
        {
            if (_menuHook == IntPtr.Zero) return;
            Native.UnhookWinEvent(_menuHook);
            _menuHook = IntPtr.Zero;
            _menuProc = null;
            _menuOpen = 0;
        }

        public void Dispose()
        {
            Deactivate();
            Prefs.Changed -= OnPrefsChanged;
            _hider.Dispose();
            _sound.Dispose();
            _timer.Stop();
        }
    }
}
