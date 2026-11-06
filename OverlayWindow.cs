using System;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Media;

namespace Pointy
{
    /// <summary>
    /// Full-monitor, always-on-top window that paints the pointer.
    ///
    /// WS_EX_TRANSPARENT makes every mouse message fall through to the application
    /// underneath, so hover, right-click and scrolling keep working normally. The window is
    /// never activated, never in the taskbar and never takes focus. Left-button taps are
    /// swallowed separately by the low-level mouse hook.
    ///
    /// Note: a per-pixel-alpha window like this one is excluded from cursor routing, so
    /// hiding the system cursor is CursorHider's job, not this window's.
    /// </summary>
    public sealed class OverlayWindow : Window
    {
        private readonly OverlaySurface _surface;
        private HwndSource? _source;
        private IntPtr _handle;

        public OverlaySurface Surface => _surface;

        public OverlayWindow()
        {
            WindowStyle = WindowStyle.None;
            AllowsTransparency = true;
            Background = Brushes.Transparent;
            Topmost = true;
            ShowInTaskbar = false;
            ShowActivated = false;
            ResizeMode = ResizeMode.NoResize;
            Focusable = false;
            IsHitTestVisible = false;
            WindowStartupLocation = WindowStartupLocation.Manual;

            _surface = new OverlaySurface();
            Content = _surface;
            RenderOptions.SetBitmapScalingMode(_surface, BitmapScalingMode.HighQuality);
        }

        protected override void OnSourceInitialized(EventArgs e)
        {
            base.OnSourceInitialized(e);

            _handle = new WindowInteropHelper(this).Handle;
            int exStyle = Native.GetWindowLong(_handle, Native.GWL_EXSTYLE);
            Native.SetWindowLong(_handle, Native.GWL_EXSTYLE,
                exStyle | Native.WS_EX_TRANSPARENT | Native.WS_EX_LAYERED
                        | Native.WS_EX_TOOLWINDOW | Native.WS_EX_NOACTIVATE);

            _source = HwndSource.FromHwnd(_handle);
            _source?.AddHook(WndProc);
        }

        private IntPtr WndProc(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam, ref bool handled)
        {
            if (msg == Native.WM_DISPLAYCHANGE)
            {
                MonitorsChanged?.Invoke();
            }

            return IntPtr.Zero;
        }

        public event Action? MonitorsChanged;

        /// <summary>Cover a monitor. Positions are physical pixels; layout follows the monitor DPI.</summary>
        internal void PlaceOn(Native.RECT monitor)
        {
            if (_handle == IntPtr.Zero) return;

            Native.SetWindowPos(_handle, new IntPtr(Native.HWND_TOPMOST),
                monitor.Left, monitor.Top, monitor.Width, monitor.Height,
                Native.SWP_NOACTIVATE | Native.SWP_SHOWWINDOW);
        }

        public double DpiScale
        {
            get
            {
                if (_handle == IntPtr.Zero) return 1.0;
                uint dpi = Native.GetDpiForWindow(_handle);
                return dpi == 0 ? 1.0 : dpi / 96.0;
            }
        }

        internal Native.RECT WindowRect
        {
            get
            {
                Native.GetWindowRect(_handle, out var rect);
                return rect;
            }
        }
    }
}
