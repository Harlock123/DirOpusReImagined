using System;
using System.Net.Http;
using System.Reflection;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

namespace DirOpusReImagined
{
    /// <summary>The newest release on GitHub.</summary>
    public sealed record ReleaseInfo(Version Version, string Tag, string Name, string Url, string Notes, DateTime? Published);

    /// <summary>
    /// Checks GitHub for a newer DORI. Nothing is sent but an ordinary request for the latest
    /// release (no user details), and only when the user asks, or once a day at start-up when they
    /// have turned that on (it is off until then).
    /// </summary>
    public static class UpdateCheck
    {
        public const string Repository = "Harlock123/DirOpusReImagined";
        public const string ReleasesPage = "https://github.com/" + Repository + "/releases";
        private const string LatestApi = "https://api.github.com/repos/" + Repository + "/releases/latest";
        public const string AutoSetting = "CheckForUpdates";
        public const string LastCheckSetting = "LastUpdateCheck";

        /// <summary>The running version (AssemblyVersion, e.g. 0.1.36.0).</summary>
        public static Version Current => Assembly.GetExecutingAssembly().GetName().Version ?? new Version(0, 0);

        /// <summary>"v0.1.36" or "0.1.36.0" → 0.1.36.0; null when it isn't a version.</summary>
        public static Version? ParseTag(string? tag)
        {
            if (string.IsNullOrWhiteSpace(tag)) return null;
            string t = tag.Trim().TrimStart('v', 'V');
            int dash = t.IndexOfAny(new[] { '-', '+', ' ' });
            if (dash > 0) t = t[..dash];
            if (!Version.TryParse(t, out var v)) return null;
            return new Version(v.Major, v.Minor, Math.Max(0, v.Build), Math.Max(0, v.Revision));
        }

        public static bool IsNewer(Version latest, Version current)
            => Normalise(latest).CompareTo(Normalise(current)) > 0;

        private static Version Normalise(Version v) => new(v.Major, v.Minor, Math.Max(0, v.Build), Math.Max(0, v.Revision));

        /// <summary>Reads GitHub's answer for the latest release.</summary>
        public static ReleaseInfo? ParseRelease(string json)
        {
            using var doc = JsonDocument.Parse(json);
            var root = doc.RootElement;
            string tag = root.TryGetProperty("tag_name", out var t) ? t.GetString() ?? "" : "";
            var version = ParseTag(tag);
            if (version == null) return null;
            string name = root.TryGetProperty("name", out var n) ? n.GetString() ?? tag : tag;
            string url = root.TryGetProperty("html_url", out var u) ? u.GetString() ?? ReleasesPage : ReleasesPage;
            string notes = root.TryGetProperty("body", out var b) ? b.GetString() ?? "" : "";
            DateTime? published = root.TryGetProperty("published_at", out var p) && p.ValueKind == JsonValueKind.String
                                  && DateTime.TryParse(p.GetString(), out var when) ? when : null;
            return new ReleaseInfo(version, tag, name, url, notes, published);
        }

        public static async Task<ReleaseInfo?> GetLatestAsync(CancellationToken ct = default)
        {
            using var http = new HttpClient { Timeout = TimeSpan.FromSeconds(15) };
            http.DefaultRequestHeaders.UserAgent.ParseAdd("DORI-update-check");
            http.DefaultRequestHeaders.Accept.ParseAdd("application/vnd.github+json");
            string json = await http.GetStringAsync(LatestApi, ct).ConfigureAwait(false);
            return ParseRelease(json);
        }

        public static bool AutoEnabled
        {
            get => string.Equals(ConfigSettings.Load(AutoSetting), "true", StringComparison.OrdinalIgnoreCase);
            set => ConfigSettings.Save(AutoSetting, value ? "true" : "false");
        }

        /// <summary>Whether the once-a-day check at start-up is due.</summary>
        public static bool DueToday(DateTime now)
        {
            if (!AutoEnabled) return false;
            string? last = ConfigSettings.Load(LastCheckSetting);
            return !DateTime.TryParse(last, System.Globalization.CultureInfo.InvariantCulture,
                                      System.Globalization.DateTimeStyles.RoundtripKind, out var when)
                   || now - when >= TimeSpan.FromDays(1);
        }

        public static void MarkChecked(DateTime now)
            => ConfigSettings.Save(LastCheckSetting, now.ToString("o", System.Globalization.CultureInfo.InvariantCulture));
    }
}
