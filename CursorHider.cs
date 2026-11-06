using System;
using System.Windows.Interop;
using System.Windows.Media;

namespace Pointy
{
    /// <summary>
    /// Full-monitor window whose only job is to answer WM_SETCURSOR with NULL.
    ///
    /// Two hard constraints decide how this window has to be built:
    ///
    ///  * It must NOT be WS_EX_LAYERED. Windows skips layered windows when it routes the
    ///    cursor, so a layered window never receives WM_SETCURSOR and the real cursor
    ///    flickers back on every mouse move handled by the application underneath.
    ///  * It must not paint anything. A plain window gets a black client area, which covered
    ///    the whole desktop - the "screen goes black" bug.
    ///
    /// Both hold for a non-layered HwndSource whose client area DWM composites as fully
    /// transparent: an empty root visual, a transparent CompositionTarget background, and the
    /// frame extended into the client area. WS_EX_TRANSPARENT still lets every mouse message
    /// through, so the window is invisible to the user in every sense.
    /// </summary>
    public sealed class CursorHider : IDisposable
    {
        private const int WS_POPUP = unchecked((int)0x80000000);
        private const int WS_VISIBLE = 0x10000000;

        private static readonly IntPtr Arrow = Native.LoadCursor(IntPtr.Zero, (IntPtr)Native.IDC_ARROW);

        private HwndSource? _source;
        private bool _enabled = true;

        public bool Enabled
        {
            get => _enabled;
            set
            {
                if (_enabled == value) return;
                _enabled = value;
                Reassert();
            }
        }

        internal void Show(Native.RECT monitor)
        {
            EnsureWindow();
            if (_source is null) return;

            Native.SetWindowPos(_source.Handle, new IntPtr(Native.HWND_TOPMOST),
                monitor.Left, monitor.Top, monitor.Width, monitor.Height,
                Native.SWP_NOACTIVATE | Native.SWP_SHOWWINDOW);

            Reassert();
        }

        private void EnsureWindow()
        {
            if (_source is not null) return;

            var parameters = new HwndSourceParameters("PointyCursorHider")
            {
                WindowStyle = WS_POPUP | WS_VISIBLE,
                // Deliberately no WS_EX_LAYERED: see the class comment.
                ExtendedWindowStyle = Native.WS_EX_TRANSPARENT | Native.WS_EX_TOOLWINDOW
                                      | Native.WS_EX_NOACTIVATE,
                PositionX = 0,
                PositionY = 0,
                Width = 1,
                Height = 1,
            };

            _source = new HwndSource(parameters);
            _source.AddHook(WndProc);

            // Let DWM composite the client area as transparent rather than black.
            if (_source.CompositionTarget is { } target)
            {
                target.BackgroundColor = Color.FromArgb(0, 0, 0, 0);
            }

            var margins = new Native.MARGINS { Left = -1, Top = -1, Right = -1, Bottom = -1 };
            Native.DwmExtendFrameIntoClientArea(_source.Handle, ref margins);
        }

        private IntPtr WndProc(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam, ref bool handled)
        {
            if (msg == Native.WM_SETCURSOR)
            {
                Native.SetCursor(_enabled ? IntPtr.Zero : Arrow);
                handled = true;
                return new IntPtr(1);
            }

            return IntPtr.Zero;
        }

        public void Reassert() => Native.SetCursor(_enabled ? IntPtr.Zero : Arrow);

        public void Hide()
        {
            Native.SetCursor(Arrow);
            if (_source is not null)
            {
                Native.ShowWindow(_source.Handle, 0 /* SW_HIDE */);
            }
        }

        public void Dispose()
        {
            Hide();
            _source?.Dispose();
            _source = null;
        }
    }
}
