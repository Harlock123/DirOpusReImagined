using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Security.Cryptography;
using System.Text;

namespace DirOpusReImagined.FileSystem.Archive;

/// <summary>
/// Writes a password-protected .zip in the WinZip AES format (AE-2, AES-256), which 7-Zip,
/// WinZip, macOS's Archive Utility (with a helper), libarchive/bsdtar and DORI itself open.
/// .NET's ZipArchive can't encrypt, and SharpCompress only reads encryption, so this writes the
/// zip structure directly: each file is deflated, then encrypted with AES in counter mode under a
/// key derived from the password (PBKDF2-HMAC-SHA1, 1000 rounds, a fresh random salt per file) and
/// sealed with an HMAC-SHA1 code. Names and sizes in a zip are never encrypted, in any tool.
/// No Zip64: a single file of 4 GB or more, or more than 65,535 entries, is refused.
/// </summary>
public sealed class AesZipWriter : IDisposable
{
    private const ushort VersionAes = 51;
    private const ushort MethodAes = 99;
    private const ushort FlagEncrypted = 1, FlagUtf8 = 1 << 11;
    private const int SaltLength = 16, KeyLength = 32, Iterations = 1000;

    private readonly Stream _out;
    private readonly byte[] _password;
    private readonly CompressionLevel _level;
    private readonly List<(byte[] Name, ushort Flags, ushort Method, ushort Time, ushort Date, long CSize, long USize, long Offset, uint External, byte[] Extra, bool Dir)> _central = new();
    private bool _finished;

    /// <param name="output">Must be seekable: each entry's sizes are filled in after its data is written.</param>
    public AesZipWriter(Stream output, string password, CompressionLevel level = CompressionLevel.Optimal)
    {
        if (!output.CanSeek) throw new ArgumentException("The output must be seekable.", nameof(output));
        if (string.IsNullOrEmpty(password)) throw new ArgumentException("A password is needed.", nameof(password));
        _out = output;
        _password = Encoding.UTF8.GetBytes(password);
        _level = level;
    }

    /// <summary>A folder entry ("photos/"); folders hold nothing, so nothing is encrypted.</summary>
    public void AddFolder(string name, DateTime modified)
    {
        if (!name.EndsWith('/')) name += "/";
        var (time, date) = DosTime(modified);
        long offset = _out.Position;
        byte[] nameBytes = Encoding.UTF8.GetBytes(name);
        WriteLocalHeader(nameBytes, FlagUtf8, 20, 0, time, date, 0, 0, Array.Empty<byte>());
        Add(nameBytes, FlagUtf8, 0, time, date, 0, 0, offset, 0x10, Array.Empty<byte>(), dir: true);
    }

