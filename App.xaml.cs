using System;
using System.Threading;
using System.Windows;

namespace Pointy
{
    public partial class App : System.Windows.Application
    {
        private Mutex? _instanceMutex;

        public static PointerSession? Session { get; private set; }
        public static HotkeyHost? Hotkeys { get; private set; }
        public static TrayIcon? Tray { get; private set; }
        public static SettingsWindow? Settings { get; private set; }

        protected override void OnStartup(StartupEventArgs e)
        {
            _instanceMutex = new Mutex(true, @"Local\Pointy.SingleInstance", out bool isFirst);
            if (!isFirst)
            {
                Shutdown();
                return;
            }

            base.OnStartup(e);

            Prefs.Load();

            Session = new PointerSession();
            Hotkeys = new HotkeyHost();
            Tray = new TrayIcon();

            RegisterHotkeys();
            Pointy.Startup.ApplyLaunchAtLogin(Prefs.Current.LaunchAtLogin);

            if (Prefs.Current.FirstRun)
            {
                Prefs.Current.FirstRun = false;
                Prefs.Save();
                ShowSettings();
            }
        }

        public static void RegisterHotkeys()
        {
            if (Hotkeys is null) return;

            Hotkeys.UnregisterAll();

            var toggle = HotkeySpec.Parse(Prefs.Current.ToggleHotkey, HotkeySpec.DefaultToggle);
            var clear = HotkeySpec.Parse(Prefs.Current.ClearHotkey, HotkeySpec.DefaultClear);

            bool t = Hotkeys.Register(1, toggle, () => Session?.Toggle());
            bool c = Hotkeys.Register(2, clear, () => Session?.ClearInk());
            Log.Write($"hotkeys toggle={t} clear={c} key={toggle.Display}");
        }

        public static void ShowSettings()
        {
            Session?.Deactivate();

            if (Settings is null)
            {
                Settings = new SettingsWindow();
                Settings.Closed += (_, _) => Settings = null;
            }

            Settings.Show();
            if (Settings.WindowState == WindowState.Minimized) Settings.WindowState = WindowState.Normal;
            Settings.Activate();
        }

        public static void ExitApp()
        {
            Session?.Dispose();
            Hotkeys?.Dispose();
            Tray?.Dispose();
            Current.Shutdown();
        }

        protected override void OnExit(ExitEventArgs e)
        {
            Session?.Dispose();
            Hotkeys?.Dispose();
            Tray?.Dispose();
            _instanceMutex?.Dispose();
            base.OnExit(e);
        }
    }
}
