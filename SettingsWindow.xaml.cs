using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media.Imaging;

namespace Pointy
{
    public sealed class Choice
    {
        public string Key { get; set; } = "";
        public string Title { get; set; } = "";
        public override string ToString() => Title;
    }

    public sealed class StyleItem : INotifyPropertyChanged
    {
        private BitmapSource? _thumb;

        public StyleItem(PointerStyle style) => Style = style;

        public PointerStyle Style { get; }
        public string Title => Style.Title;

        public BitmapSource? Thumb
        {
            get => _thumb;
            set
            {
                _thumb = value;
                PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(Thumb)));
            }
        }

        public event PropertyChangedEventHandler? PropertyChanged;
    }

    public partial class SettingsWindow : Window
    {
        private readonly List<StyleItem> _items = new();
        private bool _loading = true;

        public SettingsWindow()
        {
            InitializeComponent();

            var iconPath = Path.Combine(Prefs.AssetsDir, "pointy.png");
            try
            {
                if (File.Exists(iconPath))
                {
                    var icon = BitmapFrame.Create(new Uri(iconPath));
                    Icon = icon;
                    AppIcon.Source = icon;
                }
            }
            catch
            {
                // Missing artwork should not stop the window from opening.
            }

            TapSoundBox.ItemsSource = TapSound.Choices
                .Select(c => new Choice { Key = c.Key, Title = c.Title }).ToList();
            InkBox.ItemsSource = Pointers.InkChoices
                .Select(c => new Choice { Key = c.Key, Title = c.Title }).ToList();
            InteractBox.ItemsSource = Pointers.InteractKeys
                .Select(c => new Choice { Key = c.Key, Title = c.Title }).ToList();

            foreach (var style in Pointers.All)
            {
                _items.Add(new StyleItem(style));
            }
            StyleList.ItemsSource = _items;

            LoadFromPrefs();
            _loading = false;

            Loaded += async (_, _) => await LoadThumbnailsAsync();
        }

        private async Task LoadThumbnailsAsync()
        {
            var items = _items.ToList();
            await Task.Run(() =>
            {
                foreach (var item in items)
                {
                    var name = item.Style.AssetName;
                    if (name is null) continue;

                    var asset = HandAsset.Load(name);
                    if (asset is null) continue;

                    var thumb = asset.Thumbnail;
                    Dispatcher.Invoke(() => item.Thumb = thumb);
                }
            });
        }

        private void LoadFromPrefs()
        {
            var prefs = Prefs.Current;
            var style = Pointers.Get(prefs.Style);

            SizeSlider.Value = Math.Clamp(prefs.PointerSize, 0.08, 0.20);
            SizeText.Text = (prefs.PointerSize * 100).ToString("0") + "% of screen height";
            ShadowCheck.IsChecked = prefs.ShowShadow;
            SoundCheck.IsChecked = prefs.SoundFor(style.IsLaser);
            LaunchCheck.IsChecked = prefs.LaunchAtLogin;

            SelectKey(InkBox, prefs.InkChoice);
            SelectKey(TapSoundBox, prefs.TapSound);
            SelectKey(InteractBox, prefs.InteractKey);

            ToggleHotkeyBox.Text = HotkeySpec.Parse(prefs.ToggleHotkey, HotkeySpec.DefaultToggle).Display;
            ClearHotkeyBox.Text = HotkeySpec.Parse(prefs.ClearHotkey, HotkeySpec.DefaultClear).Display;

            StyleList.SelectedItem = _items.FirstOrDefault(i => i.Style.Id == style.Id) ?? _items.FirstOrDefault();
            if (StyleList.SelectedItem is not null)
            {
                StyleList.ScrollIntoView(StyleList.SelectedItem);
            }
        }

        private static void SelectKey(ComboBox box, string key)
        {
            var match = (box.ItemsSource as IEnumerable<Choice>)?
                .FirstOrDefault(c => string.Equals(c.Key, key, StringComparison.OrdinalIgnoreCase));
            box.SelectedItem = match ?? (box.ItemsSource as IEnumerable<Choice>)?.FirstOrDefault();
        }

        private static string? SelectedKey(ComboBox box) => (box.SelectedItem as Choice)?.Key;

        private void StyleList_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (_loading) return;
            if (StyleList.SelectedItem is not StyleItem item) return;

            Prefs.Current.Style = item.Style.Id;
            Prefs.Save();
            SoundCheck.IsChecked = Prefs.Current.SoundFor(item.Style.IsLaser);
        }

        private void SizeSlider_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
        {
            if (_loading) return;
            Prefs.Current.PointerSize = e.NewValue;
            SizeText.Text = (e.NewValue * 100).ToString("0") + "% of screen height";
            Prefs.Save();
        }

        private void ShadowCheck_Click(object sender, RoutedEventArgs e)
        {
            if (_loading) return;
            Prefs.Current.ShowShadow = ShadowCheck.IsChecked == true;
            Prefs.Save();
        }

        private void InkBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (_loading) return;
            var key = SelectedKey(InkBox);
            if (key is null) return;
            Prefs.Current.InkChoice = key;
            Prefs.Save();
        }

        private void TapSoundBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (_loading) return;
            var key = SelectedKey(TapSoundBox);
            if (key is null) return;
            Prefs.Current.TapSound = key;
            Prefs.Save();
        }

        private void SoundCheck_Click(object sender, RoutedEventArgs e)
        {
            if (_loading) return;
            Prefs.Current.SetSoundFor(Pointers.Get(Prefs.Current.Style).IsLaser, SoundCheck.IsChecked == true);
        }

        private void InteractBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (_loading) return;
            var key = SelectedKey(InteractBox);
            if (key is null) return;
            Prefs.Current.InteractKey = key;
            Prefs.Save();
        }

        private void Hotkey_LostFocus(object sender, RoutedEventArgs e)
        {
            if (_loading) return;

            var toggle = HotkeySpec.Parse(ToggleHotkeyBox.Text, HotkeySpec.DefaultToggle);
            var clear = HotkeySpec.Parse(ClearHotkeyBox.Text, HotkeySpec.DefaultClear);

            Prefs.Current.ToggleHotkey = toggle.Display;
            Prefs.Current.ClearHotkey = clear.Display;

            ToggleHotkeyBox.Text = toggle.Display;
            ClearHotkeyBox.Text = clear.Display;

            Prefs.Save();
            App.RegisterHotkeys();
        }

        private void LaunchCheck_Click(object sender, RoutedEventArgs e)
        {
            if (_loading) return;
            Prefs.Current.LaunchAtLogin = LaunchCheck.IsChecked == true;
            Prefs.Save();
            Startup.ApplyLaunchAtLogin(Prefs.Current.LaunchAtLogin);
        }

        private void Close_Click(object sender, RoutedEventArgs e) => Close();
    }
}
