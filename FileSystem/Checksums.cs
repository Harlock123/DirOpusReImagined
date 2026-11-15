using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;

namespace DirOpusReImagined.FileSystem;

public enum ChecksumAlgorithm
{
    MD5,
    SHA1,
    SHA256,
    SHA512,
}

/// <summary>One line of a checksum file: the expected hash and the file it belongs to.</summary>
/// <param name="Name">The file's path as written in the checksum file, relative to it, with '/' separators.</param>
public sealed record ChecksumEntry(string Hash, string Name, ChecksumAlgorithm Algorithm);

public enum VerifyStatus
{
    Ok,
    Mismatch,
    Missing,
    Unreadable,
}

/// <summary>The result of checking one file against a checksum file.</summary>
public sealed record VerifyResult(ChecksumEntry Entry, VerifyStatus Status, string? ActualHash, string? Error);

/// <summary>
/// Computing, saving and verifying file checksums. Files are read through
/// <see cref="ProviderRegistry"/>, so local files, files inside archives and cloud files all work
/// (a cloud file is downloaded to hash it). Checksum files use the formats <c>sha256sum</c>,
/// <c>md5sum</c> and friends read and write (<c>hash  name</c>, or <c>hash *name</c> in binary
/// mode), and BSD-style <c>SHA256 (name) = hash</c> lines are understood too.
/// </summary>
public static class Checksums
{
    private const int BufferSize = 1024 * 1024;

    /// <summary>The name shown to people: "SHA-256" and so on.</summary>
    public static string DisplayName(ChecksumAlgorithm a) => a switch
    {
        ChecksumAlgorithm.MD5 => "MD5",
        ChecksumAlgorithm.SHA1 => "SHA-1",
        ChecksumAlgorithm.SHA256 => "SHA-256",
        ChecksumAlgorithm.SHA512 => "SHA-512",
        _ => a.ToString(),
    };

    /// <summary>The usual extension for a checksum file of this kind, e.g. ".sha256".</summary>
    public static string Extension(ChecksumAlgorithm a) => "." + a.ToString().ToLowerInvariant();

    /// <summary>Length of the hash in hex digits.</summary>
    public static int HexLength(ChecksumAlgorithm a) => a switch
    {
        ChecksumAlgorithm.MD5 => 32,
        ChecksumAlgorithm.SHA1 => 40,
        ChecksumAlgorithm.SHA256 => 64,
        ChecksumAlgorithm.SHA512 => 128,
        _ => throw new ArgumentOutOfRangeException(nameof(a)),
    };

    /// <summary>The algorithm a hash of this many hex digits must come from, or null.</summary>
    public static ChecksumAlgorithm? FromHexLength(int length) => length switch
    {
        32 => ChecksumAlgorithm.MD5,
        40 => ChecksumAlgorithm.SHA1,
        64 => ChecksumAlgorithm.SHA256,
        128 => ChecksumAlgorithm.SHA512,
        _ => null,
    };

    /// <summary>
    /// True for a file that looks like a checksum list: .md5, .sha1, .sha256, .sha512 (also
    /// .sha256sum and the like), or names such as SHA256SUMS and MD5SUMS.
    /// </summary>
    public static bool IsChecksumFileName(string name)
    {
        var n = name.ToLowerInvariant();
        foreach (var a in Enum.GetValues<ChecksumAlgorithm>())
        {
            string ext = Extension(a);
            if (n.EndsWith(ext) || n.EndsWith(ext + "sum") || n.EndsWith(ext + "sums")) return true;
            string bare = a.ToString().ToLowerInvariant();
            if (n == bare + "sums" || n == bare + "sum" || n == bare + "sums.txt") return true;
        }
        return false;
    }

    /// <summary>Hashes one file, reporting bytes read. Returns lowercase hex.</summary>
    public static string Compute(string path, ChecksumAlgorithm algorithm, Action<long>? bytesRead = null,
                                 CancellationToken ct = default)
    {
        using var stream = ProviderRegistry.For(path).OpenRead(path);
        return Compute(stream, algorithm, bytesRead, ct);
    }