    /// <summary>Compresses and encrypts <paramref name="data"/> as the file <paramref name="name"/>.</summary>
    public void AddFile(string name, Stream data, DateTime modified, uint unixMode = 0, Action<int>? copied = null,
                        System.Threading.CancellationToken ct = default)
    {
        if (_central.Count >= 65535) throw new IOException("A password-protected zip from DORI holds at most 65,535 items.");
        var (time, date) = DosTime(modified);
        byte[] nameBytes = Encoding.UTF8.GetBytes(name);
        bool store = _level == CompressionLevel.NoCompression;
        ushort realMethod = (ushort)(store ? 0 : 8);
        byte[] extra = AesExtra(realMethod);
        ushort flags = FlagEncrypted | FlagUtf8;

        long offset = _out.Position;
        WriteLocalHeader(nameBytes, flags, VersionAes, MethodAes, time, date, 0, 0, extra);
        long dataStart = _out.Position;

        // Salt and the two-byte password check go first, then the encrypted data, then the code.
        byte[] salt = RandomNumberGenerator.GetBytes(SaltLength);
        byte[] keys = Rfc2898DeriveBytes.Pbkdf2(_password, salt, Iterations, HashAlgorithmName.SHA1, 2 * KeyLength + 2);
        _out.Write(salt);
        _out.Write(keys, 2 * KeyLength, 2);

        long usize = 0;
        using (var enc = new AesCtrHmacStream(_out, keys.AsSpan(0, KeyLength).ToArray(), keys.AsSpan(KeyLength, KeyLength).ToArray()))
        {
            Stream sink = store ? enc : new DeflateStream(enc, _level, leaveOpen: true);
            var buffer = new byte[81920];
            int n;
            while ((n = data.Read(buffer, 0, buffer.Length)) > 0)
            {
                ct.ThrowIfCancellationRequested();
                sink.Write(buffer, 0, n);
                usize += n;
                copied?.Invoke(n);
                if (usize >= uint.MaxValue) throw new IOException("A password-protected zip from DORI can't hold a file of 4 GB or more.");
            }
            if (!store) sink.Dispose();   // flushes the last deflate block into the encryptor
            enc.Finish();                 // writes the authentication code
        }

        long end = _out.Position;
        long csize = end - dataStart;
        if (csize >= uint.MaxValue) throw new IOException("A password-protected zip from DORI can't hold a file of 4 GB or more.");
        _out.Position = offset + 14;     // crc (0 in AE-2), compressed and uncompressed sizes
        WriteUInt32(0);
        WriteUInt32((uint)csize);
        WriteUInt32((uint)usize);
        _out.Position = end;
        uint external = unixMode != 0 ? (0x8000u | unixMode) << 16 : 0;
        Add(nameBytes, flags, MethodAes, time, date, csize, usize, offset, external, extra, dir: false);
    }

    private void Add(byte[] name, ushort flags, ushort method, ushort time, ushort date, long csize, long usize, long offset, uint external, byte[] extra, bool dir)
    {
        if (offset >= uint.MaxValue) throw new IOException("A password-protected zip from DORI can't be 4 GB or larger.");
        _central.Add((name, flags, method, time, date, csize, usize, offset, external, extra, dir));
    }

    /// <summary>Writes the central directory; the zip is complete after this.</summary>
    public void Finish()
    {
        if (_finished) return;
        _finished = true;
        long cdStart = _out.Position;
        foreach (var e in _central)
        {
            WriteUInt32(0x02014b50);
            WriteUInt16((ushort)((3 << 8) | (e.Method == MethodAes ? VersionAes : 20)));   // made by Unix
            WriteUInt16(e.Method == MethodAes ? VersionAes : (ushort)20);
            WriteUInt16(e.Flags);
            WriteUInt16(e.Method);
            WriteUInt16(e.Time);
            WriteUInt16(e.Date);
            WriteUInt32(0);
            WriteUInt32((uint)e.CSize);
            WriteUInt32((uint)e.USize);
            WriteUInt16((ushort)e.Name.Length);
            WriteUInt16((ushort)e.Extra.Length);
            WriteUInt16(0);   // comment
            WriteUInt16(0);   // disk
            WriteUInt16(0);   // internal attributes
            WriteUInt32(e.Dir ? (0x4000u | 0b111_101_101u) << 16 | 0x10 : e.External);
            WriteUInt32((uint)e.Offset);
            _out.Write(e.Name);
            _out.Write(e.Extra);
        }
        long cdSize = _out.Position - cdStart;
        WriteUInt32(0x06054b50);
        WriteUInt16(0);
        WriteUInt16(0);
        WriteUInt16((ushort)_central.Count);
        WriteUInt16((ushort)_central.Count);
        WriteUInt32((uint)cdSize);
        WriteUInt32((uint)cdStart);
        WriteUInt16(0);
        _out.Flush();
    }

    public void Dispose() => Finish();

