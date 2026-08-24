using System;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text.Json;
using System.Xml.Linq;

namespace DirOpusReImagined
{
    /// <summary>
    /// Works out the display scale factor the desktop actually wants and hands it to Avalonia before
    /// the windowing platform initialises.
    ///
    /// Avalonia's X11 backend only consults AVALONIA_GLOBAL_SCALE_FACTOR, AVALONIA_SCREEN_SCALE_FACTORS,
    /// the QT_* scaling variables, and the Xft.dpi X resource. It does <b>not</b> read GDK_SCALE, and
    /// under Wayland it runs through XWayland where the compositor's per-monitor scale is invisible to
    /// it. On a HiDPI Wayland desktop every one of those sources is typically empty, so Avalonia falls
    /// back to 1.0 and the whole app comes up at a fraction of its intended size.
    ///
    /// <see cref="Bootstrap"/> closes that gap: it resolves a factor from the desktop and exports it as
    /// AVALONIA_GLOBAL_SCALE_FACTOR. It MUST be called before any other Avalonia API — the variable is
    /// read once, when the platform starts.
    ///
    /// Only Linux/Unix is auto-detected. Windows and macOS report DPI to Avalonia natively, so this is
    /// a no-op there apart from an explicit user override.
    /// </summary>
    public static class DisplayScaling
    {
        private const string GlobalScaleVar = "AVALONIA_GLOBAL_SCALE_FACTOR";
        private const string ScreenScaleVar = "AVALONIA_SCREEN_SCALE_FACTORS";

        /// <summary>Lower/upper sanity bounds. A desktop reporting anything outside this is ignored.</summary>
        private const double MinScale = 0.5;
        private const double MaxScale = 4.0;

        /// <summary>The factor actually handed to Avalonia this session, or 1.0 if none was applied.</summary>
        public static double AppliedScale { get; private set; } = 1.0;

        /// <summary>Human-readable description of where <see cref="AppliedScale"/> came from, for the
        /// settings dialog and the "auto" watermark.</summary>
        public static string AppliedSource { get; private set; } = "not applied (Avalonia default)";

        /// <summary>What auto-detection alone would produce, ignoring any saved &lt;UiScale&gt; override.
        /// Computed lazily so the settings dialog can show the user what "Auto" resolves to.</summary>
        public static double DetectedScale
        {
            get
            {
                if (_detected == null)
                {
                    var (scale, source) = DetectFromDesktop();
                    _detected = scale;
                    _detectedSource = source;
                }
                return _detected.Value;
            }
        }

        /// <summary>Description of the source behind <see cref="DetectedScale"/>.</summary>
        public static string DetectedSource
        {
            get { _ = DetectedScale; return _detectedSource ?? "no desktop scale found"; }
        }

        private static double? _detected;
        private static string? _detectedSource;

        /// <summary>
        /// Resolves the scale factor and exports it to the environment. Call as the very first
        /// statement in Main, before <c>BuildAvaloniaApp()</c>.
        ///
        /// Priority, highest first:
        /// <list type="number">
        /// <item><c>--scale N</c> on the command line (debugging escape hatch).</item>
        /// <item>An AVALONIA_GLOBAL_SCALE_FACTOR / AVALONIA_SCREEN_SCALE_FACTORS already in the
        ///       environment — if the user set it, it wins and we do not touch it.</item>
        /// <item>The saved &lt;UiScale&gt; from Configuration.xml (0 or absent means "auto").</item>
        /// <item>Auto-detection from the running desktop.</item>
        /// </list>
        /// Every step is best-effort: any failure falls through to the next, and a total failure
        /// simply leaves Avalonia's own behaviour untouched.
        /// </summary>
        public static void Bootstrap(string[]? args)
        {
            try
            {
                // 1. Command line always wins, even over an inherited environment variable, so a bad
                //    saved setting can never lock the user out of a readable window.
                var (cliScale, cliOk) = ParseScaleArgument(args);
                if (cliOk)
                {
                    Apply(cliScale, "--scale command-line switch");
                    return;
                }

                // 2. An explicit environment variable is the user speaking directly to Avalonia.
                //    Record what they chose (so the settings dialog can show it) but change nothing.
                string? envGlobal = Environment.GetEnvironmentVariable(GlobalScaleVar);
                if (!string.IsNullOrWhiteSpace(envGlobal))
                {
                    if (TryParseScale(envGlobal, out var envScale))
                    {
                        AppliedScale = envScale;
                        AppliedSource = GlobalScaleVar + " environment variable";
                    }
                    return;
                }
                if (!string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable(ScreenScaleVar)))
                {
                    AppliedSource = ScreenScaleVar + " environment variable";
                    return;
                }

                // 3. The saved preference. Anything <= 0 means "auto", which falls through to step 4.
                double configured = ReadConfiguredScale();
                if (configured > 0)
                {
                    Apply(configured, "UiScale setting in Configuration.xml");
                    return;
                }

                // 4. Ask the desktop.
                var (detected, source) = DetectFromDesktop();
                _detected = detected;
                _detectedSource = source;
                if (detected > 0)
                    Apply(detected, source);
            }
            catch
            {
                // Scaling is a convenience, never a reason to fail startup. Leave Avalonia's default.
            }
        }

