# Pointy

A hand on a stick instead of your mouse pointer, for Windows.

Pointy hides the system cursor and draws a pointer hand that swings from below
the bottom of the screen so its finger tip sits exactly on your mouse position.
Left click taps, dragging draws ink, and holding a modifier key hands control
back to the real cursor.

Built with C# and WPF on .NET 8. No third-party packages.

## Install on Windows

**The single installer.** Download `PointySetup-1.0.1.exe` from
[Releases](https://github.com/samoxin1974/pointy/releases/latest) - one 140 MB
file you can send to anyone on 64-bit Windows 10 or 11. (Building it yourself
puts the same file at `dist/PointySetup-1.0.1.exe`.) Double-click it and confirm: it
copies Pointy to `%LOCALAPPDATA%\Programs\Pointy`, adds a Start Menu shortcut
and a desktop shortcut, lists Pointy under Settings > Apps so it can be
uninstalled the normal way, and starts the app. No administrator rights are
needed and nothing is written outside your user profile. The .NET runtime is
bundled, so the other person installs nothing else. Run it with `/S` for a
silent, no-dialog install.

**Uninstalling** goes through Windows: Settings > Apps > Pointy > Uninstall, or
`"%LOCALAPPDATA%\Programs\Pointy\PointySetup.exe" --uninstall`. That removes
the app, both shortcuts and the registry entry. Your settings stay in
`%APPDATA%\Pointy` - delete that folder to start clean.

**Portable.** A published `dist/Pointy` folder holds `Pointy.exe` and an `Assets` folder. Keep them
together and run the exe straight out of the folder; nothing is installed.

The build is unsigned, so Windows SmartScreen warns about an unknown publisher
the first time it runs - choose **More info** then **Run anyway**.

## Build and run

```bash
cd Pointy
dotnet build
dotnet run
```

The build produces `bin/Debug/net8.0-windows/Pointy.exe`. To publish a
standalone folder:

```bash
dotnet publish -c Release -r win-x64 --self-contained true -o ../dist/Pointy
```

To rebuild the single-file installer, the bootstrapper project in
`installer/Bootstrapper` embeds that published folder as `payload.zip`:

```bash
# 1. zip the *contents* of dist/Pointy (not the folder itself) into
#    installer/Bootstrapper/payload.zip, with forward-slash entry names
# 2. build the installer that embeds the zip
cd installer/Bootstrapper
dotnet publish -c Release -o ../../../dist/installer
# -> dist/installer/PointySetup.exe
```

`installer/Pointy.iss` is an equivalent Inno Setup script, kept for anyone who
would rather compile a conventional installer with `iscc`.

## Using it

| Action | What happens |
| --- | --- |
| Click the tray icon | Turn the pointer on or off |
| `Ctrl+Alt+H` | Turn the pointer on or off |
| `Ctrl+Alt+X` | Clear the drawing |
| Left click | A tap: the hand presses down and a sound plays |
| Drag | Draws ink that fades after a delay |
| Hold `Alt` | The real cursor comes back, clicks go through normally |
| `Esc` | Turn the pointer off |

The taskbar and open menus automatically get the real cursor back, because the
pointer would otherwise sit between you and their hover states.

Everything is configurable in Settings: pointer style (36 hands plus a laser
dot), size, drop shadow, ink colour, tap sound, the hold-to-click key, the two
shortcuts, and start-at-login.

## How it works

The hard part on any platform is showing a decoration over the entire desktop
without taking input away from it. Windows turns out to be obliging:

* **One overlay window per monitor** (`OverlayWindow`), positioned at
  `HWND_TOPMOST` and extended with `WS_EX_TRANSPARENT | WS_EX_LAYERED |
  WS_EX_TOOLWINDOW | WS_EX_NOACTIVATE`. `WS_EX_TRANSPARENT` means every mouse
  message falls through to the application below, so hover states, right clicks,
  scrolling and native drag and drop all keep working. The window never takes
  focus, so it never disturbs the foreground app.
* **Hiding the cursor** needs a window that Windows actually routes the cursor
  to, and layered windows are skipped for cursor routing. So `CursorHider` is a
  plain click-through window with no layered style whose client area DWM
  composites as fully transparent: invisible to the user, but it receives
  `WM_SETCURSOR` and answers with `SetCursor(NULL)`. The frame loop re-asserts
  the null cursor as well, because applications set their own cursor on every
  mouse move.
* **Swallowing clicks** is the only job left for a hook. A `WH_MOUSE_LL` hook
  returns 1 for left button down and up while the pointer is on, which turns
  them into taps; everything else is passed straight through with
  `CallNextHookEx`. It only runs while the pointer is active.
* **The arm** (`OverlaySurface`) is pure geometry. The grip sits below the
  bottom edge of the screen, and the frame is rotated so the finger tip lands on
  the cursor, with the rod length solved from the reach:
  `rodLen = sqrt(reach² − tipOffsetX²) − handTipLength`. The hand and rod are two
  slices of the same PNG, so the rod stretches while the hand keeps its
  proportions.
* **Hand anchors** (`HandAsset`) are measured from each image's alpha channel at
  load time: where the finger tip is, where the wrist ends, where the rod starts
  and stops. That is why any hand-shaped PNG fits without hand-tuned numbers.

### Layout

```
App.xaml.cs         startup, hotkeys, settings/tray wiring
PointerSession.cs   activation, mouse hook, cursor state, frame loop
OverlayWindow.cs    click-through topmost window, WM_SETCURSOR
OverlaySurface.cs   arm, laser dot and ink rendering
InkLayer.cs         stroke capture, smoothing and fading
HandAsset.cs        alpha-channel measurement, shadow, thumbnails
PointerStyle.cs     pointer catalogue, ink palette, modifier key
Prefs.cs            JSON settings in %APPDATA%\Pointy
TapSound.cs         MCI playback so taps can overlap
TrayIcon.cs         notification area icon and menu
SettingsWindow.xaml settings UI
```

## Known limitations

* Applications running **elevated** cannot have their clicks intercepted by a
  non-elevated hook, so a tap over an administrator window opens normally. Run
  Pointy elevated to cover them.
* **Exclusive full-screen games** bypass desktop composition and will not show
  the overlay.
* On **mixed-DPI** multi-monitor setups the overlay is rebuilt when the pointer
  changes monitor. A drag in progress is dropped at that moment.
* The pointer is drawn with WPF layered windows, which are composited on the
  CPU. On large, high-DPI displays a very high refresh pointer can cost more
  than a native one; idle time is throttled to keep that in check.
* The **UAC secure desktop** and the lock screen are separate desktops; Pointy
  is not visible there.

## License and credits

Pointy is an independent Windows build inspired by
[TinyHand](https://github.com/nilbuild/tinyhand). The hand artwork and tap
sounds come from that project under the MIT license — see
[THIRD-PARTY-NOTICES.md](THIRD-PARTY-NOTICES.md).
