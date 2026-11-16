using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Xml.Linq;

namespace DirOpusReImagined
{
    public enum SyncJobMode
    {
        /// <summary>Copy what is new, newer or changed from the first folder to the second.</summary>
        OneWay,
        /// <summary>One way, and also remove what the second folder has that the first doesn't (to the trash where there is one).</summary>
        Mirror,
        /// <summary>Newer wins, both ways; nothing is removed.</summary>
        TwoWay,
    }

    /// <summary>A saved sync (right-click → File Tools → Saved Sync Jobs): two folders, how, and when.</summary>
    public sealed class SyncJob
    {
        public string Name { get; set; } = "New sync job";
        public string From { get; set; } = "";
        public string To { get; set; } = "";
        public SyncJobMode Mode { get; set; } = SyncJobMode.OneWay;
        /// <summary>Compare contents (checksums) rather than size and date.</summary>
        public bool Content { get; set; }
        /// <summary>Run by itself every day at this time while DORI is open (and when it opens, if a run was missed); null for by hand only.</summary>
        public TimeSpan? DailyAt { get; set; }
        public DateTime? LastRun { get; set; }

        public SyncJob Clone() => (SyncJob)MemberwiseClone();

        public string Summary()
        {
            string how = Mode switch
            {
                SyncJobMode.Mirror => "mirror",
                SyncJobMode.TwoWay => "two-way",
                _ => "one way",
            };
            string arrow = Mode == SyncJobMode.TwoWay ? "⇄" : "→";
            string when = DailyAt is { } t ? $", daily at {t:hh\\:mm}" : "";
            return $"{From} {arrow} {To} ({how}{(Content ? ", by contents" : "")}{when})";
        }

        public string? Problem()
        {
            if (string.IsNullOrWhiteSpace(From) || string.IsNullOrWhiteSpace(To)) return "Choose both folders.";
            if (string.Equals(From.TrimEnd('/', '\\'), To.TrimEnd('/', '\\'), StringComparison.Ordinal)) return "The two folders must be different.";
            return null;
        }

        /// <summary>
        /// Whether a daily job should run now: its time today has passed and it hasn't run since then
        /// (so a run missed while DORI was closed happens when it next opens, once).
        /// </summary>
        public bool IsDue(DateTime now)
        {
            if (DailyAt is not { } at) return false;
            DateTime latest = now.Date + at;
            if (latest > now) latest = latest.AddDays(-1);
            return LastRun == null || LastRun < latest;
        }
    }

    /// <summary>The jobs, in <c>SyncJobs.xml</c> beside Configuration.xml.</summary>
    public static class SyncJobStore
    {
        public const string FileName = "SyncJobs.xml";

        public static string DefaultFile() => Path.Combine(Path.GetDirectoryName(ConfigFile.Resolve()) ?? ".", FileName);

        public static List<SyncJob> Load(string? file = null)
        {
            file ??= DefaultFile();
            var jobs = new List<SyncJob>();
            try
            {
                if (!File.Exists(file)) return jobs;
                foreach (var e in XDocument.Load(file).Root?.Elements("Job") ?? Enumerable.Empty<XElement>())
                {
                    var j = new SyncJob
                    {
                        Name = (string?)e.Attribute("name") ?? "Sync job",
                        From = (string?)e.Attribute("from") ?? "",
                        To = (string?)e.Attribute("to") ?? "",
                        Content = (bool?)e.Attribute("content") ?? false,
                    };
                    if (Enum.TryParse((string?)e.Attribute("mode"), true, out SyncJobMode m)) j.Mode = m;
                    if (TimeSpan.TryParse((string?)e.Attribute("dailyAt"), CultureInfo.InvariantCulture, out var t)) j.DailyAt = t;
                    if (DateTime.TryParse((string?)e.Attribute("lastRun"), CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind, out var lr)) j.LastRun = lr;
                    jobs.Add(j);
                }
            }
            catch { }
            return jobs;
        }

        public static void Save(IEnumerable<SyncJob> jobs, string? file = null)
        {
            file ??= DefaultFile();
            var root = new XElement("SyncJobs", jobs.Select(j =>
            {
                var e = new XElement("Job",
                    new XAttribute("name", j.Name), new XAttribute("from", j.From), new XAttribute("to", j.To),
                    new XAttribute("mode", j.Mode), new XAttribute("content", j.Content));
                if (j.DailyAt is { } t) e.Add(new XAttribute("dailyAt", t.ToString(@"hh\:mm", CultureInfo.InvariantCulture)));
                if (j.LastRun is { } lr) e.Add(new XAttribute("lastRun", lr.ToString("o", CultureInfo.InvariantCulture)));
                return e;
            }));
            Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(file))!);
            string temp = file + ".tmp";
            new XDocument(root).Save(temp);
            File.Move(temp, file, overwrite: true);
        }
    }
}