        /// <summary>Clamps, rounds and exports <paramref name="scale"/>. Values indistinguishable from
        /// 1.0 are deliberately not exported, so Avalonia's own Xft.dpi handling still gets a chance.</summary>
        private static void Apply(double scale, string source)
        {
            if (double.IsNaN(scale) || double.IsInfinity(scale)) return;
            scale = Math.Round(Math.Clamp(scale, MinScale, MaxScale), 2);
            if (Math.Abs(scale - 1.0) < 0.01) return;

            Environment.SetEnvironmentVariable(
                GlobalScaleVar, scale.ToString("0.##", CultureInfo.InvariantCulture));
            AppliedScale = scale;
            AppliedSource = source;
        }

        // ---------------------------------------------------------------- command line

        /// <summary>Accepts <c>--scale 1.5</c>, <c>--scale=1.5</c> and the <c>/scale</c> spellings.</summary>
        private static (double Scale, bool Found) ParseScaleArgument(string[]? args)
        {
            if (args == null) return (0, false);

            for (int i = 0; i < args.Length; i++)
            {
                string a = args[i];
                if (a.StartsWith("--scale=", StringComparison.OrdinalIgnoreCase) ||
                    a.StartsWith("/scale=", StringComparison.OrdinalIgnoreCase))
                {
                    if (TryParseScale(a[(a.IndexOf('=') + 1)..], out var inline)) return (inline, true);
                }
                else if (a.Equals("--scale", StringComparison.OrdinalIgnoreCase) ||
                         a.Equals("/scale", StringComparison.OrdinalIgnoreCase))
                {
                    if (i + 1 < args.Length && TryParseScale(args[i + 1], out var next)) return (next, true);
                }
            }
            return (0, false);
        }

        // ---------------------------------------------------------------- saved setting

        /// <summary>
        /// Reads &lt;UiScale&gt; out of Configuration.xml. This runs before MainWindow exists, so it
        /// repeats MainWindow.FindConfigurationFile's search order rather than sharing state with it:
        /// working directory, then the executable's folder, then the per-platform config location.
        /// </summary>
        private static double ReadConfiguredScale()
        {
            foreach (string path in ConfigSearchPaths())
            {
                try
                {
                    if (!File.Exists(path)) continue;
                    var el = XDocument.Load(path).Descendants("UiScale").FirstOrDefault();
                    if (el == null) return 0;                 // file found, setting absent => auto
                    return TryParseScale(el.Value, out var v) ? v : 0;
                }
                catch { /* unreadable or malformed: try the next location */ }
            }
            return 0;
        }

        /// <summary>The same locations MainWindow searches, in the same order.</summary>
        private static string[] ConfigSearchPaths()
        {
            const string configName = "Configuration.xml";
            string home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);

            string platformPath;
            if (RuntimeInformation.IsOSPlatform(OSPlatform.OSX))
                platformPath = Path.Combine(home, "Library", "Application Support", "dori", configName);
            else if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
                platformPath = Path.Combine(
                    Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "dori", configName);
            else
                platformPath = Path.Combine(home, ".config", "dori", configName);

