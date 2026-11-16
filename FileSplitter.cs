using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text.RegularExpressions;
using System.Threading;

namespace DirOpusReImagined
{
    /// <summary>
    /// Splitting a large file into numbered pieces (<c>name.001</c>, <c>name.002</c>, …) with a
    /// <c>name.sha256</c> file holding the whole file's SHA-256, and joining the pieces back
    /// together, checking the result against that file when it is there. The piece names are the
    /// ones 7-Zip and HJSplit use, so pieces made elsewhere join here too.
    /// </summary>
    public static class FileSplitter
    {
        private const int BufferSize = 1 << 20;
        private static readonly Regex PieceName = new(@"^(.*)\.(\d{3,})$", RegexOptions.CultureInvariant);

        /// <summary>Reads a piece size such as "100 MB", "4.7GB", "650m" or "1048576" (bytes). Units are powers of 1024.</summary>
        public static bool TryParseSize(string? text, out long bytes)
        {
            bytes = 0;
            var m = Regex.Match((text ?? "").Trim(), @"^(\d+(?:[.,]\d+)?)\s*([kmgt]?)i?b?$", RegexOptions.IgnoreCase);
            if (!m.Success) return false;
            if (!double.TryParse(m.Groups[1].Value.Replace(',', '.'), NumberStyles.Float, CultureInfo.InvariantCulture, out double n)) return false;
            double scale = char.ToLowerInvariant(m.Groups[2].Value.FirstOrDefault()) switch
            {
                'k' => 1024d, 'm' => 1024d * 1024, 'g' => 1024d * 1024 * 1024, 't' => 1024d * 1024 * 1024 * 1024, _ => 1d,
            };
            double v = Math.Floor(n * scale);
            if (v < 1 || v > long.MaxValue / 2) return false;
            bytes = (long)v;
            return true;
        }

        /// <summary>How many pieces a file of <paramref name="length"/> bytes makes.</summary>
        public static int PieceCount(long length, long pieceSize) => length == 0 ? 1 : (int)((length + pieceSize - 1) / pieceSize);

        /// <summary>The name of piece <paramref name="number"/> (from 1): three digits, more when there are over 999 pieces.</summary>
        public static string PieceFileName(string fileName, int number, int count)
            => $"{fileName}.{number.ToString(new string('0', Math.Max(3, count.ToString(CultureInfo.InvariantCulture).Length)), CultureInfo.InvariantCulture)}";

        /// <summary>
        /// Writes the pieces of <paramref name="source"/> into <paramref name="targetFolder"/>, plus
        /// the checksum file. Refuses to overwrite anything. Returns the files written. A cancel or
        /// failure removes what was written so far.
        /// </summary>
        public static List<string> Split(string source, string targetFolder, long pieceSize,
                                         IProgress<(long Done, long Total)>? progress = null, CancellationToken ct = default)
        {
            if (pieceSize < 1) throw new ArgumentOutOfRangeException(nameof(pieceSize));
            long length = new FileInfo(source).Length;
            string name = Path.GetFileName(source);
            int count = PieceCount(length, pieceSize);
            if (count > 99_999) throw new IOException($"That would make {count:N0} pieces. Choose a larger piece size.");

            var targets = Enumerable.Range(1, count).Select(i => Path.Combine(targetFolder, PieceFileName(name, i, count))).ToList();
            string sumFile = Path.Combine(targetFolder, name + ".sha256");
            var clash = targets.Append(sumFile).FirstOrDefault(p => File.Exists(p) || Directory.Exists(p));
            if (clash != null) throw new IOException($"{Path.GetFileName(clash)} already exists in the destination.");

            var written = new List<string>();
            try
            {
                using var sha = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
                using var input = new FileStream(source, FileMode.Open, FileAccess.Read, FileShare.Read, BufferSize);
                var buffer = new byte[BufferSize];
                long done = 0;
                foreach (var target in targets)
                {
                    written.Add(target);
                    using var output = new FileStream(target, FileMode.CreateNew, FileAccess.Write, FileShare.None, BufferSize);
                    long left = Math.Min(pieceSize, length - done);
                    while (left > 0)
                    {
                        ct.ThrowIfCancellationRequested();
                        int n = input.Read(buffer, 0, (int)Math.Min(buffer.Length, left));
                        if (n == 0) throw new IOException($"{name} got shorter while it was being split.");
                        output.Write(buffer, 0, n);
                        sha.AppendData(buffer, 0, n);
                        left -= n;
                        done += n;
                        progress?.Report((done, length));
                    }
                }
                written.Add(sumFile);
                File.WriteAllText(sumFile, $"{Convert.ToHexString(sha.GetHashAndReset()).ToLowerInvariant()}  {name}\n");
                return written;
            }
            catch
            {
                foreach (var f in written) { try { File.Delete(f); } catch { } }
                throw;
            }
        }

