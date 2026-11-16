using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using SharpCompress.Archives;
using SharpCompress.Readers;

namespace DirOpusReImagined.FileSystem.Archive;

/// <summary>
/// Passwords for encrypted archives, kept in memory for this session only (never written to disk),
/// and which archives turned out to be encrypted. The archive provider opens archives with the
/// password given here; the main window asks for it when an encrypted archive is opened.
/// </summary>
public static class ArchivePasswords
{
    private static readonly Dictionary<string, string> Known =
        new(OperatingSystem.IsLinux() ? StringComparer.Ordinal : StringComparer.OrdinalIgnoreCase);
    private static readonly HashSet<string> Encrypted =
        new(OperatingSystem.IsLinux() ? StringComparer.Ordinal : StringComparer.OrdinalIgnoreCase);
    private static readonly object Gate = new();

    public static void Set(string archiveFsPath, string password)
    {
        lock (Gate) { Known[archiveFsPath] = password; Encrypted.Add(archiveFsPath); }
    }

    public static string? Get(string archiveFsPath)
    {
        lock (Gate) return Known.TryGetValue(archiveFsPath, out var p) ? p : null;
    }

    /// <summary>True once the archive is known to be password-protected (so it is treated as read-only).</summary>
    public static bool IsEncrypted(string archiveFsPath)
    {
        lock (Gate) return Encrypted.Contains(archiveFsPath);
    }

    /// <summary>The options to open the archive with: its password when one is known.</summary>
    public static ReaderOptions OptionsFor(string archiveFsPath)
        => Get(archiveFsPath) is { } pw ? new ReaderOptions { Password = pw } : new ReaderOptions();

    /// <summary>
    /// Whether opening the archive needs a password DORI doesn't have yet: some entry is encrypted,
    /// or (7z with encrypted names) the archive can't even be listed without one. Archives that
    /// aren't encrypted, or whose password is already known and right, say no.
    /// </summary>
    public static bool NeedsPassword(string archiveFsPath)
    {
        if (Get(archiveFsPath) is { } known && Try(archiveFsPath, known)) return false;
        try
        {
            using var archive = ArchiveFactory.OpenArchive(archiveFsPath, new ReaderOptions());
            bool any = archive.Entries.Any(e => e.IsEncrypted);
            if (any) lock (Gate) Encrypted.Add(archiveFsPath);
            return any;
        }
        catch (Exception ex) when (LooksLikePassword(ex))
        {
            lock (Gate) Encrypted.Add(archiveFsPath);
            return true;
        }
        catch
        {
            return false;   // some other problem (a .tar.gz, a damaged file): the normal paths report it
        }
    }

    /// <summary>Whether <paramref name="password"/> opens the archive: it lists, and the first encrypted file decrypts and reads through.</summary>
    public static bool Try(string archiveFsPath, string password)
    {
        try
        {
            using var archive = ArchiveFactory.OpenArchive(archiveFsPath, new ReaderOptions { Password = password });
            var entry = archive.Entries.FirstOrDefault(e => !e.IsDirectory && e.IsEncrypted)
                        ?? archive.Entries.FirstOrDefault(e => !e.IsDirectory);
            if (entry == null) return true;
            using var s = entry.OpenEntryStream();
            var buffer = new byte[81920];
            long left = 4L * 1024 * 1024;   // the check code is at the end of each zip entry, so read a fair way
            int n;
            while (left > 0 && (n = s.Read(buffer, 0, buffer.Length)) > 0) left -= n;
            return true;
        }
        catch { return false; }
    }

    private static bool LooksLikePassword(Exception ex)
    {
        for (var e = ex; e != null; e = e.InnerException)
        {
            string m = e.Message;
            if (e is System.Security.Cryptography.CryptographicException
                || e.GetType().Name.Contains("Crypt", StringComparison.Ordinal)
                || m.Contains("password", StringComparison.OrdinalIgnoreCase)
                || m.Contains("encrypt", StringComparison.OrdinalIgnoreCase))
                return true;
        }
        return false;
    }
}
