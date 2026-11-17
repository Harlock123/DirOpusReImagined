using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text;

namespace DirOpusReImagined
{
    /// <summary>
    /// Colour labels and tags kept on the files themselves, so other programs see them and DORI
    /// sees theirs:
    /// <list type="bullet">
    /// <item>Linux: the <c>user.xdg.tags</c> extended attribute (comma-separated), which Dolphin and
    /// KDE's Baloo use. Linux file managers have no colours, so a colour is written as a tag of its
    /// name ("Red").</item>
    /// <item>macOS: Finder tags, the <c>com.apple.metadata:_kMDItemUserTags</c> attribute, a binary
    /// property list of "Name\nColour" strings; DORI's colours map to Finder's seven.</item>
    /// <item>Windows has no tags for every kind of file, so nothing is written there.</item>
    /// </list>
    /// Folders on drives without extended attributes (FAT USB sticks, some network shares) just
    /// keep their labels in DORI's own Labels.xml, as before.
    /// </summary>
    public static class SystemTags
    {
        public const string Setting = "SystemTags";
        public const string LinuxAttribute = "user.xdg.tags";
        public const string MacAttribute = "com.apple.metadata:_kMDItemUserTags";

        public static bool Supported => OperatingSystem.IsLinux() || OperatingSystem.IsMacOS();

        /// <summary>On unless turned off in the settings (&lt;SystemTags&gt;false&lt;/SystemTags&gt;).</summary>
        public static bool Enabled
        {
            get
            {
                if (!Supported) return false;
                _enabled ??= !string.Equals(ConfigSettings.Load(Setting), "false", StringComparison.OrdinalIgnoreCase);
                return _enabled.Value;
            }
            set { _enabled = value; ConfigSettings.Save(Setting, value ? "true" : "false"); }
        }
        private static bool? _enabled;

        // ---- Reading and writing the attribute ----

        /// <summary>The label kept on the file, or null when it has none (or the drive keeps none).</summary>
        public static FileLabel? Read(string path)
        {
            try
            {
                if (OperatingSystem.IsLinux())
                {
                    var bytes = GetAttribute(path, LinuxAttribute);
                    return bytes == null ? null : FromLinux(Encoding.UTF8.GetString(bytes));
                }
                if (OperatingSystem.IsMacOS())
                {
                    var bytes = GetAttribute(path, MacAttribute);
                    return bytes == null ? null : FromMac(BinaryPlist.ReadStringArray(bytes));
                }
            }
            catch { }
            return null;
        }

        /// <summary>Puts the label on the file (or takes the attribute away for no label). False when the drive won't keep it.</summary>
        public static bool Write(string path, FileLabel label)
        {
            try
            {
                if (OperatingSystem.IsLinux())
                    return label.IsEmpty ? RemoveAttribute(path, LinuxAttribute)
                                         : SetAttribute(path, LinuxAttribute, Encoding.UTF8.GetBytes(ToLinux(label)));
                if (OperatingSystem.IsMacOS())
                    return label.IsEmpty ? RemoveAttribute(path, MacAttribute)
                                         : SetAttribute(path, MacAttribute, BinaryPlist.WriteStringArray(ToMac(label)));
            }
            catch { }
            return false;
        }

        // ---- Linux: "Red,work,to do" ----

        public static string ToLinux(FileLabel label)
        {
            var parts = new List<string>();
            if (label.Colour != LabelColour.None) parts.Add(label.Colour.ToString());
            parts.AddRange(label.Tags.Where(t => !t.Equals(label.Colour.ToString(), StringComparison.OrdinalIgnoreCase))
                                     .Select(t => t.Replace(",", " ")));
            return string.Join(",", parts);
        }

        public static FileLabel? FromLinux(string text)
        {
            var tags = LabelStore.ParseTags(text.TrimEnd('\0'));
            if (tags.Count == 0) return null;
            var colour = LabelColour.None;
            var rest = new List<string>();
            foreach (var t in tags)
            {
                if (colour == LabelColour.None && t != "None" && Enum.TryParse(t, true, out LabelColour c) && Enum.IsDefined(c) && c != LabelColour.None)
                    colour = c;
                else rest.Add(t);
            }
            return new FileLabel(colour, rest);
        }

        // ---- macOS: Finder's colour numbers ----

        /// <summary>Finder's numbers: 1 grey, 2 green, 3 purple, 4 blue, 5 yellow, 6 red, 7 orange.</summary>
        public static int MacColour(LabelColour c) => c switch
        {
            LabelColour.Grey => 1, LabelColour.Green => 2, LabelColour.Purple => 3, LabelColour.Blue => 4,
            LabelColour.Yellow => 5, LabelColour.Red => 6, LabelColour.Orange => 7, _ => 0,
        };