    private void WriteLocalHeader(byte[] name, ushort flags, ushort version, ushort method, ushort time, ushort date, uint csize, uint usize, byte[] extra)
    {
        WriteUInt32(0x04034b50);
        WriteUInt16(version);
        WriteUInt16(flags);
        WriteUInt16(method);
        WriteUInt16(time);
        WriteUInt16(date);
        WriteUInt32(0);
        WriteUInt32(csize);
        WriteUInt32(usize);
        WriteUInt16((ushort)name.Length);
        WriteUInt16((ushort)extra.Length);
        _out.Write(name);
        _out.Write(extra);
    }

    /// <summary>The 0x9901 extra field: AE-2, vendor "AE", AES-256, and the real compression method.</summary>
    private static byte[] AesExtra(ushort realMethod)
        => new byte[] { 0x01, 0x99, 7, 0, 2, 0, (byte)'A', (byte)'E', 3, (byte)realMethod, (byte)(realMethod >> 8) };

    internal static (ushort Time, ushort Date) DosTime(DateTime t)
    {
        if (t.Year < 1980) t = new DateTime(1980, 1, 1);
        if (t.Year > 2107) t = new DateTime(2107, 12, 31, 23, 59, 58);
        return ((ushort)((t.Hour << 11) | (t.Minute << 5) | (t.Second / 2)),
                (ushort)(((t.Year - 1980) << 9) | (t.Month << 5) | t.Day));
    }

    private void WriteUInt16(ushort v) { Span<byte> b = stackalloc byte[2]; BitConverter.TryWriteBytes(b, v); _out.Write(b); }
    private void WriteUInt32(uint v) { Span<byte> b = stackalloc byte[4]; BitConverter.TryWriteBytes(b, v); _out.Write(b); }

    /// <summary>
    /// AES in WinZip's counter mode (a 128-bit little-endian counter starting at 1), with an
    /// HMAC-SHA1 over the encrypted bytes whose first 10 bytes close the entry.
    /// </summary>
    private sealed class AesCtrHmacStream : Stream
    {
        private readonly Stream _inner;
        private readonly ICryptoTransform _aes;
        private readonly IncrementalHash _mac;
        private readonly byte[] _counter = new byte[16];
        private readonly byte[] _keystream = new byte[16];
        private int _used = 16;
        private bool _finished;

        public AesCtrHmacStream(Stream inner, byte[] key, byte[] macKey)
        {
            _inner = inner;
            var aes = Aes.Create();
            aes.Key = key;
            aes.Mode = CipherMode.ECB;
            aes.Padding = PaddingMode.None;
            _aes = aes.CreateEncryptor();
            _mac = IncrementalHash.CreateHMAC(HashAlgorithmName.SHA1, macKey);
        }

        public override void Write(byte[] buffer, int offset, int count) => Write(buffer.AsSpan(offset, count));

        public override void Write(ReadOnlySpan<byte> data)
        {
            var output = new byte[data.Length];
            for (int i = 0; i < data.Length; i++)
            {
                if (_used == 16) NextBlock();
                output[i] = (byte)(data[i] ^ _keystream[_used++]);
            }
            _mac.AppendData(output);
            _inner.Write(output);
        }

        private void NextBlock()
        {
            for (int i = 0; i < 16 && ++_counter[i] == 0; i++) { }
            _aes.TransformBlock(_counter, 0, 16, _keystream, 0);
            _used = 0;
        }

        public void Finish()
        {
            if (_finished) return;
            _finished = true;
            _inner.Write(_mac.GetHashAndReset(), 0, 10);
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing) { _aes.Dispose(); _mac.Dispose(); }
            base.Dispose(disposing);
        }

        public override bool CanRead => false;
        public override bool CanSeek => false;
        public override bool CanWrite => true;
        public override long Length => throw new NotSupportedException();
        public override long Position { get => throw new NotSupportedException(); set => throw new NotSupportedException(); }
        public override void Flush() { }
        public override int Read(byte[] buffer, int offset, int count) => throw new NotSupportedException();
        public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
        public override void SetLength(long value) => throw new NotSupportedException();
    }
}