    /// <summary>Hashes a stream to its end. Returns lowercase hex.</summary>
    public static string Compute(Stream stream, ChecksumAlgorithm algorithm, Action<long>? bytesRead = null,
                                 CancellationToken ct = default)
    {
        using var hash = IncrementalHash.CreateHash(algorithm switch
        {
            ChecksumAlgorithm.MD5 => HashAlgorithmName.MD5,
            ChecksumAlgorithm.SHA1 => HashAlgorithmName.SHA1,
            ChecksumAlgorithm.SHA256 => HashAlgorithmName.SHA256,
            ChecksumAlgorithm.SHA512 => HashAlgorithmName.SHA512,
            _ => throw new ArgumentOutOfRangeException(nameof(algorithm)),
        });
        var buffer = new byte[BufferSize];
        int n;
        while ((n = stream.Read(buffer, 0, buffer.Length)) > 0)
        {
            ct.ThrowIfCancellationRequested();
            hash.AppendData(buffer, 0, n);
            bytesRead?.Invoke(n);
        }
        return Convert.ToHexString(hash.GetHashAndReset()).ToLowerInvariant();
    }

    /// <summary>
    /// Reduces a pasted hash to bare lowercase hex: surrounding text such as "SHA256: " or a
    /// trailing file name is dropped, and spaces or colons between digit groups are removed.
    /// Returns "" if there's no hash-length run of hex digits in it.
    /// </summary>
    public static string NormalizeHash(string? text)
    {
        if (string.IsNullOrWhiteSpace(text)) return "";
        // Candidates: runs of hex digits with single spaces or colons between groups (a hash
        // pasted as "e3 b0 c4…" or "E3:B0:C4…"), and each unbroken run on its own (so a hash
        // followed by " cafe.txt" isn't spoiled by the hex-looking name). Take the longest one
        // that is the length of a real hash.
        var grouped = Regex.Matches(text, @"[0-9A-Fa-f]{2}(?:[ :]?[0-9A-Fa-f]{2})*")
            .Select(m => Regex.Replace(m.Value, "[ :]", ""));
        var plain = Regex.Matches(text, "[0-9A-Fa-f]+").Select(m => m.Value);
        var best = grouped.Concat(plain)
            .Where(s => FromHexLength(s.Length) != null)
            .OrderByDescending(s => s.Length)
            .FirstOrDefault() ?? "";
        return best.ToLowerInvariant();
    }

    /// <summary>True if two hashes are the same, ignoring case and separators.</summary>
    public static bool Matches(string? a, string? b)
    {
        string x = NormalizeHash(a), y = NormalizeHash(b);
        return x.Length > 0 && x == y;
    }

    /// <summary>A checksum file line in the coreutils format, which <c>sha256sum -c</c> reads.</summary>
    public static string FormatLine(string hash, string name)
    {
        // coreutils escapes names containing a newline or backslash and marks the line with "\".
        if (name.Contains('\n') || name.Contains('\\'))
            return "\\" + hash + "  " + name.Replace("\\", "\\\\").Replace("\n", "\\n");
        return hash + "  " + name;
    }

    /// <summary>The whole checksum file for these (hash, name) pairs, one line each.</summary>
    public static string FormatFile(IEnumerable<(string Hash, string Name)> entries)
    {
        var sb = new StringBuilder();
        foreach (var (hash, name) in entries) sb.Append(FormatLine(hash, name)).Append('\n');
        return sb.ToString();
    }

    private static readonly Regex GnuLine = new(@"^(\\)?([0-9A-Fa-f]+) [ *](.+)$", RegexOptions.Compiled);
    private static readonly Regex BsdLine = new(@"^(MD5|SHA1|SHA256|SHA512|SHA-1|SHA-256|SHA-512) ?\((.+)\) ?= ?([0-9A-Fa-f]+)$",
                                                RegexOptions.Compiled | RegexOptions.IgnoreCase);

    /// <summary>
    /// Reads the entries of a checksum file. Blank lines, comments (#) and lines that aren't
    /// checksums are skipped. <paramref name="fallback"/> is used if a hash's length doesn't say
    /// which algorithm made it (it always does for the four supported ones).
    /// </summary>
    public static List<ChecksumEntry> Parse(string text, ChecksumAlgorithm? fallback = null)
    {
        var entries = new List<ChecksumEntry>();
        foreach (var raw in text.Split('\n'))
        {
            string line = raw.TrimEnd('\r');
            if (line.Length == 0 || line.StartsWith('#')) continue;

            string hash, name;
            var bsd = BsdLine.Match(line);
            var gnu = GnuLine.Match(line);
            if (bsd.Success)
            {
                name = bsd.Groups[2].Value;
                hash = bsd.Groups[3].Value;
            }
            else if (gnu.Success)
            {
                hash = gnu.Groups[2].Value;
                name = gnu.Groups[3].Value;
                if (gnu.Groups[1].Success) name = Unescape(name);
            }
            else continue;

            var algorithm = FromHexLength(hash.Length) ?? fallback;
            if (algorithm == null) continue;
            entries.Add(new ChecksumEntry(hash.ToLowerInvariant(), name.Replace('\\', '/'), algorithm.Value));
        }
        return entries;
    }