        /// <summary>The joined file's name for a piece ("film.mkv" for "film.mkv.003"), or null if it isn't a piece.</summary>
        public static string? JoinedName(string pieceFileName)
        {
            var m = PieceName.Match(pieceFileName);
            return m.Success && m.Groups[1].Value.Length > 0 ? m.Groups[1].Value : null;
        }

        /// <summary>
        /// All pieces belonging with <paramref name="anyPiece"/>, in order, from piece 1 up to the
        /// first number missing. Errors when piece 1 is missing.
        /// </summary>
        public static List<string> FindPieces(string anyPiece)
        {
            string folder = Path.GetDirectoryName(anyPiece) ?? ".";
            string? joined = JoinedName(Path.GetFileName(anyPiece)) ?? throw new IOException($"{Path.GetFileName(anyPiece)} isn't a numbered piece (name.001).");
            var byNumber = new SortedDictionary<int, string>();
            foreach (var f in Directory.EnumerateFiles(folder))
            {
                var m = PieceName.Match(Path.GetFileName(f));
                if (m.Success && m.Groups[1].Value == joined && int.TryParse(m.Groups[2].Value, out int n) && n > 0)
                    byNumber.TryAdd(n, f);
            }
            if (!byNumber.ContainsKey(1)) throw new IOException($"The first piece, {joined}.001, isn't there.");
            var list = new List<string>();
            for (int i = 1; byNumber.TryGetValue(i, out var p); i++) list.Add(p);
            return list;
        }

        /// <summary>What joining found: the file written, and whether it matched the checksum file (null when there was none).</summary>
        public sealed record JoinResult(string Target, int Pieces, bool? Verified);

        /// <summary>
        /// Joins the pieces <paramref name="anyPiece"/> belongs to into <paramref name="targetFolder"/>.
        /// When <c>name.sha256</c> sits beside the pieces the result is checked against it, and a
        /// mismatch is an error (the joined file is kept, so it can be looked at). Refuses to
        /// overwrite; a cancel removes the partly joined file.
        /// </summary>
        public static JoinResult Join(string anyPiece, string targetFolder,
                                      IProgress<(long Done, long Total)>? progress = null, CancellationToken ct = default)
        {
            var pieces = FindPieces(anyPiece);
            string name = JoinedName(Path.GetFileName(anyPiece))!;
            string target = Path.Combine(targetFolder, name);
            if (File.Exists(target) || Directory.Exists(target)) throw new IOException($"{name} already exists in the destination.");
            long total = pieces.Sum(p => new FileInfo(p).Length);

            string? expected = ReadExpectedHash(Path.Combine(Path.GetDirectoryName(anyPiece) ?? ".", name + ".sha256"));
            string actual;
            try
            {
                using var sha = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
                using (var output = new FileStream(target, FileMode.CreateNew, FileAccess.Write, FileShare.None, BufferSize))
                {
                    var buffer = new byte[BufferSize];
                    long done = 0;
                    foreach (var piece in pieces)
                    {
                        using var input = new FileStream(piece, FileMode.Open, FileAccess.Read, FileShare.Read, BufferSize);
                        int n;
                        while ((n = input.Read(buffer, 0, buffer.Length)) > 0)
                        {
                            ct.ThrowIfCancellationRequested();
                            output.Write(buffer, 0, n);
                            sha.AppendData(buffer, 0, n);
                            done += n;
                            progress?.Report((done, total));
                        }
                    }
                }
                actual = Convert.ToHexString(sha.GetHashAndReset());
            }
            catch
            {
                try { File.Delete(target); } catch { }
                throw;
            }

            if (expected == null) return new JoinResult(target, pieces.Count, null);
            if (!string.Equals(expected, actual, StringComparison.OrdinalIgnoreCase))
                throw new IOException($"{name} was joined from {pieces.Count} pieces, but it doesn't match {name}.sha256. " +
                                      "A piece may be damaged or missing.");
            return new JoinResult(target, pieces.Count, true);
        }

        private static string? ReadExpectedHash(string sumFile)
        {
            try
            {
                if (!File.Exists(sumFile)) return null;
                var m = Regex.Match(File.ReadAllText(sumFile), @"\b([0-9a-fA-F]{64})\b");
                return m.Success ? m.Groups[1].Value : null;
            }
            catch { return null; }
        }
    }
}
