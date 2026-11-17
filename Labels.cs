using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Xml.Linq;

namespace DirOpusReImagined
{
    /// <summary>The colours a file or folder can be labelled with.</summary>
    public enum LabelColour { None, Red, Orange, Yellow, Green, Blue, Purple, Grey }

    /// <summary>One item's colour label and tags.</summary>
    public sealed record FileLabel(LabelColour Colour, IReadOnlyList<string> Tags)
    {
        public static readonly FileLabel Empty = new(LabelColour.None, Array.Empty<string>());

        public bool IsEmpty => Colour == LabelColour.None && Tags.Count == 0;

        /// <summary>What the Tags column shows: "Red · work, todo", "work", "Green".</summary>
        public string Display
        {
            get
            {
                string tags = string.Join(", ", Tags);
                if (Colour == LabelColour.None) return tags;
                return tags.Length == 0 ? Colour.ToString() : $"{Colour} · {tags}";
            }
        }

        /// <summary>Whether the colour or a tag is <paramref name="word"/> (ignoring case).</summary>
        public bool Has(string word)
            => (Colour != LabelColour.None && Colour.ToString().Equals(word, StringComparison.OrdinalIgnoreCase))
               || Tags.Any(t => t.Equals(word, StringComparison.OrdinalIgnoreCase));
    }

    /// <summary>
    /// Colour labels and tags for local files and folders, kept by full path in <c>Labels.xml</c>
    /// beside Configuration.xml. DORI moves an item's label when DORI renames or moves the item;
    /// changes made by other programs leave the label behind on the old path (it is then dropped
    /// the next time the store is cleaned).
    /// </summary>
    public sealed class LabelStore
    {
        public const string FileName = "Labels.xml";

        private readonly string? _file;
        private readonly Dictionary<string, FileLabel> _labels;
        private static readonly StringComparer PathComparer =
            OperatingSystem.IsLinux() ? StringComparer.Ordinal : StringComparer.OrdinalIgnoreCase;

        /// <summary>The store the app uses, loaded on first use.</summary>
        public static LabelStore Current
        {
            get => _current ??= Load(DefaultFile());
            set => _current = value;
        }
        private static LabelStore? _current;

        public LabelStore(string? file)
        {
            _file = file;
            _labels = new Dictionary<string, FileLabel>(PathComparer);
        }

        /// <summary>Raised after any change, so panels showing labels can redraw.</summary>
        public event Action? Changed;

        public int Count => _labels.Count;

        private static string DefaultFile()
        {
            string config = ConfigFile.Resolve();
            return Path.Combine(Path.GetDirectoryName(config) ?? ".", FileName);
        }

        public static LabelStore Load(string file)
        {
            var store = new LabelStore(file);
            try
            {
                if (!File.Exists(file)) return store;
                foreach (var e in XDocument.Load(file).Root?.Elements("Item") ?? Enumerable.Empty<XElement>())
                {
                    string? path = (string?)e.Attribute("path");
                    if (string.IsNullOrEmpty(path)) continue;
                    Enum.TryParse((string?)e.Attribute("colour") ?? "", true, out LabelColour colour);
                    var label = new FileLabel(colour, ParseTags((string?)e.Attribute("tags")));
                    if (!label.IsEmpty) store._labels[Normalise(path)] = label;
                }
            }
            catch { /* an unreadable file means no labels, not a crash */ }
            return store;
        }

