using System;
using System.IO;

namespace Pointy
{
    /// <summary>Append-only diagnostic log in %APPDATA%\Pointy\pointy.log.</summary>
    internal static class Log
    {
        private static readonly object Gate = new();

        private static string FilePath => Path.Combine(Prefs.DataDir, "pointy.log");

        public static void Write(string message)
        {
            try
            {
                lock (Gate)
                {
                    Directory.CreateDirectory(Prefs.DataDir);
                    File.AppendAllText(FilePath, DateTime.Now.ToString("HH:mm:ss.fff") + " " + message + Environment.NewLine);
                }
            }
            catch
            {
            }
        }
    }
}