        public static LabelColour FromMacColour(int n) => n switch
        {
            1 => LabelColour.Grey, 2 => LabelColour.Green, 3 => LabelColour.Purple, 4 => LabelColour.Blue,
            5 => LabelColour.Yellow, 6 => LabelColour.Red, 7 => LabelColour.Orange, _ => LabelColour.None,
        };

        /// <summary>A colour becomes Finder's own tag of that name and colour ("Red\n6"); other tags have no colour.</summary>
        public static List<string> ToMac(FileLabel label)
        {
            var list = new List<string>();
            if (label.Colour != LabelColour.None)
                list.Add($"{(label.Colour == LabelColour.Grey ? "Gray" : label.Colour.ToString())}\n{MacColour(label.Colour)}");
            list.AddRange(label.Tags.Select(t => t + "\n0"));
            return list;
        }

        public static FileLabel? FromMac(IReadOnlyList<string> entries)
        {
            if (entries.Count == 0) return null;
            var colour = LabelColour.None;
            var tags = new List<string>();
            foreach (var e in entries)
            {
                int nl = e.LastIndexOf('\n');
                string name = nl >= 0 ? e[..nl] : e;
                int n = nl >= 0 && int.TryParse(e[(nl + 1)..], out int k) ? k : 0;
                var c = FromMacColour(n);
                // Finder's own colour tags ("Red", "Gray"…) are the colour; any other tag keeps its name.
                bool isColourTag = c != LabelColour.None &&
                                   (name.Equals(c.ToString(), StringComparison.OrdinalIgnoreCase) ||
                                    (c == LabelColour.Grey && name.Equals("Gray", StringComparison.OrdinalIgnoreCase)));
                if (colour == LabelColour.None && c != LabelColour.None) colour = c;
                if (!isColourTag && name.Length > 0) tags.Add(name);
            }
            return new FileLabel(colour, tags.Distinct(StringComparer.OrdinalIgnoreCase).ToList());
        }

        // ---- The system calls ----

        [DllImport("libc", EntryPoint = "getxattr", SetLastError = true)]
        private static extern nint LinuxGet(string path, string name, byte[]? value, nuint size);
        [DllImport("libc", EntryPoint = "setxattr", SetLastError = true)]
        private static extern int LinuxSet(string path, string name, byte[] value, nuint size, int flags);
        [DllImport("libc", EntryPoint = "removexattr", SetLastError = true)]
        private static extern int LinuxRemove(string path, string name);

        [DllImport("libc", EntryPoint = "getxattr", SetLastError = true)]
        private static extern nint MacGet(string path, string name, byte[]? value, nuint size, uint position, int options);
        [DllImport("libc", EntryPoint = "setxattr", SetLastError = true)]
        private static extern int MacSet(string path, string name, byte[] value, nuint size, uint position, int options);
        [DllImport("libc", EntryPoint = "removexattr", SetLastError = true)]
        private static extern int MacRemove(string path, string name, int options);

        private static byte[]? GetAttribute(string path, string name)
        {
            nint size = OperatingSystem.IsMacOS() ? MacGet(path, name, null, 0, 0, 0) : LinuxGet(path, name, null, 0);
            if (size <= 0 || size > 1 << 20) return null;
            var buffer = new byte[size];
            nint got = OperatingSystem.IsMacOS() ? MacGet(path, name, buffer, (nuint)size, 0, 0) : LinuxGet(path, name, buffer, (nuint)size);
            if (got <= 0) return null;
            return got == size ? buffer : buffer[..(int)got];
        }

        private static bool SetAttribute(string path, string name, byte[] value)
            => (OperatingSystem.IsMacOS() ? MacSet(path, name, value, (nuint)value.Length, 0, 0)
                                          : LinuxSet(path, name, value, (nuint)value.Length, 0)) == 0;

        private static bool RemoveAttribute(string path, string name)
        {
            int rc = OperatingSystem.IsMacOS() ? MacRemove(path, name, 0) : LinuxRemove(path, name);
            if (rc == 0) return true;
            int err = Marshal.GetLastPInvokeError();
            return err is 61 or 93;   // ENODATA (Linux) / ENOATTR (macOS): there was nothing to remove
        }
    }

