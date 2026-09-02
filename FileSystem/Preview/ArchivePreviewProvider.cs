using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using DirOpusReImagined.FileSystem.Archive;

namespace DirOpusReImagined.FileSystem.Preview;

/// <summary>
/// Describes an archive: what kind it is, what it holds, how well it compressed, and the first
/// screenful of its entries.
///
/// <para>Reuses <see cref="ArchiveFileProvider.ListEntries"/> rather than opening the archive
/// itself, so it inherits the random-access/streaming fallback that compression-wrapped tarballs
/// need, and shares the mtime-keyed cache — previewing an archive the user then browses into costs
/// one read, not two.</para>
/// </summary>
public sealed class ArchivePreviewProvider : IPreviewProvider
{
    /// <summary>
    /// Largest archive worth enumerating. ZIP and 7z only read a central directory, but a
    /// compression-wrapped tarball has no index at all: listing it means decompressing the whole
    /// stream. A cap keeps a huge <c>.tar.gz</c> from burning CPU because the cursor passed over it.
    /// </summary>
    private const long MaxArchiveBytes = 256L * 1024 * 1024;

    /// <summary>How many entries to list. Enough to see what an archive is; short enough that a
    /// 50,000-entry package does not build a giant string for a preview pane.</summary>
    private const int MaxListedEntries = 200;

    /// <summary>Longest entry path shown before the middle is elided.</summary>
    private const int MaxPathLength = 58;

    /// <summary>Below <see cref="ImagePreviewProvider"/> and <see cref="PdfPreviewProvider"/>, above
    /// the byte fallback. A future Office provider slots in above this one to claim the OOXML
    /// documents that are also ZIP files.</summary>
    public int Priority => 80;

    public bool CanPreview(PreviewRequest r)
    {
        if (r.Signature is not (FileSignature.Kind.Zip or FileSignature.Kind.SevenZip
            or FileSignature.Kind.Rar or FileSignature.Kind.Tar or FileSignature.Kind.Gzip
            or FileSignature.Kind.Bzip2 or FileSignature.Kind.Xz))
            return false;

        // The reader opens a real file by path, so an archive nested inside another archive, or one
        // sitting on a cloud remote, cannot be listed - those fall through to the byte preview.
        if (ArchivePath.IsArchiveUri(r.Path)) return false;
        return !r.Provider.IsRemote;
    }

