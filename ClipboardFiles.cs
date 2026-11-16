using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;

namespace DirOpusReImagined
{
    /// <summary>
    /// The clipboard formats file managers use to copy and cut files, so files copied in DORI can be
    /// pasted in Explorer, Finder, Files (Nautilus), Dolphin and others, and the other way round.
    /// Windows and macOS have native file lists (Avalonia's <c>DataFormats.Files</c>); Linux desktops
    /// use <c>text/uri-list</c>, with GNOME-style apps adding <c>x-special/gnome-copied-files</c>
    /// (which says copy or cut) and KDE adding <c>application/x-kde-cutselection</c>.
    /// </summary>
    public static class ClipboardFiles
    {
        public const string UriListFormat = "text/uri-list";
        public const string GnomeFormat = "x-special/gnome-copied-files";
        public const string KdeCutFormat = "application/x-kde-cutselection";
        /// <summary>Windows: 4 bytes, 2 meaning move (cut) and 1 or 5 meaning copy.</summary>
        public const string WindowsDropEffectFormat = "Preferred DropEffect";

        /// <summary>file:// URIs, one per line with CRLF endings, as RFC 2483 says.</summary>
        public static string UriList(IEnumerable<string> paths)
            => string.Concat(paths.Select(p => ToUri(p) + "\r\n"));

        /// <summary>"copy" or "cut", then one file:// URI per line (no line ending after the last).</summary>
        public static string GnomeCopiedFiles(IEnumerable<string> paths, bool cut)
            => (cut ? "cut" : "copy") + string.Concat(paths.Select(p => "\n" + ToUri(p)));

        public static string ToUri(string path) => new Uri(Path.GetFullPath(path)).AbsoluteUri;

        /// <summary>
        /// Reads paths from any of the text forms: the GNOME list (whose first line says copy or
        /// cut), a URI list (lines starting with # are comments), or plain full paths one per line.
        /// <paramref name="cut"/> is true or false when the text says, else null. Only file:// URIs
        /// and full paths are taken; anything else (web links, words) is left out.
        /// </summary>
        public static List<string> Parse(string? text, out bool? cut)
        {
            cut = null;
            var paths = new List<string>();
            if (string.IsNullOrWhiteSpace(text)) return paths;
            var lines = text.Replace("\r\n", "\n").Replace('\r', '\n').Split('\n')
                            .Select(l => l.Trim().TrimEnd('\0')).Where(l => l.Length > 0).ToList();
            if (lines.Count > 0 && lines[0] is "copy" or "cut")
            {
                cut = lines[0] == "cut";
                lines.RemoveAt(0);
            }
            foreach (var line in lines)
            {
                if (line.StartsWith('#')) continue;
                if (line.StartsWith("file:", StringComparison.OrdinalIgnoreCase))
                {
                    if (Uri.TryCreate(line, UriKind.Absolute, out var uri) && uri.IsFile && (uri.Host.Length == 0 || uri.Host == "localhost"))
                        paths.Add(uri.LocalPath);
                    else if (Uri.TryCreate(line, UriKind.Absolute, out var unc) && unc.IsUnc)
                        paths.Add(unc.LocalPath);
                }
                else if (Path.IsPathFullyQualified(line))
                    paths.Add(line);
            }
            return paths.Distinct(OperatingSystem.IsLinux() ? StringComparer.Ordinal : StringComparer.OrdinalIgnoreCase).ToList();
        }

        /// <summary>Text from clipboard data that may come back as a string, bytes (UTF-8, or UTF-16 on Windows) or a stream.</summary>
        public static string? AsText(object? data)
        {
            switch (data)
            {
                case null: return null;
                case string s: return s;
                case byte[] b: return DecodeBytes(b);
                case Stream st:
                    using (var ms = new MemoryStream()) { st.CopyTo(ms); return DecodeBytes(ms.ToArray()); }
                default: return data.ToString();
            }
        }

        private static string DecodeBytes(byte[] b)
        {
            // UTF-16 text has a zero in every other byte for the ASCII part of a path.
            bool utf16 = b.Length >= 4 && b[1] == 0 && b[3] == 0;
            return (utf16 ? Encoding.Unicode : Encoding.UTF8).GetString(b).TrimEnd('\0');
        }

        /// <summary>Whether Windows' "Preferred DropEffect" bytes (or a stream of them) mean cut.</summary>
        public static bool? DropEffectIsCut(object? data)
        {
            byte[]? b = data switch
            {
                byte[] bytes => bytes,
                Stream st => ReadAll(st),
                _ => null,
            };
            if (b == null || b.Length < 4) return null;
            int effect = BitConverter.ToInt32(b, 0);
            return (effect & 2) != 0 && (effect & 1) == 0;
        }

        private static byte[] ReadAll(Stream st)
        {
            using var ms = new MemoryStream();
            st.CopyTo(ms);
            return ms.ToArray();
        }
    }
}