    /// <summary>The small part of Apple's binary property list format that Finder tags need: one array of strings.</summary>
    public static class BinaryPlist
    {
        public static byte[] WriteStringArray(IReadOnlyList<string> items)
        {
            int count = items.Count + 1;
            int refSize = count < 256 ? 1 : 2;
            var body = new System.IO.MemoryStream();
            var offsets = new List<long>();
            body.Write("bplist00"u8);

            offsets.Add(body.Position);
            WriteMarker(body, 0xA0, items.Count);
            for (int i = 1; i <= items.Count; i++) WriteSized(body, i, refSize);

            foreach (var s in items)
            {
                offsets.Add(body.Position);
                if (s.All(c => c < 128))
                {
                    WriteMarker(body, 0x50, s.Length);
                    body.Write(Encoding.ASCII.GetBytes(s));
                }
                else
                {
                    WriteMarker(body, 0x60, s.Length);
                    body.Write(Encoding.BigEndianUnicode.GetBytes(s));
                }
            }

            long tableStart = body.Position;
            int offsetSize = tableStart < 256 ? 1 : tableStart < 65536 ? 2 : 4;
            foreach (var o in offsets) WriteSized(body, o, offsetSize);

            // Trailer: 6 unused bytes, offset size, ref size, object count, top object, table offset.
            body.Write(new byte[6]);
            body.WriteByte((byte)offsetSize);
            body.WriteByte((byte)refSize);
            WriteSized(body, count, 8);
            WriteSized(body, 0, 8);
            WriteSized(body, tableStart, 8);
            return body.ToArray();
        }

        /// <summary>The strings of a plist whose top object is an array; anything else in it is skipped. Empty when it isn't one.</summary>
        public static List<string> ReadStringArray(byte[] data)
        {
            var list = new List<string>();
            if (data.Length < 40 || Encoding.ASCII.GetString(data, 0, 6) != "bplist") return list;
            int t = data.Length - 32;
            int offsetSize = data[t + 6], refSize = data[t + 7];
            long count = ReadSized(data, t + 8, 8), top = ReadSized(data, t + 16, 8), table = ReadSized(data, t + 24, 8);
            if (offsetSize is < 1 or > 8 || refSize is < 1 or > 8 || count <= 0 || count > 100000 || top >= count
                || table < 8 || table + count * offsetSize > t) return list;

            long Offset(long index) => ReadSized(data, (int)(table + index * offsetSize), offsetSize);

            long at = Offset(top);
            if (at < 8 || at >= t || (data[at] & 0xF0) != 0xA0) return list;
            var (n, start) = ReadLength(data, (int)at);
            for (long i = 0; i < n; i++)
            {
                int refAt = start + (int)(i * refSize);
                if (refAt + refSize > t) break;
                long r = ReadSized(data, refAt, refSize);
                if (r >= count) continue;
                long o = Offset(r);
                if (o < 8 || o >= t) continue;
                int marker = data[o] & 0xF0;
                var (len, s) = ReadLength(data, (int)o);
                if (marker == 0x50 && s + len <= t) list.Add(Encoding.ASCII.GetString(data, s, (int)len));
                else if (marker == 0x60 && s + len * 2 <= t) list.Add(Encoding.BigEndianUnicode.GetString(data, s, (int)len * 2));
                else if (marker == 0x70 && s + len <= t) list.Add(Encoding.UTF8.GetString(data, s, (int)len));
            }
            return list;
        }

        private static void WriteMarker(System.IO.Stream s, int type, int length)
        {
            if (length < 15) { s.WriteByte((byte)(type | length)); return; }
            s.WriteByte((byte)(type | 0x0F));
            if (length < 256) { s.WriteByte(0x10); s.WriteByte((byte)length); }
            else if (length < 65536) { s.WriteByte(0x11); WriteSized(s, length, 2); }
            else { s.WriteByte(0x12); WriteSized(s, length, 4); }
        }

        private static (long Length, int Start) ReadLength(byte[] d, int at)
        {
            int low = d[at] & 0x0F;
            if (low != 0x0F) return (low, at + 1);
            if (at + 2 >= d.Length || (d[at + 1] & 0xF0) != 0x10) return (0, at + 1);
            int size = 1 << (d[at + 1] & 0x0F);
            if (at + 2 + size > d.Length) return (0, at + 1);
            return (ReadSized(d, at + 2, size), at + 2 + size);
        }

        private static void WriteSized(System.IO.Stream s, long value, int size)
        {
            for (int i = size - 1; i >= 0; i--) s.WriteByte((byte)(value >> (8 * i)));
        }

        private static long ReadSized(byte[] d, int at, int size)
        {
            long v = 0;
            for (int i = 0; i < size && at + i < d.Length; i++) v = (v << 8) | d[at + i];
            return v;
        }
    }
}
