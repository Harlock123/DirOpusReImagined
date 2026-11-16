using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using DirOpusReImagined.FileSystem;
using DirOpusReImagined.FileSystem.Preview;

namespace DirOpusReImagined
{
    /// <summary>
    /// What the rename tokens can say about a file beyond its name: when it last changed, when a
    /// photo was taken (EXIF), and a music or video file's tags. Read through the provider, so
    /// files in archives and the cloud work too (only the start of the file is read).
    /// </summary>
    public sealed record FileFacts(DateTime? Modified, DateTime? Taken, IReadOnlyDictionary<string, string> Tags)
    {
        public static readonly FileFacts None = new(null, null, new Dictionary<string, string>());

        /// <summary>The tag names the rename tokens use (%ARTIST% and so on), as the media preview labels them.</summary>
        public static readonly string[] TagNames = { "Title", "Artist", "Album", "Year", "Track", "Genre" };

        public static FileFacts Read(string path)
        {
            try
            {
                var provider = ProviderRegistry.For(path);
                DateTime? modified = null;
                long size = -1;
                try
                {
                    var stat = provider.Stat(path);
                    if (stat != null) { modified = stat.LastModified; size = stat.Size; }
                }
                catch { }

                byte[] head;
                using (var s = provider.OpenRead(path))
                    head = PreviewText.ReadUpTo(s, Math.Max(FileSignature.HeadBytes, ExifReader.ProbeBytes), default);
                var kind = FileSignature.Detect(head);

                DateTime? taken = null;
                var tags = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
                if (FileSignature.IsImage(kind) && ExifReader.Read(head) is { } photo)
                {
                    var t = photo.Fields.FirstOrDefault(f => f.Label == "Taken").Value;
                    if (DateTime.TryParseExact(t, "yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture, DateTimeStyles.None, out var d))
                        taken = d;
                }
                else
                {
                    var media = new MediaPreviewProvider();
                    var request = new PreviewRequest
                    {
                        Path = path, DisplayName = System.IO.Path.GetFileName(path), Provider = provider,
                        Size = size, Head = head.Length > FileSignature.HeadBytes ? head[..FileSignature.HeadBytes] : head,
                        Signature = kind, DetailsOnly = true,
                    };
                    if (media.CanPreview(request) && media.LoadAsync(request, default).GetAwaiter().GetResult() is PreviewResult.Info info)
                        foreach (var f in info.Fields.Where(f => TagNames.Contains(f.Label)))
                            tags[f.Label] = f.Value;
                }
                return new FileFacts(modified, taken, tags);
            }
            catch
            {
                return None;
            }
        }
    }
}