            return new[]
            {
                Path.Combine(Environment.CurrentDirectory, configName),
                Path.Combine(AppContext.BaseDirectory, configName),
                platformPath,
            };
        }

        // ---------------------------------------------------------------- desktop detection

        /// <summary>
        /// Asks the running desktop what scale it is using. Returns 0 when nothing usable was found,
        /// which leaves Avalonia's own Xft.dpi / QT_* handling in charge.
        /// </summary>
        private static (double Scale, string Source) DetectFromDesktop()
        {
            // Windows and macOS report DPI to Avalonia properly; overriding would double-apply.
            if (!RuntimeInformation.IsOSPlatform(OSPlatform.Linux) &&
                !RuntimeInformation.IsOSPlatform(OSPlatform.FreeBSD))
                return (0, "native platform DPI");

            bool wayland =
                string.Equals(Environment.GetEnvironmentVariable("XDG_SESSION_TYPE"), "wayland",
                              StringComparison.OrdinalIgnoreCase) ||
                !string.IsNullOrEmpty(Environment.GetEnvironmentVariable("WAYLAND_DISPLAY"));

            if (wayland)
            {
                var hypr = DetectHyprland();
                if (hypr.Scale > 0 || hypr.Blocked) return (hypr.Scale, hypr.Source);

                var gnome = DetectGnome();
                if (gnome.Scale > 0) return gnome;

                var wlr = DetectWlrRandr();
                if (wlr.Scale > 0) return wlr;
            }

            // GDK_SCALE/GDK_DPI_SCALE are set by most desktops and ignored by Avalonia entirely, so
            // they are worth honouring on X11 and as a Wayland fallback.
            var gdk = DetectGdk();
            if (gdk.Scale > 0) return gdk;

            // QT_* and Xft.dpi are left alone deliberately: Avalonia already reads both, and setting
            // AVALONIA_GLOBAL_SCALE_FACTOR from them would just duplicate its own work.
            return (0, "no desktop scale found");
        }

        /// <summary>
        /// Hyprland exposes the true per-monitor scale via hyprctl.
        ///
        /// The catch is XWayland. With <c>xwayland:force_zero_scaling = true</c> Hyprland hands the
        /// client the raw pixel resolution and expects it to scale itself — exactly what we want. With
        /// the option off, Hyprland renders the XWayland surface at 1x and upscales it, so applying the
        /// factor here as well would double-apply it (1.6 x 1.6 = 2.56x). In that case we report
        /// <c>Blocked</c> so the caller stops rather than falling through to GDK_SCALE, which would hit
        /// the same double-scaling problem.
        /// </summary>
        private static (double Scale, string Source, bool Blocked) DetectHyprland()
        {
            bool isHyprland =
                !string.IsNullOrEmpty(Environment.GetEnvironmentVariable("HYPRLAND_INSTANCE_SIGNATURE")) ||
                (Environment.GetEnvironmentVariable("XDG_CURRENT_DESKTOP") ?? "")
                    .Contains("hyprland", StringComparison.OrdinalIgnoreCase);
            if (!isHyprland) return (0, "", false);

            string? optionJson = RunCommand("hyprctl", "getoption xwayland:force_zero_scaling -j");
            bool forceZero = false;
            bool answered = false;
            if (optionJson != null)
            {
                try
                {
                    using var doc = JsonDocument.Parse(optionJson);
                    if (doc.RootElement.TryGetProperty("bool", out var b))
                    {
                        forceZero = b.ValueKind == JsonValueKind.True ||
                                    (b.ValueKind == JsonValueKind.Number && b.GetInt32() != 0);
                        answered = true;
                    }
                }
                catch { /* unparseable: treated the same as no answer at all */ }
            }

            // Blocked either way, but for different reasons worth telling apart in the settings dialog.
            // When we could not ask at all we still block, because the option defaults to off and
            // guessing wrong here means scaling on top of the compositor's own scaling.
            if (!forceZero)
                return (0, answered
                    ? "Hyprland is upscaling XWayland itself (xwayland:force_zero_scaling is off)"
                    : "Hyprland detected but hyprctl could not be queried; assuming the compositor scales XWayland",
                    true);

            string? monitorsJson = RunCommand("hyprctl", "monitors -j");
            if (monitorsJson == null) return (0, "", false);

            try
            {
                using var doc = JsonDocument.Parse(monitorsJson);
                if (doc.RootElement.ValueKind != JsonValueKind.Array) return (0, "", false);

                JsonElement? chosen = null;
                foreach (var monitor in doc.RootElement.EnumerateArray())
                {
                    chosen ??= monitor;                       // fall back to the first monitor
                    if (monitor.TryGetProperty("focused", out var f) && f.ValueKind == JsonValueKind.True)
                    {
                        chosen = monitor;
                        break;
                    }
                }

                if (chosen is { } m && m.TryGetProperty("scale", out var s) &&
                    s.ValueKind == JsonValueKind.Number)
                {
                    double scale = s.GetDouble();
                    string name = m.TryGetProperty("name", out var n) ? n.GetString() ?? "?" : "?";
                    if (scale > 0) return (scale, $"Hyprland monitor {name} (scale {scale:0.##})", false);
                }
            }
            catch { /* unparseable: fall through to the next detector */ }

            return (0, "", false);
        }

        /// <summary>
        /// GNOME/Mutter keeps an integer surface scale in <c>scaling-factor</c> (0 meaning "let the
        /// shell decide") and a separate font-only multiplier in <c>text-scaling-factor</c>. Only the
        /// former changes layout geometry, so that is the one mapped onto the Avalonia scale.
        /// </summary>
        private static (double Scale, string Source) DetectGnome()
        {
            string desktop = Environment.GetEnvironmentVariable("XDG_CURRENT_DESKTOP") ?? "";
            if (!desktop.Contains("gnome", StringComparison.OrdinalIgnoreCase) &&
                !desktop.Contains("unity", StringComparison.OrdinalIgnoreCase))
                return (0, "");

            string? raw = RunCommand("gsettings", "get org.gnome.desktop.interface scaling-factor");
            if (raw == null) return (0, "");

            // Printed as "uint32 2"; take the last whitespace-separated token.
            string token = raw.Trim().Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries).LastOrDefault() ?? "";
            if (TryParseScale(token, out var scale) && scale >= 1)
                return (scale, $"GNOME scaling-factor ({scale:0.##})");

            return (0, "");
        }

        /// <summary>Generic wlroots fallback for compositors that ship wlr-randr (Sway, river, Wayfire).</summary>
        private static (double Scale, string Source) DetectWlrRandr()
        {
            string? json = RunCommand("wlr-randr", "--json");
            if (json == null) return (0, "");

            try
            {
                using var doc = JsonDocument.Parse(json);
                if (doc.RootElement.ValueKind != JsonValueKind.Array) return (0, "");

                foreach (var output in doc.RootElement.EnumerateArray())
                {
                    if (output.TryGetProperty("enabled", out var en) && en.ValueKind == JsonValueKind.False)
                        continue;
                    if (output.TryGetProperty("scale", out var s) && s.ValueKind == JsonValueKind.Number)
                    {
                        double scale = s.GetDouble();
                        string name = output.TryGetProperty("name", out var n) ? n.GetString() ?? "?" : "?";
                        if (scale > 0) return (scale, $"wlr-randr output {name} (scale {scale:0.##})");
                    }
                }
            }
            catch { /* unparseable: give up on this detector */ }

            return (0, "");
        }

        /// <summary>
        /// GDK_SCALE is an integer surface scale; GDK_DPI_SCALE is a font-only multiplier that is
        /// conventionally set to 1/GDK_SCALE to undo it for text. Multiplying them gives the effective
        /// scale the GTK apps on this desktop are rendering at.
        /// </summary>
        private static (double Scale, string Source) DetectGdk()
        {
            if (!TryParseScale(Environment.GetEnvironmentVariable("GDK_SCALE"), out var scale) || scale <= 0)
                return (0, "");

            if (TryParseScale(Environment.GetEnvironmentVariable("GDK_DPI_SCALE"), out var dpiScale) &&
                dpiScale > 0)
                scale *= dpiScale;

            return (scale, $"GDK_SCALE environment variable ({scale:0.##})");
        }

        // ---------------------------------------------------------------- helpers

        /// <summary>Parses a scale, accepting both "1.6" and locales that would write "1,6".</summary>
        public static bool TryParseScale(string? text, out double value)
        {
            value = 0;
            if (string.IsNullOrWhiteSpace(text)) return false;
            text = text.Trim();

            if (double.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out value))
                return true;
            return double.TryParse(text, NumberStyles.Float, CultureInfo.CurrentCulture, out value);
        }

        /// <summary>
        /// Runs a short-lived query command and returns its stdout, or null if it is missing, fails, or
        /// takes too long. stderr is drained asynchronously so a chatty child cannot fill its pipe and
        /// deadlock us; the timeout keeps a hung helper from stalling startup.
        /// </summary>
        private static string? RunCommand(string exe, string arguments, int timeoutMs = 800)
        {
            try
            {
                var psi = new ProcessStartInfo(exe, arguments)
                {
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    UseShellExecute = false,
                    CreateNoWindow = true,
                };

                using var process = Process.Start(psi);
                if (process == null) return null;

                process.ErrorDataReceived += (_, _) => { };
                process.BeginErrorReadLine();

                string output = process.StandardOutput.ReadToEnd();
                if (!process.WaitForExit(timeoutMs))
                {
                    try { process.Kill(entireProcessTree: true); } catch { }
                    return null;
                }

                return process.ExitCode == 0 ? output : null;
            }
            catch
            {
                // Command not installed, not executable, or blocked. Not an error worth surfacing.
                return null;
            }
        }
    }
}
