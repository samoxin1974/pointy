using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Windows.Media;

namespace Pointy
{
    /// <summary>One selectable pointer: either a hand image or the laser dot.</summary>
    public sealed class PointerStyle
    {
        public string Id { get; }
        public string Title { get; }
        public Color Ink { get; }
        public bool IsLaser { get; }

        public PointerStyle(string id, string title, Color ink, bool isLaser = false)
        {
            Id = id;
            Title = title;
            Ink = ink;
            IsLaser = isLaser;
        }

        public string? AssetName => IsLaser ? null : "hand_" + Id;
        public override string ToString() => Title;
    }

    public static class Pointers
    {
        private static Color Hex(uint rgb) => Color.FromRgb(
            (byte)((rgb >> 16) & 0xFF), (byte)((rgb >> 8) & 0xFF), (byte)(rgb & 0xFF));

        public static readonly Color Purple = Hex(0x8754F2);
        public static readonly Color Red = Hex(0xFF291F);

        public static readonly IReadOnlyList<PointerStyle> Hands = new List<PointerStyle>
        {
            new("lavender", "Lavender toy", Purple),
            new("glove",    "Cursor glove", Purple),
            new("coral",    "Coral vinyl",  Red),
            new("plush",    "Plush",        Hex(0x2F86F0)),
            new("robot",    "Robot",        Hex(0x16A87A)),
            new("pink",     "Bubblegum",    Hex(0xEE4F98)),
            new("blue",     "Sky",          Hex(0x2F86F0)),
            new("mint",     "Mint",         Hex(0x16A87A)),
            new("lemon",    "Lemon",        Hex(0xF2B705)),
            new("black",    "Midnight",     Hex(0x1D1D21)),
            new("chrome",   "Chrome",       Hex(0x6B7380)),
            new("holo",     "Holo",         Purple),
            new("fuzzy",    "Fuzzy",        Purple),
            new("wood",     "Wood",         Hex(0x9A5E2C)),
            new("marble",   "Marble",       Hex(0x6B7380)),
            new("terrazzo", "Terrazzo",     Hex(0xF0561D)),
            new("glass",    "Glass",        Hex(0x1597E5)),
            new("jelly",    "Jelly",        Hex(0xA33FE6)),
            new("pastel",   "Pastel",       Hex(0xEE4F98)),
            new("pixel",    "Pixel",        Hex(0x1D1D21)),
            new("lowpoly",  "Low poly",     Purple),
            new("knit",     "Knit",         Purple),
            new("skeleton", "Skeleton",     Hex(0x9A5E2C)),
            new("astro",    "Astronaut",    Hex(0x2F86F0)),
            new("space",    "Space",        Red),
            new("monster",  "Monster",      Hex(0x3E9E2E)),
            new("water",    "Water",        Hex(0x1597E5)),
            new("lava",     "Lava",         Hex(0xF0561D)),
            new("grass",    "Grass",        Hex(0x3E9E2E)),
            new("cactus",   "Cactus",       Hex(0x3E9E2E)),
            new("choco",    "Chocolate",    Hex(0x9A5E2C)),
            new("cloud",    "Cloud",        Hex(0x2F86F0)),
            new("mech",     "Mech",         Hex(0xF0561D)),
            new("neon",     "Neon",         Hex(0xE0309A)),
            new("leaf",     "Leaf",         Hex(0x3E9E2E)),
            new("cookie",   "Gingerbread",  Hex(0x9A5E2C)),
        }.AsReadOnly();

        public static readonly PointerStyle Laser = new("laser", "Laser dot", Red, isLaser: true);

        public static IReadOnlyList<PointerStyle> All { get; } =
            Hands.Concat(new[] { Laser }).ToList().AsReadOnly();

        public static PointerStyle Get(string? id)
        {
            if (string.IsNullOrEmpty(id)) return Hands[0];
            foreach (var s in All)
            {
                if (string.Equals(s.Id, id, StringComparison.OrdinalIgnoreCase)) return s;
            }
            return Hands[0];
        }

        public static readonly IReadOnlyList<(string Key, string Title)> InkChoices = new List<(string, string)>
        {
            ("match", "Match the pointer"),
            ("red", "Red"),
            ("orange", "Orange"),
            ("yellow", "Yellow"),
            ("green", "Green"),
            ("blue", "Blue"),
            ("purple", "Purple"),
            ("white", "White"),
            ("black", "Black"),
        };

        public static Color ResolveInk(string? choice, PointerStyle style)
        {
            switch ((choice ?? "match").ToLowerInvariant())
            {
                case "red": return Red;
                case "orange": return Hex(0xFF8A00);
                case "yellow": return Hex(0xFFCC00);
                case "green": return Hex(0x30C25A);
                case "blue": return Hex(0x0A84FF);
                case "purple": return Purple;
                case "white": return Colors.White;
                case "black": return Hex(0x1D1D21);
                default: return style.Ink;
            }
        }

        public static readonly IReadOnlyList<(string Key, string Title)> InteractKeys = new List<(string, string)>
        {
            ("Alt", "Alt"),
            ("Control", "Ctrl"),
            ("Shift", "Shift"),
            ("Win", "Win"),
            ("None", "Off"),
        };

        public static bool InteractHeld(string? key)
        {
            switch ((key ?? "Alt").ToLowerInvariant())
            {
                case "alt": return Down(Native.VK_MENU);
                case "control": return Down(Native.VK_CONTROL);
                case "shift": return Down(Native.VK_SHIFT);
                case "win": return Down(Native.VK_LWIN);
                default: return false;
            }
        }

        private static bool Down(int vk) => (Native.GetAsyncKeyState(vk) & 0x8000) != 0;

        public static string FormatSize(double frac) =>
            (frac * 100).ToString("0", CultureInfo.InvariantCulture) + "% of screen height";
    }
}
