using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Text;
using System.Threading;
using DirOpusReImagined.FileSystem;

namespace DirOpusReImagined
{
    /// <summary>One file or folder in a catalogue: its path below the catalogue's root ("/"-separated), size and date.</summary>
    public sealed record CatalogueEntry(string RelPath, bool IsDirectory, long Size, DateTime Modified)
    {
        public string Name => RelPath.Contains('/') ? RelPath[(RelPath.LastIndexOf('/') + 1)..] : RelPath;
        public string Parent => RelPath.Contains('/') ? RelPath[..RelPath.LastIndexOf('/')] : "";
    }

    /// <summary>
    /// A disk catalogue (right-click → File Tools → Disk Catalogues): a saved record of every file
    /// and folder under a folder — an external drive, a NAS share, a cloud remote — that can be
    /// browsed and searched while the drive isn't there, and compared with the drive later to see
    /// what changed. Kept as a small gzip'd text file in the <c>Catalogues</c> folder beside the config.
    /// </summary>
    public sealed class Catalogue
    {
        public const string FolderName = "Catalogues";
        public const string Extension = ".dcat";
        private const string Magic = "# DORI catalogue 1";

        public string Name { get; set; } = "";
        public string Root { get; set; } = "";
        public DateTime Made { get; set; }
        public List<CatalogueEntry> Entries { get; } = new();
        /// <summary>Where it is saved, once it has been.</summary>
        public string? FilePath { get; set; }

        public int FileCount => Entries.Count(e => !e.IsDirectory);
        public int FolderCount => Entries.Count(e => e.IsDirectory);
        public long TotalBytes => Entries.Where(e => !e.IsDirectory).Sum(e => e.Size);

        private Dictionary<string, List<CatalogueEntry>>? _children;

        /// <summary>What is directly inside <paramref name="relFolder"/> ("" for the root): folders first, then files, by name.</summary>
        public IReadOnlyList<CatalogueEntry> Children(string relFolder)
        {
            _children ??= Entries.GroupBy(e => e.Parent, StringComparer.Ordinal)
                                 .ToDictionary(g => g.Key, g => g.OrderBy(e => !e.IsDirectory).ThenBy(e => e.Name, NaturalComparer.Instance).ToList(),
                                               StringComparer.Ordinal);
            return _children.TryGetValue(relFolder, out var list) ? list : Array.Empty<CatalogueEntry>();
        }

        /// <summary>The bytes of every file below <paramref name="relFolder"/>.</summary>
        public long BytesBelow(string relFolder)
            => relFolder.Length == 0 ? TotalBytes
               : Entries.Where(e => !e.IsDirectory && e.RelPath.StartsWith(relFolder + "/", StringComparison.Ordinal)).Sum(e => e.Size);

        /// <summary>Entries whose names match the filter text (<c>*.mp4;holiday</c>, as the filter box), up to <paramref name="limit"/>.</summary>
        public IEnumerable<CatalogueEntry> Search(string filterText, int limit = 5000)
        {
            var match = SavedFilters.Matcher(filterText);
            return Entries.Where(e => match(e.Name)).Take(limit);
        }

        /// <summary>The full path an entry had on the drive (root joined with the entry's path).</summary>
        public string FullPathOf(CatalogueEntry e) => JoinRoot(Root, e.RelPath);

        public static string JoinRoot(string root, string rel)
        {
            if (rel.Length == 0) return root;
            if (root.Contains("://")) return root.TrimEnd('/') + "/" + rel;
            char sep = Path.DirectorySeparatorChar;
            return root.TrimEnd('/', '\\') + sep + rel.Replace('/', sep);
        }

        // ---- Making one ----