        public void Save()
        {
            if (_file == null) return;
            var root = new XElement("Labels",
                _labels.OrderBy(kv => kv.Key, StringComparer.Ordinal).Select(kv =>
                {
                    var e = new XElement("Item", new XAttribute("path", kv.Key));
                    if (kv.Value.Colour != LabelColour.None) e.Add(new XAttribute("colour", kv.Value.Colour));
                    if (kv.Value.Tags.Count > 0) e.Add(new XAttribute("tags", string.Join(",", kv.Value.Tags)));
                    return e;
                }));
            Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(_file))!);
            string temp = _file + ".tmp";
            new XDocument(root).Save(temp);
            File.Move(temp, _file, overwrite: true);
        }

        /// <summary>"work, to do ,urgent" → ["work", "to do", "urgent"]; commas separate, blanks and repeats are dropped.</summary>
        public static List<string> ParseTags(string? text)
            => (text ?? "").Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                           .Distinct(StringComparer.OrdinalIgnoreCase)
                           .ToList();

        private static string Normalise(string path)
        {
            string p = path.Length > 1 ? path.TrimEnd('/', '\\') : path;
            return p.Length == 0 ? path : p;
        }

        /// <summary>
        /// The label of <paramref name="path"/>: the one kept on the file itself when
        /// <see cref="SystemReader"/> finds one (so changes made in other programs show), else DORI's own.
        /// </summary>
        public FileLabel Get(string path)
        {
            if (SystemReader?.Invoke(path) is { } onFile) return onFile;
            return GetStored(path);
        }

        /// <summary>The label in DORI's own store only.</summary>
        public FileLabel GetStored(string path) => _labels.TryGetValue(Normalise(path), out var l) ? l : FileLabel.Empty;

        /// <summary>Reads the label kept on a file by the system (<see cref="SystemTags.Read"/>), or null for none. Null when not used.</summary>
        public Func<string, FileLabel?>? SystemReader { get; set; }

        /// <summary>Whether labels can come from anywhere: the store has some, or they are read from the files.</summary>
        public bool AnyLabels => _labels.Count > 0 || SystemReader != null;

        /// <summary>Raised with the paths whose labels DORI just set, so they can be written onto the files too.</summary>
        public event Action<IReadOnlyCollection<string>>? LabelsSet;

        /// <summary>The paths DORI's own store has labels for.</summary>
        public List<string> StoredPaths() => _labels.Keys.ToList();

        /// <summary>Every tag in use, sorted, for menus.</summary>
        public List<string> AllTags()
            => _labels.Values.SelectMany(l => l.Tags).Distinct(StringComparer.OrdinalIgnoreCase)
                      .OrderBy(t => t, StringComparer.OrdinalIgnoreCase).ToList();

        private void Put(string path, FileLabel label)
        {
            string key = Normalise(path);
            if (label.IsEmpty) _labels.Remove(key); else _labels[key] = label;
        }

        public void SetColour(IEnumerable<string> paths, LabelColour colour)
        {
            var list = paths.ToList();
            foreach (var p in list) Put(p, Get(p) with { Colour = colour });
            LabelsSet?.Invoke(list);
            Changed?.Invoke();
        }

        /// <summary>Replaces the tags of every path.</summary>
        public void SetTags(IEnumerable<string> paths, IEnumerable<string> tags)
        {
            var list = tags.ToList();
            var targets = paths.ToList();
            foreach (var p in targets) Put(p, Get(p) with { Tags = list });
            LabelsSet?.Invoke(targets);
            Changed?.Invoke();
        }

        /// <summary>Adds a tag to every path that doesn't have it, or removes it from all of them.</summary>
        public void ToggleTag(IReadOnlyCollection<string> paths, string tag)
        {
            bool allHave = paths.Count > 0 && paths.All(p => Get(p).Tags.Contains(tag, StringComparer.OrdinalIgnoreCase));
            foreach (var p in paths)
            {
                var tags = Get(p).Tags.Where(t => !t.Equals(tag, StringComparison.OrdinalIgnoreCase)).ToList();
                if (!allHave) tags.Add(tag);
                Put(p, Get(p) with { Tags = tags });
            }
            LabelsSet?.Invoke(paths);
            Changed?.Invoke();
        }

        public void Clear(IEnumerable<string> paths)
        {
            var list = paths.ToList();
            foreach (var p in list) _labels.Remove(Normalise(p));
            LabelsSet?.Invoke(list);
            Changed?.Invoke();
        }

        /// <summary>
        /// Moves the label of <paramref name="from"/> to <paramref name="to"/>, and for a folder the
        /// labels of everything inside it. Returns whether anything moved.
        /// </summary>
        public bool Move(string from, string to)
        {
            string a = Normalise(from), b = Normalise(to);
            if (PathComparer.Equals(a, b)) return false;
            var moved = new List<(string Old, string New, FileLabel Label)>();
            foreach (var (key, label) in _labels)
            {
                if (PathComparer.Equals(key, a)) moved.Add((key, b, label));
                else if (IsInside(key, a)) moved.Add((key, b + key.Substring(a.Length), label));
            }
            if (moved.Count == 0) return false;
            foreach (var m in moved) _labels.Remove(m.Old);
            foreach (var m in moved) _labels[m.New] = m.Label;
            Changed?.Invoke();
            return true;
        }

        /// <summary>Drops the labels of <paramref name="path"/> and everything inside it (after a delete).</summary>
        public bool Forget(string path)
        {
            string a = Normalise(path);
            var gone = _labels.Keys.Where(k => PathComparer.Equals(k, a) || IsInside(k, a)).ToList();
            foreach (var k in gone) _labels.Remove(k);
            if (gone.Count > 0) Changed?.Invoke();
            return gone.Count > 0;
        }

        /// <summary>Removes labels whose files no longer exist. Returns how many went.</summary>
        public int Prune(Func<string, bool> exists)
        {
            var gone = _labels.Keys.Where(k => !exists(k)).ToList();
            foreach (var k in gone) _labels.Remove(k);
            if (gone.Count > 0) Changed?.Invoke();
            return gone.Count;
        }

        private static bool IsInside(string key, string folder)
            => key.Length > folder.Length + 1
               && key.StartsWith(folder, OperatingSystem.IsLinux() ? StringComparison.Ordinal : StringComparison.OrdinalIgnoreCase)
               && (key[folder.Length] is '/' or '\\' || folder.EndsWith('/') || folder.EndsWith('\\'));
    }
}
