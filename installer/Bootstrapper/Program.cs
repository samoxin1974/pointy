using System;
using System.Diagnostics;
using System.IO;
using System.IO.Compression;
using System.Reflection;
using System.Runtime.InteropServices;
using Microsoft.Win32;

namespace PointySetup
{
    /// <summary>
    /// Single-file installer for Pointy. The application folder is embedded as payload.zip,
    /// so this exe needs no runtime, no download and no administrator rights.
    /// </summary>
    internal static class Program
    {
        private const string AppName = "Pointy";
        private const string AppExeName = "Pointy.exe";
        private const string AppVersion = "1.0.1";
        private const string UninstallRoot = @"Software\Microsoft\Windows\CurrentVersion\Uninstall";
        private const string RunKey = @"Software\Microsoft\Windows\CurrentVersion\Run";

        private static string UninstallSubKey => UninstallRoot + "\\" + AppName;

        private static string InstallDir => Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Programs", AppName);

        private static string StartMenuDir => Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            "Microsoft\\Windows\\Start Menu\\Programs");

        private static string DesktopDir => Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory);

        private static readonly string LogPath = Path.Combine(Path.GetTempPath(), "PointySetup.log");

        [STAThread]
        private static int Main(string[] args)
        {
            bool silent = Has(args, "/S") || Has(args, "--silent");
            Log("start args=" + string.Join(" ", args) + " silent=" + silent);

            try
            {
                if (Has(args, "--uninstall"))
                {
                    Uninstall();
                    Log("done (uninstall)");
                    return 0;
                }

                if (!silent && MessageBoxW(IntPtr.Zero, ConfirmText(), AppName + " " + AppVersion, 0x04 | 0x20) != 6)
                {
                    Log("cancelled by user");
                    return 0;
                }

                Install(silent);
                Log("done (install)");
                return 0;
            }
            catch (Exception ex)
            {
                Log("FAILED: " + ex);
                if (!silent) MessageBoxW(IntPtr.Zero, ex.Message, AppName + " setup", 0x10);
                return 1;
            }
        }

        private static bool Has(string[] args, string flag)
        {
            foreach (var argument in args)
            {
                if (string.Equals(argument, flag, StringComparison.OrdinalIgnoreCase)) return true;
            }
            return false;
        }

        private static string ConfirmText() =>
            "Install " + AppName + " for this user?\n\n" +
            "Location: " + InstallDir + "\n\n" +
            "Adds a Start Menu shortcut and a desktop shortcut, and lists Pointy under " +
            "Settings > Apps so it can be uninstalled. No administrator rights are needed.";

        private static void Install(bool silent)
        {
            Log("install dir=" + InstallDir);
            KillRunning();

            if (Directory.Exists(InstallDir)) Directory.Delete(InstallDir, true);
            Directory.CreateDirectory(InstallDir);

            ExtractPayload();
            Log("payload extracted");

            var exePath = Path.Combine(InstallDir, AppExeName);
            var self = Environment.ProcessPath;
            if (self is not null)
            {
                File.Copy(self, Path.Combine(InstallDir, "PointySetup.exe"), overwrite: true);
                Log("setup copied for uninstall");
            }

            CreateShortcut(Path.Combine(StartMenuDir, AppName + ".lnk"), exePath);
            Log("start menu shortcut created");

            CreateShortcut(Path.Combine(DesktopDir, AppName + ".lnk"), exePath);
            Log("desktop shortcut created");

            RegisterUninstall(exePath);
            Log("uninstall entry registered");

            if (!silent)
            {
                Process.Start(new ProcessStartInfo(exePath)
                {
                    UseShellExecute = true,
                    WorkingDirectory = InstallDir,
                });
                Log("app launched");
            }
        }

        private static void ExtractPayload()
        {
            var assembly = Assembly.GetExecutingAssembly();
            var name = Array.Find(assembly.GetManifestResourceNames(),
                n => n.EndsWith("payload.zip", StringComparison.OrdinalIgnoreCase));
            if (name is null) throw new InvalidOperationException("The installer payload is missing.");

            using var stream = assembly.GetManifestResourceStream(name)!;
            using var archive = new ZipArchive(stream, ZipArchiveMode.Read);
            archive.ExtractToDirectory(InstallDir, overwriteFiles: true);
        }

        private static void RegisterUninstall(string exePath)
        {
            using var key = Registry.CurrentUser.CreateSubKey(UninstallSubKey);
            if (key is null) throw new InvalidOperationException("Could not write the uninstall entry.");

            key.SetValue("DisplayName", AppName);
            key.SetValue("DisplayVersion", AppVersion);
            key.SetValue("Publisher", AppName);
            key.SetValue("DisplayIcon", exePath);
            key.SetValue("InstallLocation", InstallDir);
            key.SetValue("UninstallString", "\"" + Path.Combine(InstallDir, "PointySetup.exe") + "\" --uninstall");
            key.SetValue("NoModify", 1, RegistryValueKind.DWord);
            key.SetValue("NoRepair", 1, RegistryValueKind.DWord);
            key.SetValue("EstimatedSize", EstimateSizeKb(), RegistryValueKind.DWord);
        }

        private static int EstimateSizeKb()
        {
            try
            {
                long total = 0;
                foreach (var file in Directory.EnumerateFiles(InstallDir, "*", SearchOption.AllDirectories))
                {
                    total += new FileInfo(file).Length;
                }
                return (int)Math.Min(int.MaxValue, total / 1024);
            }
            catch
            {
                return 0;
            }
        }

        private static void Uninstall()
        {
            Log("uninstall dir=" + InstallDir);
            KillRunning();
            SafeDelete(Path.Combine(StartMenuDir, AppName + ".lnk"));
            SafeDelete(Path.Combine(DesktopDir, AppName + ".lnk"));

            try { Registry.CurrentUser.DeleteSubKeyTree(UninstallSubKey, throwOnMissingSubKey: false); } catch { }
            try
            {
                using var run = Registry.CurrentUser.OpenSubKey(RunKey, writable: true);
                run?.DeleteValue(AppName, throwOnMissingValue: false);
            }
            catch { }

            // The folder cannot delete itself while this exe runs out of it, so hand off.
            var cleanup = Path.Combine(Path.GetTempPath(), "pointy-uninstall.cmd");
            var script =
                "@echo off\r\n" +
                "timeout /t 3 /nobreak\r\n" +
                "rmdir /s /q \"" + InstallDir + "\"\r\n" +
                "del \"%~f0\"\r\n";
            File.WriteAllText(cleanup, script);

            Process.Start(new ProcessStartInfo("cmd.exe", "/c \"" + cleanup + "\"")
            {
                CreateNoWindow = true,
                UseShellExecute = false,
            });
            Log("cleanup script launched");
        }

        private static void KillRunning()
        {
            foreach (var process in Process.GetProcessesByName(AppName))
            {
                try
                {
                    process.Kill();
                    process.WaitForExit(3000);
                }
                catch { }
            }
        }

        private static void SafeDelete(string path)
        {
            try { if (File.Exists(path)) File.Delete(path); } catch { }
        }

        /// <summary>
        /// Shell shortcuts are created through WScript.Shell in a child PowerShell process:
        /// in-process COM (IShellLink) is unavailable in a trimmed single-file executable.
        /// </summary>
        private static void CreateShortcut(string lnkPath, string target)
        {
            var script =
                "$s = (New-Object -ComObject WScript.Shell).CreateShortcut('" + Quote(lnkPath) + "'); " +
                "$s.TargetPath = '" + Quote(target) + "'; " +
                "$s.WorkingDirectory = '" + Quote(InstallDir) + "'; " +
                "$s.IconLocation = '" + Quote(target) + ",0'; " +
                "$s.Description = '" + AppName + "'; " +
                "$s.Save()";

            var start = new ProcessStartInfo("powershell.exe",
                "-NoProfile -NonInteractive -ExecutionPolicy Bypass -Command \"" + script + "\"")
            {
                CreateNoWindow = true,
                UseShellExecute = false,
                RedirectStandardError = true,
                RedirectStandardOutput = true,
            };

            using var powershell = Process.Start(start)
                ?? throw new InvalidOperationException("Could not start PowerShell to create the shortcut.");

            var stderr = powershell.StandardError.ReadToEnd();
            var stdout = powershell.StandardOutput.ReadToEnd();
            powershell.WaitForExit(30000);

            if (powershell.ExitCode != 0 || !File.Exists(lnkPath))
            {
                throw new InvalidOperationException(
                    "Could not create the shortcut " + lnkPath + ". " +
                    (stderr.Length > 0 ? stderr : stdout));
            }
        }

        private static string Quote(string value) => value.Replace("'", "''");

        private static void Log(string message)
        {
            try
            {
                File.AppendAllText(LogPath,
                    DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss") + "  " + message + Environment.NewLine);
            }
            catch { }
        }

        [DllImport("user32.dll", CharSet = CharSet.Unicode)]
        private static extern int MessageBoxW(IntPtr hWnd, string text, string caption, uint type);
    }
}
