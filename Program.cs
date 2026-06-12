using Avalonia;
using System;
using System.Diagnostics;
using System.Globalization;
using System.Runtime.InteropServices;

namespace DirOpusReImagined
{
    internal class Program
    {
        // Initialization code. Don't use any Avalonia, third-party APIs or any
        // SynchronizationContext-reliant code before AppMain is called: things aren't initialized
        // yet and stuff might break.
        [STAThread]
        public static void Main(string[] args)
        {
            // Avalonia's X11/XWayland backend defaults to scale 1.0 and does not
            // reliably pick up the desktop's fractional scaling. Detect it and
            // hand it to Avalonia via AVALONIA_GLOBAL_SCALE_FACTOR before init.
            if (RuntimeInformation.IsOSPlatform(OSPlatform.Linux))
                EnsureLinuxScaleFactor();

            BuildAvaloniaApp().StartWithClassicDesktopLifetime(args);
        }

        private static void EnsureLinuxScaleFactor()
        {
            // Respect anything the user already set explicitly.
            if (!string.IsNullOrEmpty(Environment.GetEnvironmentVariable("AVALONIA_GLOBAL_SCALE_FACTOR")) ||
                !string.IsNullOrEmpty(Environment.GetEnvironmentVariable("AVALONIA_SCREEN_SCALE_FACTORS")))
                return;

            double scale = DetectScale();
            if (scale > 1.01)
                Environment.SetEnvironmentVariable(
                    "AVALONIA_GLOBAL_SCALE_FACTOR",
                    scale.ToString("0.0#", CultureInfo.InvariantCulture));
        }

        private static double DetectScale()
        {
            // 1) GDK hints (integer GDK_SCALE * fractional GDK_DPI_SCALE)
            if (double.TryParse(Environment.GetEnvironmentVariable("GDK_SCALE"),
                    NumberStyles.Float, CultureInfo.InvariantCulture, out var gdk) && gdk > 0)
            {
                double dpiScale = 1.0;
                double.TryParse(Environment.GetEnvironmentVariable("GDK_DPI_SCALE"),
                    NumberStyles.Float, CultureInfo.InvariantCulture, out dpiScale);
                return gdk * (dpiScale > 0 ? dpiScale : 1.0);
            }

            // 2) Xft.dpi from xrdb (set by GNOME even on Wayland) -> dpi / 96
            try
            {
                var psi = new ProcessStartInfo("xrdb", "-query")
                {
                    RedirectStandardOutput = true,
                    UseShellExecute = false,
                };
                using var p = Process.Start(psi);
                if (p != null)
                {
                    string output = p.StandardOutput.ReadToEnd();
                    p.WaitForExit(1000);
                    foreach (var line in output.Split('\n'))
                    {
                        if (line.StartsWith("Xft.dpi:", StringComparison.OrdinalIgnoreCase))
                        {
                            var val = line.Substring("Xft.dpi:".Length).Trim();
                            if (double.TryParse(val, NumberStyles.Float,
                                    CultureInfo.InvariantCulture, out var dpi) && dpi > 0)
                                return dpi / 96.0;
                        }
                    }
                }
            }
            catch { /* xrdb not present — fall through */ }

            return 1.0;
        }

        // Avalonia configuration, don't remove; also used by visual designer.
        public static AppBuilder BuildAvaloniaApp()
            => AppBuilder.Configure<App>()
                .UsePlatformDetect()
                .LogToTrace();
    }
}