    private static string Unescape(string name)
    {
        var sb = new StringBuilder(name.Length);
        for (int i = 0; i < name.Length; i++)
        {
            if (name[i] == '\\' && i + 1 < name.Length)
            {
                char next = name[++i];
                sb.Append(next == 'n' ? '\n' : next);
            }
            else sb.Append(name[i]);
        }
        return sb.ToString();
    }

    /// <summary>
    /// Checks each entry of a checksum file against the files beside it. <paramref name="folder"/>
    /// is the folder the checksum file is in; entries name files relative to it.
    /// <paramref name="onResult"/> is called as each file is checked.
    /// </summary>
    public static List<VerifyResult> Verify(string folder, IReadOnlyList<ChecksumEntry> entries,
                                            Action<VerifyResult>? onResult = null, Action<long>? bytesRead = null,
                                            CancellationToken ct = default)
    {
        var results = new List<VerifyResult>();
        foreach (var entry in entries)
        {
            ct.ThrowIfCancellationRequested();
            string path = ResolveEntry(folder, entry.Name);
            VerifyResult result;
            var provider = ProviderRegistry.For(path);
            if (!SafeFileExists(provider, path))
            {
                result = new VerifyResult(entry, VerifyStatus.Missing, null, null);
            }
            else
            {
                try
                {
                    string actual = Compute(path, entry.Algorithm, bytesRead, ct);
                    result = new VerifyResult(entry, actual == entry.Hash ? VerifyStatus.Ok : VerifyStatus.Mismatch, actual, null);
                }
                catch (OperationCanceledException) { throw; }
                catch (Exception ex)
                {
                    result = new VerifyResult(entry, VerifyStatus.Unreadable, null, ex.Message);
                }
            }
            results.Add(result);
            onResult?.Invoke(result);
        }
        return results;
    }

    /// <summary>The full path of a checksum-file entry, which is relative to the checksum file's folder.</summary>
    public static string ResolveEntry(string folder, string name)
    {
        string path = folder;
        foreach (var part in name.TrimStart('*').Split('/', StringSplitOptions.RemoveEmptyEntries))
        {
            if (part == ".") continue;
            path = FileUtility.JoinPanelPath(path, part);
        }
        return path;
    }

    private static bool SafeFileExists(IFileProvider p, string path)
    {
        try { return p.FileExists(path); }
        catch { return false; }
    }

    /// <summary>
    /// The files to hash for a selection: each selected file, and every file inside each selected
    /// folder (recursively). Names are relative to <paramref name="folder"/>, with '/' separators,
    /// so they can go straight into a checksum file saved there.
    /// </summary>
    public static List<(string Path, string Name, long Size)> CollectFiles(
        string folder, IEnumerable<(string Name, bool IsDir)> selection, CancellationToken ct = default)
    {
        var files = new List<(string, string, long)>();
        foreach (var (name, isDir) in selection)
        {
            ct.ThrowIfCancellationRequested();
            string path = FileUtility.JoinPanelPath(folder, name);
            var provider = ProviderRegistry.For(path);
            if (isDir) Walk(provider, path, name, files, ct);
            else
            {
                long size = 0;
                try { size = provider.Stat(path)?.Size ?? 0; } catch { /* size is only for progress */ }
                files.Add((path, name, size));
            }
        }
        return files;
    }

    private static void Walk(IFileProvider provider, string path, string rel,
                             List<(string, string, long)> files, CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        List<FileEntry> fileEntries, dirEntries;
        try
        {
            fileEntries = provider.EnumerateFiles(path).OrderBy(f => f.Name, StringComparer.Ordinal).ToList();
            dirEntries = provider.EnumerateDirectories(path).OrderBy(d => d.Name, StringComparer.Ordinal).ToList();
        }
        catch
        {
            return; // unreadable folder: nothing from it
        }
        foreach (var f in fileEntries) files.Add((f.Path, rel + "/" + f.Name, f.Size));
        // Linked folders aren't followed (they may loop back to a parent).
        foreach (var d in dirEntries.Where(d => !d.IsLink)) Walk(provider, d.Path, rel + "/" + d.Name, files, ct);
    }
}
