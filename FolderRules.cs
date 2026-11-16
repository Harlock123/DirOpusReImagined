using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Xml.Linq;

namespace DirOpusReImagined
{
    /// <summary>What a folder rule does to each file it matches.</summary>
    public enum RuleAction
    {
        MoveTo,
        CopyTo,
        Trash,
        SetColour,
        AddTag,
    }

    /// <summary>
    /// One folder rule (right-click → File Tools → Folder Rules): for files in <see cref="Folder"/>
    /// (not its subfolders) whose names match <see cref="Match"/> and that are older than
    /// <see cref="OlderThanDays"/>, do <see cref="Action"/>. A watching rule runs by itself when
    /// files arrive, and once an hour for its age test; any rule can be run by hand.
    /// </summary>
    public sealed class FolderRule
    {
        public string Name { get; set; } = "New rule";
        public string Folder { get; set; } = "";
        /// <summary>The filter box's patterns: <c>*.pdf;*.docx</c>, plain text to contain, or <c>tag:name</c>.</summary>
        public string Match { get; set; } = "*";
        /// <summary>Only files last changed more than this many days ago; 0 for any age.</summary>
        public double OlderThanDays { get; set; }
        public RuleAction Action { get; set; } = RuleAction.MoveTo;
        /// <summary>The folder for Move and Copy, the colour for Set Colour, the tag for Add Tag.</summary>
        public string Target { get; set; } = "";
        public bool Enabled { get; set; } = true;
        public bool Watch { get; set; }

        public FolderRule Clone() => (FolderRule)MemberwiseClone();

        /// <summary>"Move *.pdf older than 30 days to ~/Documents" — the rule in a line, for lists and logs.</summary>
        public string Summary()
        {
            string what = string.IsNullOrWhiteSpace(Match) || Match.Trim() == "*" ? "every file" : Match.Trim();
            string age = OlderThanDays > 0 ? $" older than {OlderThanDays.ToString("0.##", CultureInfo.CurrentCulture)} days" : "";
            string act = Action switch
            {
                RuleAction.MoveTo => $"move to {Target}",
                RuleAction.CopyTo => $"copy to {Target}",
                RuleAction.Trash => "move to the trash",
                RuleAction.SetColour => $"label {Target}",
                RuleAction.AddTag => $"tag \"{Target}\"",
                _ => Action.ToString(),
            };
            return $"{what}{age}: {act}";
        }

        /// <summary>Why the rule can't run as it stands, or null when it can.</summary>
        public string? Problem()
        {
            if (string.IsNullOrWhiteSpace(Folder)) return "Choose the folder the rule looks in.";
            if (!Path.IsPathFullyQualified(Folder)) return "The folder must be a full path on this computer.";
            switch (Action)
            {
                case RuleAction.MoveTo or RuleAction.CopyTo:
                    if (string.IsNullOrWhiteSpace(Target) || !Path.IsPathFullyQualified(Target))
                        return "Give the full path of the folder to move or copy to.";
                    if (FolderRuleEngine.SamePath(Target, Folder))
                        return "The folder to move or copy to must be a different folder.";
                    break;
                case RuleAction.SetColour:
                    if (!Enum.TryParse(Target, true, out LabelColour c) || c == LabelColour.None)
                        return "Choose a colour: Red, Orange, Yellow, Green, Blue, Purple or Grey.";
                    break;
                case RuleAction.AddTag:
                    if (string.IsNullOrWhiteSpace(Target) || Target.Contains(','))
                        return "Give one tag to add (no commas).";
                    break;
            }
            return null;
        }
    }

    /// <summary>The rules, kept in <c>Rules.xml</c> beside Configuration.xml.</summary>
    public static class RuleStore
    {
        public const string FileName = "Rules.xml";

        public static string DefaultFile()
            => Path.Combine(Path.GetDirectoryName(ConfigFile.Resolve()) ?? ".", FileName);

