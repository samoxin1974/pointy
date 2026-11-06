using System;
using System.Drawing;
using System.IO;
using System.Windows.Forms;

namespace Pointy
{
    /// <summary>Notification area icon: click toggles the pointer, right click opens the menu.</summary>
    public sealed class TrayIcon : IDisposable
    {
        private readonly NotifyIcon _icon;
        private readonly ToolStripMenuItem _toggleItem;
        private readonly ToolStripMenuItem _soundItem;

        /// <summary>Raised when the session state changes so the menu labels can refresh.</summary>
        public static Action? RefreshMenu { get; set; }

        public TrayIcon()
        {
            var contextMenu = new ContextMenuStrip();

            _toggleItem = new ToolStripMenuItem("Turn pointer on");
            _toggleItem.Click += (_, _) => App.Session?.Toggle();

            var clearItem = new ToolStripMenuItem("Clear drawing");
            clearItem.Click += (_, _) => App.Session?.ClearInk();

            _soundItem = new ToolStripMenuItem("Tap sound");
            _soundItem.Click += (_, _) =>
            {
                if (App.Session is null) return;
                App.Session.SoundOn = !App.Session.SoundOn;
            };

            var settingsItem = new ToolStripMenuItem("Settings…");
            settingsItem.Click += (_, _) => App.ShowSettings();

            var exitItem = new ToolStripMenuItem("Quit Pointy");
            exitItem.Click += (_, _) => App.ExitApp();

            contextMenu.Items.Add(_toggleItem);
            contextMenu.Items.Add(clearItem);
            contextMenu.Items.Add(_soundItem);
            contextMenu.Items.Add(new ToolStripSeparator());
            contextMenu.Items.Add(settingsItem);
            contextMenu.Items.Add(new ToolStripSeparator());
            contextMenu.Items.Add(exitItem);
            contextMenu.Opening += (_, _) => Refresh();

            _icon = new NotifyIcon
            {
                Text = "Pointy",
                Visible = true,
                ContextMenuStrip = contextMenu,
            };

            var iconPath = Path.Combine(Prefs.AssetsDir, "pointy.ico");
            try
            {
                if (File.Exists(iconPath)) _icon.Icon = new Icon(iconPath);
            }
            catch
            {
                _icon.Icon = SystemIcons.Application;
            }

            _icon.MouseClick += (_, e) =>
            {
                if (e.Button == MouseButtons.Left) App.Session?.Toggle();
            };

            RefreshMenu = Refresh;
            Refresh();
        }

        private void Refresh()
        {
            var session = App.Session;
            bool active = session?.IsActive ?? false;

            _toggleItem.Text = active ? "Turn pointer off" : "Turn pointer on";
            _soundItem.Checked = session?.SoundOn ?? true;
            _icon.Text = active ? "Pointy — pointer is on" : "Pointy — pointer is off";
        }

        public void Dispose()
        {
            RefreshMenu = null;
            _icon.Visible = false;
            _icon.Dispose();
        }
    }
}
