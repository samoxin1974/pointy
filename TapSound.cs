using System;
using System.Collections.Generic;
using System.IO;
using System.Text;

namespace Pointy
{
    /// <summary>
    /// Tap sounds. Uses MCI so several taps can overlap and no audio dependency is needed.
    /// Mirrors TinyHand: one clip per choice, restarted from the top on every tap.
    /// </summary>
    public sealed class TapSound : IDisposable
    {
        public static readonly IReadOnlyList<(string Key, string Title)> Choices = new List<(string, string)>
        {
            ("thock", "Thock"),
            ("click", "Click"),
            ("pop", "Pop"),
            ("wood", "Wood block"),
            ("boop", "Boop"),
            ("squeak", "Squeak"),
        };

        private readonly Dictionary<string, string> _aliases = new(StringComparer.OrdinalIgnoreCase);
        private readonly HashSet<string> _failed = new(StringComparer.OrdinalIgnoreCase);

        public bool Enabled { get; set; } = true;

        public void Play(string choice)
        {
            if (!Enabled) return;

            var alias = Ensure(choice);
            if (alias is null) return;

            Native.mciSendString($"seek {alias} to start", null, 0, IntPtr.Zero);
            Native.mciSendString($"play {alias}", null, 0, IntPtr.Zero);
        }

        private string? Ensure(string choice)
        {
            if (_aliases.TryGetValue(choice, out var existing)) return existing;
            if (_failed.Contains(choice)) return null;

            var path = Path.Combine(Prefs.AssetsDir, "sounds", $"tap_{choice}.wav");
            if (!File.Exists(path))
            {
                _failed.Add(choice);
                return null;
            }

            var alias = "pointy_" + choice;
            var open = Native.mciSendString($"open \"{path}\" type waveaudio alias {alias}", null, 0, IntPtr.Zero);
            if (open != 0)
            {
                _failed.Add(choice);
                return null;
            }

            Native.mciSendString($"set {alias} time format milliseconds", null, 0, IntPtr.Zero);
            _aliases[choice] = alias;
            return alias;
        }

        public void Dispose()
        {
            foreach (var alias in _aliases.Values)
            {
                Native.mciSendString($"close {alias}", null, 0, IntPtr.Zero);
            }
            _aliases.Clear();
        }
    }
}