        /// <summary>
        /// Walks everything under <paramref name="root"/> through its provider (so cloud remotes work
        /// too), never into linked folders. <paramref name="progress"/> gets the count so far.
        /// Folders that can't be read are left out.
        /// </summary>
        public static Catalogue Scan(string name, string root, IProgress<int>? progress = null, CancellationToken ct = default)
        {
            var provider = ProviderRegistry.For(root);
            var cat = new Catalogue { Name = name, Root = root, Made = DateTime.Now };
            var pending = new Stack<(string Path, string Rel)>();
            pending.Push((root, ""));
            int lastReport = 0;
            while (pending.Count > 0)
            {
                ct.ThrowIfCancellationRequested();
                var (dir, rel) = pending.Pop();
                List<FileEntry> dirs, files;
                try
                {
                    dirs = provider.EnumerateDirectories(dir).ToList();
                    files = provider.EnumerateFiles(dir).ToList();
                }
                catch { continue; }

                foreach (var f in files)
                    cat.Entries.Add(new CatalogueEntry(Join(rel, f.Name), false, Math.Max(0, f.Size), f.LastModified));
                foreach (var d in dirs)
                {
                    string r = Join(rel, d.Name);
                    cat.Entries.Add(new CatalogueEntry(r, true, 0, d.LastModified));
                    if (!d.IsLink) pending.Push((d.Path, r));
                }
                if (cat.Entries.Count - lastReport >= 500)
                {
                    lastReport = cat.Entries.Count;
                    progress?.Report(lastReport);
                }
            }
            progress?.Report(cat.Entries.Count);
            return cat;
        }

        private static string Join(string rel, string name) => rel.Length == 0 ? name : rel + "/" + name;

        // ---- Saving and loading ----

        public static string DefaultFolder()
            => Path.Combine(Path.GetDirectoryName(ConfigFile.Resolve()) ?? ".", FolderName);

        public void Save(string? folder = null)
        {
            folder ??= DefaultFolder();
            Directory.CreateDirectory(folder);
            string file = FilePath ?? Path.Combine(folder, SafeFileName(Name) + Extension);
            string temp = file + ".tmp";
            using (var fs = File.Create(temp))
            using (var gz = new GZipStream(fs, CompressionLevel.SmallestSize))
            using (var w = new StreamWriter(gz, new UTF8Encoding(false)))
            {
                w.Write(Magic + "\n");
                w.Write("name\t" + Clean(Name) + "\n");
                w.Write("root\t" + Clean(Root) + "\n");
                w.Write("made\t" + Made.ToUniversalTime().ToString("o", CultureInfo.InvariantCulture) + "\n");
                foreach (var e in Entries)
                {
                    long ticks = e.Modified.ToUniversalTime().Ticks;
                    w.Write(e.IsDirectory
                        ? $"D\t{Clean(e.RelPath)}\t{ticks}\n"
                        : $"F\t{Clean(e.RelPath)}\t{e.Size}\t{ticks}\n");
                }
            }
            File.Move(temp, file, overwrite: true);
            FilePath = file;
        }

        public static Catalogue Load(string file)
        {
            using var fs = File.OpenRead(file);
            using var gz = new GZipStream(fs, CompressionMode.Decompress);
            using var r = new StreamReader(gz, Encoding.UTF8);
            if (r.ReadLine() != Magic) throw new InvalidDataException($"{Path.GetFileName(file)} isn't a DORI catalogue.");
            var cat = new Catalogue { FilePath = file, Name = Path.GetFileNameWithoutExtension(file) };
            string? line;
            while ((line = r.ReadLine()) != null)
            {
                var p = line.Split('\t');
                switch (p[0])
                {
                    case "name" when p.Length > 1: cat.Name = p[1]; break;
                    case "root" when p.Length > 1: cat.Root = p[1]; break;
                    case "made" when p.Length > 1:
                        if (DateTime.TryParse(p[1], CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind, out var made)) cat.Made = made.ToLocalTime();
                        break;
                    case "D" when p.Length >= 3:
                        cat.Entries.Add(new CatalogueEntry(p[1], true, 0, FromTicks(p[2])));
                        break;
                    case "F" when p.Length >= 4:
                        long.TryParse(p[2], NumberStyles.Integer, CultureInfo.InvariantCulture, out long size);
                        cat.Entries.Add(new CatalogueEntry(p[1], false, size, FromTicks(p[3])));
                        break;
                }
            }
            return cat;
        }