    public Task<PreviewResult> LoadAsync(PreviewRequest r, CancellationToken ct)
        => Task.Run<PreviewResult>(() =>
        {
            try
            {
                ct.ThrowIfCancellationRequested();

                string kind = FileSignature.Describe(r.Signature);

                if (r.Size > MaxArchiveBytes)
                {
                    return new PreviewResult.Info(r.DisplayName, new List<InfoField>
                    {
                        new("Archive", kind),
                        new("Size", PreviewText.FormatSize(r.Size)),
                        new("Contents",
                            $"Not listed — over {MaxArchiveBytes / (1024 * 1024)} MB. Open it in a panel to browse."),
                    }, kind);
                }

                // Reuse the app's registered instance so the entry cache is shared with browsing.
                var archives = ProviderRegistry.For(ArchivePath.RootUriFor(r.Path)) as ArchiveFileProvider
                               ?? new ArchiveFileProvider();

                var entries = archives.ListEntries(r.Path);
                ct.ThrowIfCancellationRequested();

                var files = entries.Where(e => !e.IsDirectory).ToList();
                int folders = entries.Count(e => e.IsDirectory);
                long uncompressed = files.Sum(e => e.Size);

                string container = DescribeContainer(kind, r, entries);

                var fields = new List<InfoField>
                {
                    new("Archive", container),
                    new("Contents", DescribeContents(files.Count, folders)),
                };

                if (uncompressed > 0)
                {
                    fields.Add(new InfoField("Uncompressed", PreviewText.FormatSize(uncompressed)));
                    fields.Add(new InfoField("Compressed",
                        $"{PreviewText.FormatSize(r.Size)}{DescribeRatio(r.Size, uncompressed)}"));
                }
                else
                {
                    fields.Add(new InfoField("Size", PreviewText.FormatSize(r.Size)));
                }

                if (files.Count > 0)
                {
                    fields.Add(new InfoField("", ""));           // blank row before the listing

                    foreach (var e in files.OrderBy(e => e.Key, StringComparer.OrdinalIgnoreCase)
                                           .Take(MaxListedEntries))
                    {
                        ct.ThrowIfCancellationRequested();
                        fields.Add(new InfoField(Elide(e.Key), PreviewText.FormatSize(e.Size)));
                    }

                    if (files.Count > MaxListedEntries)
                        fields.Add(new InfoField("",
                            $"… and {files.Count - MaxListedEntries:N0} more"));
                }

                string subtitle = $"{container} · {DescribeContents(files.Count, folders)}";
                return new PreviewResult.Info(r.DisplayName, fields, subtitle);
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (Exception ex)
            {
                // A corrupt, truncated or password-protected archive is an ordinary outcome.
                return new PreviewResult.Error($"Could not read archive: {ex.Message}");
            }
        }, ct);

    /// <summary>
    /// Names what the archive actually is when its entries give it away. DOCX, XLSX, PPTX, ODF
    /// documents, JARs and EPUBs are all ZIP files, and a bare "ZIP container" is a poor answer when
    /// the contents identify the format precisely.
    /// </summary>
    private static string DescribeContainer(string kind, PreviewRequest r,
                                            IReadOnlyList<ArchiveFileProvider.ArchiveEntry> entries)
    {
        // A .tar.gz identifies by its magic bytes as a plain gzip stream, because that is all the
        // header says. Once the entries are read, the tar inside is not a guess - so name it.
        if (r.Signature is FileSignature.Kind.Gzip or FileSignature.Kind.Bzip2 or FileSignature.Kind.Xz)
        {
            string compressor = r.Signature switch
            {
                FileSignature.Kind.Gzip => "gzip",
                FileSignature.Kind.Bzip2 => "bzip2",
                _ => "xz",
            };
            return entries.Count > 1 || LooksLikeTarball(r.DisplayName)
                ? $"TAR archive ({compressor}-compressed)"
                : $"{compressor}-compressed file";
        }

        bool Has(string key) => entries.Any(e =>
            string.Equals(e.Key, key, StringComparison.OrdinalIgnoreCase));

        bool Under(string prefix) => entries.Any(e =>
            e.Key.StartsWith(prefix, StringComparison.OrdinalIgnoreCase));

        if (Has("word/document.xml")) return "Word document (OOXML, ZIP container)";
        if (Has("xl/workbook.xml")) return "Excel workbook (OOXML, ZIP container)";
        if (Has("ppt/presentation.xml")) return "PowerPoint presentation (OOXML, ZIP container)";
        if (Has("content.xml") && Has("mimetype")) return "OpenDocument file (ZIP container)";
        if (Has("META-INF/container.xml")) return "EPUB book (ZIP container)";
        if (Has("META-INF/MANIFEST.MF")) return "Java archive (JAR, ZIP container)";
        if (Under("AndroidManifest.xml")) return "Android package (APK, ZIP container)";

        return kind;
    }

    private static bool LooksLikeTarball(string name)
    {
        string n = name.ToLowerInvariant();
        return n.EndsWith(".tar.gz") || n.EndsWith(".tgz")
            || n.EndsWith(".tar.bz2") || n.EndsWith(".tbz2")
            || n.EndsWith(".tar.xz") || n.EndsWith(".txz");
    }

    private static string DescribeContents(int files, int folders)
    {
        string f = $"{files:N0} file" + (files == 1 ? "" : "s");
        if (folders <= 0) return f;
        return f + $", {folders:N0} folder" + (folders == 1 ? "" : "s");
    }

    /// <summary>The space saved, when the numbers say anything useful.</summary>
    private static string DescribeRatio(long compressed, long uncompressed)
    {
        if (compressed <= 0 || uncompressed <= 0 || compressed >= uncompressed) return "";
        double saved = 1.0 - (double)compressed / uncompressed;
        return $" ({saved * 100:0}% smaller)";
    }

    /// <summary>
    /// Shortens a long entry path from the middle, keeping the start and the file name — the two
    /// parts that identify it. Trimming the end would hide exactly the part being looked for.
    /// </summary>
    private static string Elide(string path)
    {
        if (path.Length <= MaxPathLength) return path;

        int slash = path.LastIndexOf('/');
        string name = slash >= 0 ? path.Substring(slash + 1) : path;

        if (name.Length >= MaxPathLength - 2)
            return "…" + name.Substring(name.Length - (MaxPathLength - 1));

        int keep = MaxPathLength - name.Length - 2;
        return path.Substring(0, keep) + "…/" + name;
    }
}