        public static List<FolderRule> Load(string? file = null)
        {
            file ??= DefaultFile();
            var rules = new List<FolderRule>();
            try
            {
                if (!File.Exists(file)) return rules;
                foreach (var e in XDocument.Load(file).Root?.Elements("Rule") ?? Enumerable.Empty<XElement>())
                {
                    var r = new FolderRule
                    {
                        Name = (string?)e.Attribute("name") ?? "Rule",
                        Folder = (string?)e.Attribute("folder") ?? "",
                        Match = (string?)e.Attribute("match") ?? "*",
                        Target = (string?)e.Attribute("target") ?? "",
                        Enabled = (bool?)e.Attribute("enabled") ?? true,
                        Watch = (bool?)e.Attribute("watch") ?? false,
                    };
                    if (double.TryParse((string?)e.Attribute("olderThanDays"), NumberStyles.Float, CultureInfo.InvariantCulture, out var d)) r.OlderThanDays = Math.Max(0, d);
                    if (Enum.TryParse((string?)e.Attribute("action"), true, out RuleAction a)) r.Action = a;
                    rules.Add(r);
                }
            }
            catch { /* an unreadable file means no rules, not a crash */ }
            return rules;
        }

        public static void Save(IEnumerable<FolderRule> rules, string? file = null)
        {
            file ??= DefaultFile();
            var root = new XElement("Rules", rules.Select(r => new XElement("Rule",
                new XAttribute("name", r.Name),
                new XAttribute("folder", r.Folder),
                new XAttribute("match", r.Match),
                new XAttribute("olderThanDays", r.OlderThanDays.ToString(CultureInfo.InvariantCulture)),
                new XAttribute("action", r.Action),
                new XAttribute("target", r.Target),
                new XAttribute("enabled", r.Enabled),
                new XAttribute("watch", r.Watch))));
            Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(file))!);
            string temp = file + ".tmp";
            new XDocument(root).Save(temp);
            File.Move(temp, file, overwrite: true);
        }
    }

    /// <summary>A file a rule looks at: its full path, when it last changed, and its size.</summary>
    public readonly record struct RuleFile(string Path, DateTime LastWrite, long Length);

    /// <summary>Deciding which files a rule takes, and doing it to one file.</summary>
    public static class FolderRuleEngine
    {
        /// <summary>A file changed more recently than this may still be being written, so it waits for the next look.</summary>
        public static readonly TimeSpan SettleTime = TimeSpan.FromSeconds(5);

        private static readonly string[] UnfinishedEndings =
        {
            ".part", ".partial", ".crdownload", ".download", ".tmp", ".temp", ".!qb", ".opdownload", ".dori-part",
        };

        /// <summary>Downloads and copies still in progress, and editors' lock and swap files.</summary>
        public static bool LooksUnfinished(string name)
            => UnfinishedEndings.Any(e => name.EndsWith(e, StringComparison.OrdinalIgnoreCase))
               || name.StartsWith("~$", StringComparison.Ordinal)
               || name.StartsWith(".~lock.", StringComparison.Ordinal)
               || (name.StartsWith('.') && name.EndsWith(".swp", StringComparison.Ordinal));

        /// <summary>The files the rule takes now, in name order. <paramref name="label"/> gives each path's label (for tag: patterns and to skip files already labelled).</summary>
        public static List<string> Candidates(FolderRule rule, IEnumerable<RuleFile> files, DateTime now, Func<string, FileLabel> label)
        {
            var matches = SavedFilters.EntryMatcher(string.IsNullOrWhiteSpace(rule.Match) ? "*" : rule.Match);
            LabelColour colour = LabelColour.None;
            if (rule.Action == RuleAction.SetColour) Enum.TryParse(rule.Target, true, out colour);
            var result = new List<string>();
            foreach (var f in files.OrderBy(f => f.Path, NaturalComparer.Instance))
            {
                string name = System.IO.Path.GetFileName(f.Path);
                if (LooksUnfinished(name)) continue;
                if (now - f.LastWrite < SettleTime) continue;
                if (rule.OlderThanDays > 0 && now - f.LastWrite < TimeSpan.FromDays(rule.OlderThanDays)) continue;
                var l = label(f.Path);
                if (!matches(name, l.Has)) continue;
                // Labelling needs doing once; skip files that already have it.
                if (rule.Action == RuleAction.SetColour && l.Colour == colour) continue;
                if (rule.Action == RuleAction.AddTag && l.Tags.Contains(rule.Target.Trim(), StringComparer.OrdinalIgnoreCase)) continue;
                result.Add(f.Path);
            }
            return result;
        }

        /// <summary>The files directly in the rule's folder (links and hidden files included, folders not).</summary>
        public static List<RuleFile> FilesIn(string folder)
        {
            var list = new List<RuleFile>();
            try
            {
                foreach (var path in Directory.EnumerateFiles(folder))
                {
                    try
                    {
                        var fi = new FileInfo(path);
                        list.Add(new RuleFile(path, fi.LastWriteTime, fi.Length));
                    }
                    catch { }
                }
            }
            catch { }
            return list;
        }

        /// <summary>
        /// Does the rule to one file and says what happened ("moved to …", or null when nothing
        /// needed doing). Never overwrites: a move to a name in use gets " (2)" and so on; a copy
        /// whose same-size twin is already there is skipped, so running the rule again doesn't copy again.
        /// </summary>
        public static string? Apply(FolderRule rule, string path, LabelStore labels, Action<Action>? onLabels = null)
        {
            // Labels belong to the UI thread in the app; the caller says how to get there.
            onLabels ??= a => a();
            string name = System.IO.Path.GetFileName(path);
            switch (rule.Action)
            {
                case RuleAction.MoveTo:
                {
                    Directory.CreateDirectory(rule.Target);
                    string dst = FreeName(System.IO.Path.Combine(rule.Target, name));
                    File.Move(path, dst);
                    onLabels(() => labels.Move(path, dst));
                    return $"moved {name} to {dst}";
                }
                case RuleAction.CopyTo:
                {
                    Directory.CreateDirectory(rule.Target);
                    string dst = System.IO.Path.Combine(rule.Target, name);
                    if (File.Exists(dst) && new FileInfo(dst).Length == new FileInfo(path).Length) return null;
                    dst = FreeName(dst);
                    File.Copy(path, dst);
                    return $"copied {name} to {dst}";
                }
                case RuleAction.Trash:
                    TrashService.Trash(path);   // throws when it can't
                    return $"moved {name} to the trash";
                case RuleAction.SetColour:
                {
                    var colour = Enum.Parse<LabelColour>(rule.Target, true);
                    onLabels(() => labels.SetColour(new[] { path }, colour));
                    return $"labelled {name} {colour}";
                }
                case RuleAction.AddTag:
                {
                    string tag = rule.Target.Trim();
                    onLabels(() => labels.SetTags(new[] { path },
                        labels.Get(path).Tags.Append(tag).Distinct(StringComparer.OrdinalIgnoreCase).ToList()));
                    return $"tagged {name} \"{tag}\"";
                }
            }
            return null;
        }

        /// <summary><paramref name="path"/> if nothing is there, else "name (2).ext", "name (3).ext"…</summary>
        public static string FreeName(string path)
        {
            if (!File.Exists(path) && !Directory.Exists(path)) return path;
            string dir = System.IO.Path.GetDirectoryName(path) ?? "";
            string stem = System.IO.Path.GetFileNameWithoutExtension(path);
            string ext = System.IO.Path.GetExtension(path);
            for (int n = 2; ; n++)
            {
                string candidate = System.IO.Path.Combine(dir, $"{stem} ({n}){ext}");
                if (!File.Exists(candidate) && !Directory.Exists(candidate)) return candidate;
            }
        }

        public static bool SamePath(string a, string b)
            => string.Equals(System.IO.Path.GetFullPath(a).TrimEnd('/', '\\'), System.IO.Path.GetFullPath(b).TrimEnd('/', '\\'),
                             OperatingSystem.IsLinux() ? StringComparison.Ordinal : StringComparison.OrdinalIgnoreCase);
    }
}