        /// <summary>Every catalogue in the folder, by name; unreadable files are skipped.</summary>
        public static List<Catalogue> LoadAll(string? folder = null)
        {
            folder ??= DefaultFolder();
            var list = new List<Catalogue>();
            if (!Directory.Exists(folder)) return list;
            foreach (var f in Directory.EnumerateFiles(folder, "*" + Extension))
            {
                try { list.Add(Load(f)); }
                catch (Exception ex) { CrashLog.Write("Loading catalogue " + f, ex); }
            }
            return list.OrderBy(c => c.Name, NaturalComparer.Instance).ToList();
        }

        private static DateTime FromTicks(string s)
            => long.TryParse(s, NumberStyles.Integer, CultureInfo.InvariantCulture, out long t) && t >= DateTime.MinValue.Ticks && t <= DateTime.MaxValue.Ticks
                ? new DateTime(t, DateTimeKind.Utc).ToLocalTime()
                : DateTime.MinValue;

        /// <summary>Tabs and line breaks can't be stored in a line of the file; they become spaces.</summary>
        private static string Clean(string s) => s.Replace('\t', ' ').Replace('\n', ' ').Replace('\r', ' ');

        public static string SafeFileName(string name)
        {
            var bad = Path.GetInvalidFileNameChars().Concat(new[] { '/', '\\', ':', '*', '?', '"', '<', '>', '|' }).ToHashSet();
            string s = new string(name.Select(c => bad.Contains(c) ? '_' : c).ToArray()).Trim().TrimEnd('.');
            return s.Length == 0 ? "catalogue" : s;
        }
    }

    /// <summary>What changed between an older catalogue and a newer one of the same place.</summary>
    public sealed record CatalogueChanges(List<CatalogueEntry> Added, List<CatalogueEntry> Removed, List<CatalogueEntry> Changed)
    {
        public bool None => Added.Count == 0 && Removed.Count == 0 && Changed.Count == 0;

        /// <summary>
        /// Compares by path. A file whose size or date (to the second) differs counts as changed;
        /// folders only count as added or removed.
        /// </summary>
        public static CatalogueChanges Between(Catalogue older, Catalogue newer)
        {
            var old = older.Entries.ToDictionary(e => e.RelPath, StringComparer.Ordinal);
            var now = newer.Entries.ToDictionary(e => e.RelPath, StringComparer.Ordinal);
            var added = newer.Entries.Where(e => !old.ContainsKey(e.RelPath)).ToList();
            var removed = older.Entries.Where(e => !now.ContainsKey(e.RelPath)).ToList();
            var changed = newer.Entries.Where(e => !e.IsDirectory && old.TryGetValue(e.RelPath, out var o) && !o.IsDirectory &&
                                                   (o.Size != e.Size || Math.Abs((o.Modified - e.Modified).TotalSeconds) >= 1)).ToList();
            return new CatalogueChanges(added, removed, changed);
        }

        /// <summary>A readable report, a line per item (at most <paramref name="max"/> per kind).</summary>
        public string Report(int max = 200)
        {
            var sb = new StringBuilder();
            void Section(string title, List<CatalogueEntry> items, string mark)
            {
                if (items.Count == 0) return;
                sb.Append(title).Append(" (").Append(items.Count.ToString("N0", CultureInfo.CurrentCulture)).Append(")\n");
                foreach (var e in items.OrderBy(e => e.RelPath, NaturalComparer.Instance).Take(max))
                    sb.Append("  ").Append(mark).Append(' ').Append(e.RelPath).Append(e.IsDirectory ? "/" : "").Append('\n');
                if (items.Count > max) sb.Append("  … and ").Append(items.Count - max).Append(" more\n");
                sb.Append('\n');
            }
            Section("New since last time", Added, "+");
            Section("Gone since last time", Removed, "−");
            Section("Changed (size or date)", Changed, "~");
            return sb.Length == 0 ? "Nothing has changed." : sb.ToString().TrimEnd();
        }
    }
}
