using System;
using System.IO;
using System.Text.Json;

namespace Pointy
{
    /// <summary>User settings, persisted as JSON next to the app's roaming data.</summary>
    public sealed class Prefs
    {
        private static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = true };

        public static string DataDir { get; } = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "Pointy");

        public static string AssetsDir { get; } = Path.Combine(AppContext.BaseDirectory, "Assets");

        private static string FilePath => Path.Combine(DataDir, "settings.json");

        public static Prefs Current { get; private set; } = new();

        public static event Action? Changed;

        public double PointerSize { get; set; } = 0.13;
        public bool ShowShadow { get; set; } = true;
        public double FadeSeconds { get; set; } = 3;
        public bool HandSound { get; set; } = true;
        public bool LaserSound { get; set; }
        public string TapSound { get; set; } = "thock";
        public string InteractKey { get; set; } = "Alt";
        public string InkChoice { get; set; } = "match";
        public string Style { get; set; } = "lavender";
        public string ToggleHotkey { get; set; } = "Ctrl+Alt+H";
        public string ClearHotkey { get; set; } = "Ctrl+Alt+X";
        public bool LaunchAtLogin { get; set; }
        public bool FirstRun { get; set; } = true;

        public static void Load()
        {
            try
            {
                if (File.Exists(FilePath))
                {
                    var json = File.ReadAllText(FilePath);
                    Current = JsonSerializer.Deserialize<Prefs>(json) ?? new Prefs();
                }
            }
            catch
            {
                Current = new Prefs();
            }

            Current.PointerSize = Math.Clamp(Current.PointerSize, 0.08, 0.20);
            Current.FadeSeconds = Math.Clamp(Current.FadeSeconds, 0, 20);
        }

        public static void Save()
        {
            try
            {
                Directory.CreateDirectory(DataDir);
                File.WriteAllText(FilePath, JsonSerializer.Serialize(Current, JsonOptions));
            }
            catch
            {
                // Settings are best-effort; a locked profile should not break the pointer.
            }

            Changed?.Invoke();
        }

        public static void Notify() => Changed?.Invoke();

        /// <summary>Sound toggle for the currently active pointer (hands and laser keep separate switches).</summary>
        public bool SoundFor(bool isLaser)
        {
            return isLaser ? LaserSound : HandSound;
        }

        public void SetSoundFor(bool isLaser, bool value)
        {
            if (isLaser) LaserSound = value;
            else HandSound = value;
            Save();
        }
    }
}
