using System;
using System.Buffers.Binary;
using System.Collections.Generic;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace DirOpusReImagined.FileSystem.Preview;

/// <summary>
/// Describes audio and video files: title, artist, album, duration, and the stream details worth
/// knowing (sample rate, channels, bitrate).
///
/// <para>The tag formats are read directly rather than through a library. TagLib# would cover more
/// exotic cases, but the four container families that matter here — ID3 on MP3, FLAC metadata
/// blocks, ISO base-media atoms, and RIFF chunks — are each a short, stable, well-specified header,
/// and a preview pane does not justify another dependency in every single-file executable.</para>
///
/// <para>Everything is read from a bounded prefix (plus 128 bytes at the end for a legacy ID3v1
/// tag). Nothing decodes audio, so cost does not scale with the length of the track.</para>
/// </summary>
public sealed class MediaPreviewProvider : IPreviewProvider
{
    /// <summary>How much of the head to read. Tag blocks live at the front; the generous size
    /// covers cover-art frames that push the real tags further in.</summary>
    private const int HeadBytes = 2 * 1024 * 1024;

    /// <summary>Below the document providers, above the byte fallback.</summary>
    public int Priority => 70;

    private static readonly FileSignature.Kind[] Handled =
    {
        FileSignature.Kind.Mp3, FileSignature.Kind.Flac, FileSignature.Kind.Wav,
        FileSignature.Kind.Mp4, FileSignature.Kind.Ogg, FileSignature.Kind.Matroska,
        FileSignature.Kind.Avi,
    };

    public bool CanPreview(PreviewRequest r) => Array.IndexOf(Handled, r.Signature) >= 0;

    public Task<PreviewResult> LoadAsync(PreviewRequest r, CancellationToken ct)
        => Task.Run<PreviewResult>(() =>
        {
            try
            {
                ct.ThrowIfCancellationRequested();

                byte[] head;
                using (var stream = r.Provider.OpenRead(r.Path))
                    head = PreviewText.ReadUpTo(stream, HeadBytes, ct);

                var tags = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
                var stream_ = new List<InfoField>();
                double seconds = 0;

                // Starts from the signature, but a container may be able to name itself more
                // precisely once opened (WebM and Matroska share a magic number).
                string formatName = FileSignature.Describe(r.Signature);

                switch (r.Signature)
                {
                    case FileSignature.Kind.Mp3:
                        ReadId3v2(head, tags);
                        if (tags.Count == 0) ReadId3v1(r, tags, ct);
                        seconds = ReadMp3Stream(head, r.Size, stream_);
                        break;

                    case FileSignature.Kind.Flac:
                        seconds = ReadFlac(head, tags, stream_);
                        break;

                    case FileSignature.Kind.Wav:
                        seconds = ReadWav(head, stream_);
                        break;

                    case FileSignature.Kind.Mp4:
                        seconds = ReadMp4(head, tags, stream_);
                        break;

                    case FileSignature.Kind.Ogg:
                        seconds = ReadOgg(r, head, tags, stream_, ct);
                        break;

                    case FileSignature.Kind.Avi:
                        seconds = ReadAvi(head, stream_);
                        break;

                    case FileSignature.Kind.Matroska:
                        seconds = ReadMatroska(head, tags, stream_, ref formatName);
                        break;
                }

                var fields = new List<InfoField> { new("Format", formatName) };

                // Tags first - they are what someone previewing a track actually wants.
                foreach (string key in new[] { "Title", "Artist", "Album", "Album artist",
                                               "Year", "Track", "Genre", "Comment" })
                {
                    if (tags.TryGetValue(key, out string? v) && !string.IsNullOrWhiteSpace(v))
                        fields.Add(new InfoField(key, Clean(v)));
                }

                if (seconds > 0) fields.Add(new InfoField("Duration", FormatDuration(seconds)));
                fields.AddRange(stream_);
                fields.Add(new InfoField("Size", PreviewText.FormatSize(r.Size)));

                if (tags.Count == 0 && stream_.Count == 0)
                {
                    fields.Add(new InfoField("Details",
                        "No readable tags or stream header found in this file."));
                }

                string subtitle = BuildSubtitle(formatName, tags, seconds);
                return new PreviewResult.Info(r.DisplayName, fields, subtitle);
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (Exception ex)
            {
                return new PreviewResult.Error($"Could not read media file: {ex.Message}");
            }
        }, ct);

    private static string BuildSubtitle(string formatName, Dictionary<string, string> tags, double seconds)
    {
        var parts = new List<string> { formatName };

        if (tags.TryGetValue("Artist", out string? artist) && !string.IsNullOrWhiteSpace(artist))
            parts.Add(Clean(artist));
        if (seconds > 0) parts.Add(FormatDuration(seconds));

        return string.Join(" · ", parts);
    }

    private static string FormatDuration(double seconds)
    {
        var t = TimeSpan.FromSeconds(Math.Round(seconds));
        return t.TotalHours >= 1
            ? $"{(int)t.TotalHours}:{t.Minutes:00}:{t.Seconds:00}"
            : $"{t.Minutes}:{t.Seconds:00}";
    }

    /// <summary>Trims and strips the trailing NULs that fixed-width tag fields are padded with.</summary>
    private static string Clean(string s) => s.Trim().Trim('\0').Trim();

    // ---- ID3v2 (MP3) --------------------------------------------------------------------------

    /// <summary>
    /// Reads an ID3v2.2/2.3/2.4 tag from the head of the file.
    ///
    /// <para>Sizes in the tag header are "syncsafe": seven bits per byte, so a size can never
    /// contain a byte that looks like an MPEG frame sync. Reading them as ordinary big-endian
    /// integers is the classic way to land in the middle of a frame.</para>
    /// </summary>
    private static void ReadId3v2(byte[] d, Dictionary<string, string> tags)
    {
        if (d.Length < 10 || d[0] != 'I' || d[1] != 'D' || d[2] != '3') return;

        int major = d[3];
        int tagSize = SyncSafe(d, 6);
        int frameHeader = major >= 3 ? 10 : 6;
        int nameLength = major >= 3 ? 4 : 3;

        int pos = 10;
        int end = Math.Min(d.Length, 10 + tagSize);

        while (pos + frameHeader <= end)
        {
            string id = Encoding.ASCII.GetString(d, pos, nameLength);
            if (id[0] == '\0') break;                       // padding starts here

            int size = major >= 4 ? SyncSafe(d, pos + 4)
                     : major == 3 ? BinaryPrimitives.ReadInt32BigEndian(d.AsSpan(pos + 4, 4))
                                  : (d[pos + 3] << 16) | (d[pos + 4] << 8) | d[pos + 5];

            pos += frameHeader;
            if (size <= 0 || pos + size > end) break;

            string? label = FrameLabel(id);
            if (label != null && !tags.ContainsKey(label))
            {
                string value = DecodeFrame(d, pos, size);
                if (!string.IsNullOrWhiteSpace(value)) tags[label] = value;
            }

            pos += size;
        }
    }

    private static int SyncSafe(byte[] d, int at) =>
        ((d[at] & 0x7F) << 21) | ((d[at + 1] & 0x7F) << 14) |
        ((d[at + 2] & 0x7F) << 7) | (d[at + 3] & 0x7F);

    private static string? FrameLabel(string id) => id switch
    {
        "TIT2" or "TT2" => "Title",
        "TPE1" or "TP1" => "Artist",
        "TPE2" or "TP2" => "Album artist",
        "TALB" or "TAL" => "Album",
        "TYER" or "TDRC" or "TYE" => "Year",
        "TRCK" or "TRK" => "Track",
        "TCON" or "TCO" => "Genre",
        "COMM" or "COM" => "Comment",
        _ => null,
    };

    /// <summary>Decodes a text frame, honouring the leading encoding byte.</summary>
    private static string DecodeFrame(byte[] d, int at, int size)
    {
        if (size < 1) return "";

        byte encoding = d[at];
        int from = at + 1;
        int length = size - 1;
        if (length <= 0) return "";

        string text = encoding switch
        {
            1 => Encoding.Unicode.GetString(d, from, length),           // UTF-16 with BOM
            2 => Encoding.BigEndianUnicode.GetString(d, from, length),  // UTF-16BE
            3 => Encoding.UTF8.GetString(d, from, length),
            _ => Encoding.Latin1.GetString(d, from, length),            // ISO-8859-1
        };

        // Comment frames carry a language code and a short description before the text; keep the
        // part after the final NUL separator.
        int lastNul = text.LastIndexOf('\0');
        if (lastNul >= 0 && lastNul < text.Length - 1) text = text.Substring(lastNul + 1);

        return Clean(text);
    }

    /// <summary>Legacy 128-byte ID3v1 tag at the very end of the file — the fallback when no
    /// ID3v2 tag is present.</summary>
    private static void ReadId3v1(PreviewRequest r, Dictionary<string, string> tags, CancellationToken ct)
    {
        if (r.Size < 128) return;

        try
        {
            using var stream = r.Provider.OpenRead(r.Path);
            if (!stream.CanSeek) return;

            stream.Position = r.Size - 128;
            byte[] t = PreviewText.ReadUpTo(stream, 128, ct);
            if (t.Length < 128 || t[0] != 'T' || t[1] != 'A' || t[2] != 'G') return;

            string Field(int at, int len) => Clean(Encoding.Latin1.GetString(t, at, len));

            void Set(string key, string value)
            {
                if (!string.IsNullOrWhiteSpace(value)) tags[key] = value;
            }

            Set("Title", Field(3, 30));
            Set("Artist", Field(33, 30));
            Set("Album", Field(63, 30));
            Set("Year", Field(93, 4));
            Set("Comment", Field(97, 30));
        }
        catch
        {
            // A missing legacy tag is the normal case, not a failure.
        }
    }

    // ---- MPEG audio frame header (MP3) --------------------------------------------------------

    private static readonly int[,] BitRates =
    {
        // MPEG1 Layer3, MPEG2/2.5 Layer3 - indexed by the 4-bit bitrate field.
        { 0, 32, 40, 48, 56, 64, 80, 96, 112, 128, 160, 192, 224, 256, 320, 0 },
        { 0, 8, 16, 24, 32, 40, 48, 56, 64, 80, 96, 112, 128, 144, 160, 0 },
    };

    private static readonly int[,] SampleRates =
    {
        { 44100, 48000, 32000, 0 },   // MPEG1
        { 22050, 24000, 16000, 0 },   // MPEG2
        { 11025, 12000, 8000, 0 },    // MPEG2.5
    };

    /// <summary>
    /// Reads the first MPEG audio frame for bitrate and sample rate, and derives a duration.
    ///
    /// <para>A Xing/Info header (written by variable-bitrate encoders) carries the true frame count,
    /// which is the only accurate answer for VBR. Without one, the file is assumed constant-bitrate
    /// and the duration is derived from its size — which is exactly the estimate every player makes.</para>
    /// </summary>
    private static double ReadMp3Stream(byte[] d, long fileSize, List<InfoField> stream)
    {
        int start = 0;

        // Step over an ID3v2 tag if present.
        if (d.Length >= 10 && d[0] == 'I' && d[1] == 'D' && d[2] == '3')
            start = 10 + SyncSafe(d, 6);

        for (int i = start; i + 4 < Math.Min(d.Length, start + 200_000); i++)
        {
            if (d[i] != 0xFF || (d[i + 1] & 0xE0) != 0xE0) continue;

            int versionBits = (d[i + 1] >> 3) & 0x03;    // 0=2.5, 2=2, 3=1
            int layerBits = (d[i + 1] >> 1) & 0x03;      // 1=Layer3
            int bitrateIdx = (d[i + 2] >> 4) & 0x0F;
            int sampleIdx = (d[i + 2] >> 2) & 0x03;

            if (versionBits == 1 || layerBits == 0 || bitrateIdx is 0 or 15 || sampleIdx == 3) continue;

            bool mpeg1 = versionBits == 3;
            int bitrate = BitRates[mpeg1 ? 0 : 1, bitrateIdx];
            int sampleRate = SampleRates[versionBits == 3 ? 0 : versionBits == 2 ? 1 : 2, sampleIdx];
            if (bitrate == 0 || sampleRate == 0) continue;

            int channelMode = (d[i + 3] >> 6) & 0x03;
            string channels = channelMode == 3 ? "Mono" : "Stereo";

            stream.Add(new InfoField("Sample rate", $"{sampleRate:N0} Hz"));
            stream.Add(new InfoField("Channels", channels));

            // Xing/Info sits after the side information, whose length depends on version and mode.
            int xingAt = i + 4 + (mpeg1 ? (channelMode == 3 ? 17 : 32) : (channelMode == 3 ? 9 : 17));
            int frames = ReadXingFrames(d, xingAt, out bool variable);
            int samplesPerFrame = mpeg1 ? 1152 : 576;

            if (frames > 0)
            {
                // Both headers carry a frame count, but they mean opposite things about the
                // bitrate: "Xing" is written by VBR encoders, "Info" by CBR ones. The number
                // shown is the first frame's rate, which is only the whole story for CBR.
                stream.Add(new InfoField("Bitrate",
                    variable ? $"{bitrate} kbps (variable)" : $"{bitrate} kbps (constant)"));
                return (double)frames * samplesPerFrame / sampleRate;
            }

            stream.Add(new InfoField("Bitrate", $"{bitrate} kbps"));
            long audioBytes = Math.Max(0, fileSize - start);
            return bitrate > 0 ? audioBytes * 8.0 / (bitrate * 1000.0) : 0;
        }

        return 0;
    }

    /// <param name="variable">True for a "Xing" header (variable bitrate), false for "Info" (constant).</param>
    private static int ReadXingFrames(byte[] d, int at, out bool variable)
    {
        variable = false;
        if (at < 0 || at + 12 > d.Length) return 0;

        string tag = Encoding.ASCII.GetString(d, at, 4);
        if (tag != "Xing" && tag != "Info") return 0;
        variable = tag == "Xing";

        int flags = BinaryPrimitives.ReadInt32BigEndian(d.AsSpan(at + 4, 4));
        if ((flags & 0x01) == 0) return 0;                       // no frame count present

        return BinaryPrimitives.ReadInt32BigEndian(d.AsSpan(at + 8, 4));
    }

    // ---- FLAC ---------------------------------------------------------------------------------

    /// <summary>Walks FLAC metadata blocks for STREAMINFO (stream details) and VORBIS_COMMENT (tags).</summary>
    private static double ReadFlac(byte[] d, Dictionary<string, string> tags, List<InfoField> stream)
    {
        if (d.Length < 8) return 0;

        int pos = 4;                                             // past "fLaC"
        double seconds = 0;

        while (pos + 4 <= d.Length)
        {
            bool last = (d[pos] & 0x80) != 0;
            int type = d[pos] & 0x7F;
            int size = (d[pos + 1] << 16) | (d[pos + 2] << 8) | d[pos + 3];
            pos += 4;

            if (size < 0 || pos + size > d.Length) break;

            if (type == 0 && size >= 18)
            {
                // Sample rate (20 bits), channels (3), bits per sample (5), total samples (36) are
                // packed across a byte boundary starting at offset 10 of the block.
                int at = pos + 10;
                int sampleRate = (d[at] << 12) | (d[at + 1] << 4) | (d[at + 2] >> 4);
                int channels = ((d[at + 2] >> 1) & 0x07) + 1;
                int bits = (((d[at + 2] & 0x01) << 4) | (d[at + 3] >> 4)) + 1;

                long totalSamples = ((long)(d[at + 3] & 0x0F) << 32) |
                                    ((long)d[at + 4] << 24) | ((long)d[at + 5] << 16) |
                                    ((long)d[at + 6] << 8) | d[at + 7];

                if (sampleRate > 0)
                {
                    stream.Add(new InfoField("Sample rate", $"{sampleRate:N0} Hz"));
                    stream.Add(new InfoField("Channels", channels == 1 ? "Mono" : $"{channels}"));
                    stream.Add(new InfoField("Bit depth", $"{bits}-bit"));
                    seconds = (double)totalSamples / sampleRate;
                }
            }
            else if (type == 4)
            {
                ReadVorbisComments(d, pos, size, tags);
            }

            pos += size;
            if (last) break;
        }

        return seconds;
    }

    /// <summary>Vorbis comments: little-endian lengths, then "KEY=value" strings in UTF-8.</summary>
    private static void ReadVorbisComments(byte[] d, int at, int size, Dictionary<string, string> tags)
    {
        int end = at + size;
        if (at + 4 > end) return;

        int vendorLength = BinaryPrimitives.ReadInt32LittleEndian(d.AsSpan(at, 4));
        int pos = at + 4 + vendorLength;
        if (pos + 4 > end) return;

        int count = BinaryPrimitives.ReadInt32LittleEndian(d.AsSpan(pos, 4));
        pos += 4;

        for (int i = 0; i < count && pos + 4 <= end; i++)
        {
            int length = BinaryPrimitives.ReadInt32LittleEndian(d.AsSpan(pos, 4));
            pos += 4;
            if (length < 0 || pos + length > end) return;

            string entry = Encoding.UTF8.GetString(d, pos, length);
            pos += length;

            int eq = entry.IndexOf('=');
            if (eq <= 0) continue;

            string? label = entry.Substring(0, eq).ToUpperInvariant() switch
            {
                "TITLE" => "Title",
                "ARTIST" => "Artist",
                "ALBUMARTIST" => "Album artist",
                "ALBUM" => "Album",
                "DATE" or "YEAR" => "Year",
                "TRACKNUMBER" => "Track",
                "GENRE" => "Genre",
                "COMMENT" or "DESCRIPTION" => "Comment",
                _ => null,
            };

            if (label != null && !tags.ContainsKey(label)) tags[label] = entry.Substring(eq + 1);
        }
    }

    // ---- Ogg (Vorbis / Opus) ------------------------------------------------------------------

    /// <summary>
    /// Reads an Ogg stream's identification and comment headers.
    ///
    /// <para>Ogg carries the same Vorbis comment structure FLAC does, so the tag parsing is shared.
    /// Duration comes from the granule position on the final page — a sample counter — which means
    /// reading the end of the file rather than decoding any of it.</para>
    /// </summary>
    private static double ReadOgg(PreviewRequest r, byte[] d, Dictionary<string, string> tags,
                                  List<InfoField> stream, CancellationToken ct)
    {
        int sampleRate = 0;
        int channels = 0;

        // Identification header: "\x01vorbis" for Vorbis, "OpusHead" for Opus.
        int vorbisId = IndexOf(d, new byte[] { 0x01, (byte)'v', (byte)'o', (byte)'r', (byte)'b', (byte)'i', (byte)'s' });
        if (vorbisId >= 0 && vorbisId + 16 <= d.Length)
        {
            channels = d[vorbisId + 11];
            sampleRate = BinaryPrimitives.ReadInt32LittleEndian(d.AsSpan(vorbisId + 12, 4));
        }
        else
        {
            int opusId = IndexOf(d, Encoding.ASCII.GetBytes("OpusHead"));
            if (opusId >= 0 && opusId + 16 <= d.Length)
            {
                channels = d[opusId + 9];
                // Opus granule positions are always counted at 48 kHz, whatever the input rate.
                sampleRate = 48000;
            }
        }

        if (sampleRate > 0)
        {
            stream.Add(new InfoField("Sample rate", $"{sampleRate:N0} Hz"));
            if (channels > 0)
                stream.Add(new InfoField("Channels", channels == 1 ? "Mono" : $"{channels}"));
        }

        // Comment header follows the identification header on a later page.
        int comments = IndexOf(d, new byte[] { 0x03, (byte)'v', (byte)'o', (byte)'r', (byte)'b', (byte)'i', (byte)'s' });
        if (comments >= 0)
            ReadVorbisComments(d, comments + 7, d.Length - comments - 7, tags);
        else
        {
            int opusTags = IndexOf(d, Encoding.ASCII.GetBytes("OpusTags"));
            if (opusTags >= 0)
                ReadVorbisComments(d, opusTags + 8, d.Length - opusTags - 8, tags);
        }

        return sampleRate > 0 ? ReadOggDuration(r, sampleRate, ct) : 0;
    }

    /// <summary>Granule position of the last Ogg page, which counts samples played so far.</summary>
    private static double ReadOggDuration(PreviewRequest r, int sampleRate, CancellationToken ct)
    {
        try
        {
            using var stream = r.Provider.OpenRead(r.Path);
            if (!stream.CanSeek) return 0;

            const int TailBytes = 64 * 1024;
            long from = Math.Max(0, r.Size - TailBytes);
            stream.Position = from;
            byte[] tail = PreviewText.ReadUpTo(stream, TailBytes, ct);

            // Scan backwards for the last page header; its granule position is the sample count.
            for (int i = tail.Length - 14; i >= 0; i--)
            {
                if (tail[i] != 'O' || tail[i + 1] != 'g' || tail[i + 2] != 'g' || tail[i + 3] != 'S') continue;

                long granule = BinaryPrimitives.ReadInt64LittleEndian(tail.AsSpan(i + 6, 8));
                if (granule > 0) return (double)granule / sampleRate;
            }
        }
        catch
        {
            // Duration is a bonus; the tags are the point.
        }
        return 0;
    }

    private static int IndexOf(byte[] haystack, byte[] needle)
    {
        for (int i = 0; i + needle.Length <= haystack.Length; i++)
        {
            int j = 0;
            while (j < needle.Length && haystack[i + j] == needle[j]) j++;
            if (j == needle.Length) return i;
        }
        return -1;
    }

    // ---- WAV ----------------------------------------------------------------------------------

    private static double ReadWav(byte[] d, List<InfoField> stream)
    {
        int pos = 12;                                            // past "RIFF" size "WAVE"
        int byteRate = 0;
        double seconds = 0;

        while (pos + 8 <= d.Length)
        {
            string id = Encoding.ASCII.GetString(d, pos, 4);
            int size = BinaryPrimitives.ReadInt32LittleEndian(d.AsSpan(pos + 4, 4));
            int body = pos + 8;
            if (size < 0) break;

            if (id == "fmt " && body + 16 <= d.Length)
            {
                int channels = BinaryPrimitives.ReadInt16LittleEndian(d.AsSpan(body + 2, 2));
                int sampleRate = BinaryPrimitives.ReadInt32LittleEndian(d.AsSpan(body + 4, 4));
                byteRate = BinaryPrimitives.ReadInt32LittleEndian(d.AsSpan(body + 8, 4));
                int bits = BinaryPrimitives.ReadInt16LittleEndian(d.AsSpan(body + 14, 2));

                stream.Add(new InfoField("Sample rate", $"{sampleRate:N0} Hz"));
                stream.Add(new InfoField("Channels", channels == 1 ? "Mono" : $"{channels}"));
                if (bits > 0) stream.Add(new InfoField("Bit depth", $"{bits}-bit"));
                if (byteRate > 0) stream.Add(new InfoField("Bitrate", $"{byteRate * 8 / 1000:N0} kbps"));
            }
            else if (id == "data" && byteRate > 0)
            {
                seconds = (double)size / byteRate;
                break;
            }

            pos = body + size + (size % 2);                      // chunks are word-aligned
        }

        return seconds;
    }

    // ---- Matroska / WebM ----------------------------------------------------------------------

    // EBML element IDs, stored with their length-marker bits intact, as they appear in the file.
    private const long IdEbmlHeader = 0x1A45DFA3, IdDocType = 0x4282;
    private const long IdSegment = 0x18538067, IdInfo = 0x1549A966;
    private const long IdTimecodeScale = 0x2AD7B1, IdDuration = 0x4489, IdTitle = 0x7BA9;
    private const long IdTracks = 0x1654AE6B, IdTrackEntry = 0xAE;
    private const long IdTrackType = 0x83, IdCodecId = 0x86;
    private const long IdVideo = 0xE0, IdPixelWidth = 0xB0, IdPixelHeight = 0xBA;
    private const long IdAudio = 0xE1, IdSamplingFrequency = 0xB5, IdChannels = 0x9F;
    private const long IdTags = 0x1254C367, IdTag = 0x7373, IdSimpleTag = 0x67C8;
    private const long IdTagName = 0x45A3, IdTagString = 0x4487;
    private const long IdCluster = 0x1F43B675;

    /// <summary>
    /// One track's properties, accumulated while its <c>TrackEntry</c> is walked.
    ///
    /// <para>Needed because EBML does not fix the order of a master element's children: encoders
    /// commonly write <c>CodecID</c> before <c>TrackType</c>, so a codec cannot be filed under
    /// "video" or "audio" at the moment it is read. The whole entry is collected first, then
    /// committed once its type is known.</para>
    /// </summary>
    private sealed class MatroskaTrack
    {
        public long Type;
        public string Codec = "";
        public int Width, Height, Channels;
        public double SampleRate;
    }

    /// <summary>
    /// Reads a Matroska (or WebM) file's header elements.
    ///
    /// <para>EBML stores both element IDs and sizes as variable-length integers, so nothing can be
    /// read at a fixed offset the way RIFF and ISO atoms allow — the tree has to be walked. Only
    /// master elements known to hold metadata are descended into; <c>Cluster</c> elements, which
    /// hold the actual media and dwarf everything else, are stepped over entirely.</para>
    /// </summary>
    private static double ReadMatroska(byte[] d, Dictionary<string, string> tags,
                                       List<InfoField> stream, ref string formatName)
    {
        double timecodeScale = 1_000_000;      // nanoseconds per tick; the spec's default
        double durationTicks = 0;
        string docType = "";
        int width = 0, height = 0;
        int channels = 0;
        double sampleRate = 0;
        string? videoCodec = null, audioCodec = null;
        MatroskaTrack? track = null;
        string tagName = "";

        Walk(0, d.Length, 0);

        void Walk(int start, int end, int depth)
        {
            if (depth > 8) return;

            int pos = start;
            while (pos < end)
            {
                long id = ReadId(d, ref pos, end);
                if (id < 0) return;

                long size = ReadVInt(d, ref pos, end, out bool unknownSize);
                if (size < 0) return;

                // An unknown-size master element runs to the end of what we have; that is normal
                // for a streamed Segment and fine to walk into.
                int bodyEnd = unknownSize ? end : (int)Math.Min(end, pos + size);
                if (!unknownSize && pos + size > end && id != IdSegment) return;

                switch (id)
                {
                    case IdEbmlHeader:
                    case IdSegment:
                    case IdInfo:
                    case IdTracks:
                    case IdVideo:
                    case IdAudio:
                    case IdTags:
                    case IdTag:
                    case IdSimpleTag:
                        Walk(pos, bodyEnd, depth + 1);
                        break;

                    case IdTrackEntry:
                    {
                        // Collect the whole entry before deciding what its codec describes.
                        var outer = track;
                        track = new MatroskaTrack();
                        Walk(pos, bodyEnd, depth + 1);

                        if (track.Type == 1)
                        {
                            videoCodec ??= track.Codec;
                            if (track.Width > 0) { width = track.Width; height = track.Height; }
                        }
                        else if (track.Type == 2)
                        {
                            audioCodec ??= track.Codec;
                            if (track.Channels > 0) channels = track.Channels;
                            if (track.SampleRate > 0) sampleRate = track.SampleRate;
                        }

                        track = outer;
                        break;
                    }

                    case IdCluster:
                        return;                 // media data begins; everything wanted precedes it

                    case IdDocType: docType = AsciiAt(d, pos, (int)size); break;
                    case IdTimecodeScale: timecodeScale = UIntAt(d, pos, (int)size); break;
                    case IdDuration: durationTicks = FloatAt(d, pos, (int)size); break;
                    case IdTitle: tags["Title"] = Utf8At(d, pos, (int)size); break;

                    case IdTrackType when track != null: track.Type = UIntAt(d, pos, (int)size); break;
                    case IdPixelWidth when track != null: track.Width = (int)UIntAt(d, pos, (int)size); break;
                    case IdPixelHeight when track != null: track.Height = (int)UIntAt(d, pos, (int)size); break;
                    case IdChannels when track != null: track.Channels = (int)UIntAt(d, pos, (int)size); break;
                    case IdSamplingFrequency when track != null: track.SampleRate = FloatAt(d, pos, (int)size); break;
                    case IdCodecId when track != null: track.Codec = AsciiAt(d, pos, (int)size); break;

                    case IdTagName: tagName = AsciiAt(d, pos, (int)size); break;
                    case IdTagString:
                    {
                        string? label = tagName.ToUpperInvariant() switch
                        {
                            "TITLE" => "Title",
                            "ARTIST" => "Artist",
                            "ALBUM" => "Album",
                            "DATE" or "DATE_RELEASED" => "Year",
                            "GENRE" => "Genre",
                            "COMMENT" or "DESCRIPTION" => "Comment",
                            _ => null,
                        };
                        if (label != null && !tags.ContainsKey(label))
                            tags[label] = Utf8At(d, pos, (int)size);
                        break;
                    }
                }

                if (unknownSize && id != IdSegment && id != IdEbmlHeader) return;
                pos = bodyEnd;
            }
        }

        if (width > 0 && height > 0) stream.Add(new InfoField("Resolution", $"{width} × {height}"));
        if (videoCodec != null) stream.Add(new InfoField("Video", DescribeMatroskaCodec(videoCodec)));

        if (audioCodec != null || sampleRate > 0 || channels > 0)
        {
            var parts = new List<string>();
            if (audioCodec != null) parts.Add(DescribeMatroskaCodec(audioCodec));
            if (sampleRate > 0) parts.Add($"{sampleRate:N0} Hz");
            if (channels > 0) parts.Add(channels == 1 ? "Mono" : $"{channels} channels");
            stream.Add(new InfoField("Audio", string.Join(", ", parts)));
        }

        // Duration is a tick count; the timecode scale says how many nanoseconds a tick is.
        double seconds = durationTicks > 0 ? durationTicks * timecodeScale / 1_000_000_000.0 : 0;

        // WebM and Matroska are the same container with the same magic number; only the DocType
        // separates them, so it refines the format rather than adding a second, contradictory row.
        if (docType.Equals("webm", StringComparison.OrdinalIgnoreCase)) formatName = "WebM video";
        else if (docType.Length > 0) formatName = "Matroska video";

        return seconds;
    }

    /// <summary>Reads an EBML element ID, whose length is signalled by the leading bits of its
    /// first byte. The marker bits are part of the ID and are kept.</summary>
    private static long ReadId(byte[] d, ref int pos, int end)
    {
        if (pos >= end) return -1;

        byte first = d[pos];
        int length = first >= 0x80 ? 1 : first >= 0x40 ? 2 : first >= 0x20 ? 3 : first >= 0x10 ? 4 : 0;
        if (length == 0 || pos + length > end) return -1;

        long id = 0;
        for (int i = 0; i < length; i++) id = (id << 8) | d[pos + i];
        pos += length;
        return id;
    }

    /// <summary>Reads an EBML variable-length integer. Unlike an ID, the marker bit is stripped —
    /// it signals width only. An all-ones value means the size is unknown.</summary>
    private static long ReadVInt(byte[] d, ref int pos, int end, out bool unknown)
    {
        unknown = false;
        if (pos >= end) return -1;

        byte first = d[pos];
        int length = 0;
        for (int i = 0; i < 8; i++)
        {
            if ((first & (0x80 >> i)) != 0) { length = i + 1; break; }
        }
        if (length == 0 || pos + length > end) return -1;

        long value = first & (0xFF >> length);
        long allOnes = (1L << (7 * length)) - 1;

        for (int i = 1; i < length; i++) value = (value << 8) | d[pos + i];
        pos += length;

        if (value == allOnes) { unknown = true; return 0; }
        return value;
    }

    private static long UIntAt(byte[] d, int at, int size)
    {
        if (size <= 0 || at + size > d.Length || size > 8) return 0;
        long v = 0;
        for (int i = 0; i < size; i++) v = (v << 8) | d[at + i];
        return v;
    }

    private static double FloatAt(byte[] d, int at, int size)
    {
        if (at + size > d.Length) return 0;
        return size switch
        {
            4 => BinaryPrimitives.ReadSingleBigEndian(d.AsSpan(at, 4)),
            8 => BinaryPrimitives.ReadDoubleBigEndian(d.AsSpan(at, 8)),
            _ => 0,
        };
    }

    private static string AsciiAt(byte[] d, int at, int size) =>
        size <= 0 || at + size > d.Length ? "" : Encoding.ASCII.GetString(d, at, size).Trim().Trim('\0');

    private static string Utf8At(byte[] d, int at, int size) =>
        size <= 0 || at + size > d.Length ? "" : Encoding.UTF8.GetString(d, at, size).Trim().Trim('\0');

    /// <summary>Matroska codec IDs are descriptive strings; these are the common ones.</summary>
    private static string DescribeMatroskaCodec(string codecId) => codecId switch
    {
        "V_MPEG4/ISO/AVC" => "H.264",
        "V_MPEGH/ISO/HEVC" => "H.265",
        "V_MPEG4/ISO/ASP" or "V_MPEG4/ISO/SP" => "MPEG-4 Part 2",
        "V_MPEG1" => "MPEG-1",
        "V_MPEG2" => "MPEG-2",
        "V_VP8" => "VP8",
        "V_VP9" => "VP9",
        "V_AV1" => "AV1",
        "V_THEORA" => "Theora",
        "A_AAC" or "A_AAC/MPEG4/LC" => "AAC",
        "A_OPUS" => "Opus",
        "A_VORBIS" => "Vorbis",
        "A_FLAC" => "FLAC",
        "A_MPEG/L3" => "MP3",
        "A_MPEG/L2" => "MP2",
        "A_AC3" => "AC-3",
        "A_EAC3" => "E-AC-3",
        "A_DTS" => "DTS",
        _ when codecId.StartsWith("A_PCM", StringComparison.Ordinal) => "PCM",
        _ => codecId,
    };

    // ---- AVI ----------------------------------------------------------------------------------

    /// <summary>
    /// Reads an AVI's header chunks for duration, resolution, frame rate and codecs.
    ///
    /// <para>AVI is RIFF, like WAV, but its headers are nested inside <c>LIST</c> chunks, so this
    /// needs a recursive walk rather than the flat scan a WAV gets. Only the header list is read:
    /// it precedes the <c>movi</c> data chunk, which is the whole film and far larger than the
    /// buffer — the walk stops when a chunk runs past what was read, which is exactly right.</para>
    /// </summary>
    private static double ReadAvi(byte[] d, List<InfoField> stream)
    {
        int microSecPerFrame = 0;
        long totalFrames = 0;
        int width = 0, height = 0;
        string? videoCodec = null;
        string? audio = null;
        string streamType = "";

        Walk(12, d.Length, 0);

        void Walk(int start, int end, int depth)
        {
            if (depth > 5) return;

            int pos = start;
            while (pos + 8 <= end)
            {
                string id = Encoding.ASCII.GetString(d, pos, 4);
                int size = BinaryPrimitives.ReadInt32LittleEndian(d.AsSpan(pos + 4, 4));
                int body = pos + 8;

                // A chunk larger than what was read is the movi payload; everything wanted is
                // already behind us.
                if (size < 0 || body + size > end) return;

                switch (id)
                {
                    case "LIST":
                    case "RIFF":
                        if (body + 4 <= end) Walk(body + 4, body + size, depth + 1);
                        break;

                    case "avih" when body + 40 <= end:
                        microSecPerFrame = BinaryPrimitives.ReadInt32LittleEndian(d.AsSpan(body, 4));
                        totalFrames = (uint)BinaryPrimitives.ReadInt32LittleEndian(d.AsSpan(body + 16, 4));
                        width = BinaryPrimitives.ReadInt32LittleEndian(d.AsSpan(body + 32, 4));
                        height = BinaryPrimitives.ReadInt32LittleEndian(d.AsSpan(body + 36, 4));
                        break;

                    case "strh" when body + 8 <= end:
                        // Records which kind of stream the strf that follows describes.
                        streamType = Encoding.ASCII.GetString(d, body, 4);
                        string handler = FourCc(d, body + 4);
                        if (streamType == "vids" && handler.Length > 0) videoCodec = handler;
                        break;

                    case "strf" when body + 16 <= end:
                        if (streamType == "vids")
                        {
                            // BITMAPINFOHEADER: biCompression sits at offset 16 and is more
                            // reliable than the stream handler, which is often left zero.
                            string compression = FourCc(d, body + 16);
                            if (compression.Length > 0) videoCodec = compression;
                        }
                        else if (streamType == "auds")
                        {
                            int formatTag = BinaryPrimitives.ReadUInt16LittleEndian(d.AsSpan(body, 2));
                            int channels = BinaryPrimitives.ReadUInt16LittleEndian(d.AsSpan(body + 2, 2));
                            int sampleRate = BinaryPrimitives.ReadInt32LittleEndian(d.AsSpan(body + 4, 4));

                            audio = $"{AudioFormatName(formatTag)}, {sampleRate:N0} Hz, " +
                                    (channels == 1 ? "Mono" : $"{channels} channels");
                        }
                        break;
                }

                pos = body + size + (size % 2);          // chunks are word-aligned
            }
        }

        if (width > 0 && height > 0)
            stream.Add(new InfoField("Resolution", $"{width} × {height}"));

        if (microSecPerFrame > 0)
            stream.Add(new InfoField("Frame rate", $"{1_000_000.0 / microSecPerFrame:0.##} fps"));

        if (videoCodec != null) stream.Add(new InfoField("Video", DescribeVideoCodec(videoCodec)));
        if (audio != null) stream.Add(new InfoField("Audio", audio));

        return microSecPerFrame > 0 && totalFrames > 0
            ? totalFrames * microSecPerFrame / 1_000_000.0
            : 0;
    }

    /// <summary>Reads a four-character code, trimming the NUL and space padding it may carry.</summary>
    private static string FourCc(byte[] d, int at)
    {
        if (at + 4 > d.Length) return "";
        string code = Encoding.ASCII.GetString(d, at, 4).Trim().Trim('\0');
        foreach (char c in code)
            if (char.IsControl(c)) return "";
        return code;
    }

    /// <summary>
    /// Puts a familiar name to a video FourCC, keeping the raw code alongside it.
    ///
    /// <para>The code is retained because several encoders write their own for the same codec —
    /// FMP4, DX50, DIVX and XVID are all MPEG-4 Part 2 — and knowing which one produced a file is
    /// occasionally the thing being looked for.</para>
    /// </summary>
    private static string DescribeVideoCodec(string fourCc)
    {
        string name = fourCc.ToUpperInvariant() switch
        {
            "FMP4" or "DIVX" or "DX50" or "XVID" or "MP4V" => "MPEG-4 Part 2",
            "H264" or "AVC1" or "X264" => "H.264",
            "HEVC" or "H265" or "HVC1" => "H.265",
            "MJPG" => "Motion JPEG",
            "MPG1" or "MPEG" => "MPEG-1",
            "MPG2" or "MP2V" => "MPEG-2",
            "WMV1" or "WMV2" or "WMV3" => "Windows Media Video",
            "VP80" => "VP8",
            "VP90" => "VP9",
            "AV01" => "AV1",
            "CVID" => "Cinepak",
            "IV50" => "Indeo 5",
            "DVSD" or "DVC " => "DV",
            "RGB " or "DIB " => "Uncompressed",
            _ => "",
        };

        return name.Length > 0 ? $"{name} ({fourCc})" : fourCc;
    }

    /// <summary>Names the common WAVEFORMATEX format tags; anything else is reported by number.</summary>
    private static string AudioFormatName(int formatTag) => formatTag switch
    {
        0x0001 => "PCM",
        0x0002 => "ADPCM",
        0x0003 => "IEEE float",
        0x0006 => "A-law",
        0x0007 => "µ-law",
        0x0011 => "IMA ADPCM",
        0x0031 or 0x0032 => "GSM 6.10",
        0x0050 => "MPEG audio",
        0x0055 => "MP3",
        0x00FF => "AAC",
        0x2000 => "AC-3",
        0x2001 => "DTS",
        0xF1AC => "FLAC",
        _ => $"format 0x{formatTag:X4}",
    };

    // ---- MP4 / M4A ----------------------------------------------------------------------------

    /// <summary>
    /// Walks the ISO base-media atom tree for <c>mvhd</c> (duration) and the iTunes <c>ilst</c>
    /// metadata atoms.
    /// </summary>
    private static double ReadMp4(byte[] d, Dictionary<string, string> tags, List<InfoField> stream)
    {
        double seconds = 0;
        WalkAtoms(d, 0, d.Length, 0);
        return seconds;

        void WalkAtoms(byte[] buf, int start, int end, int depth)
        {
            if (depth > 6) return;                               // malformed files can nest forever

            int pos = start;
            while (pos + 8 <= end)
            {
                long size = BinaryPrimitives.ReadUInt32BigEndian(buf.AsSpan(pos, 4));
                string type = Encoding.ASCII.GetString(buf, pos + 4, 4);
                int body = pos + 8;

                if (size == 1)
                {
                    if (pos + 16 > end) return;                  // 64-bit size follows the header
                    size = (long)BinaryPrimitives.ReadUInt64BigEndian(buf.AsSpan(pos + 8, 8));
                    body = pos + 16;
                }
                else if (size == 0)
                {
                    size = end - pos;                            // runs to the end of the container
                }

                if (size < 8 || pos + size > end) return;
                int bodyEnd = (int)(pos + size);

                switch (type)
                {
                    case "moov":
                    case "trak":
                    case "mdia":
                    case "udta":
                        WalkAtoms(buf, body, bodyEnd, depth + 1);
                        break;

                    case "meta":
                        // "meta" is a full box: four bytes of version/flags precede its children.
                        WalkAtoms(buf, body + 4, bodyEnd, depth + 1);
                        break;

                    case "ilst":
                        ReadItunesList(buf, body, bodyEnd, tags);
                        break;

                    case "mvhd":
                        ReadMovieHeader(buf, body, bodyEnd, ref seconds, stream);
                        break;
                }

                pos = bodyEnd;
            }
        }
    }

    private static void ReadMovieHeader(byte[] d, int at, int end, ref double seconds, List<InfoField> stream)
    {
        if (at + 4 > end) return;

        int version = d[at];
        long timescale, duration;

        if (version == 1)
        {
            if (at + 28 > end) return;
            timescale = BinaryPrimitives.ReadUInt32BigEndian(d.AsSpan(at + 20, 4));
            duration = (long)BinaryPrimitives.ReadUInt64BigEndian(d.AsSpan(at + 24, 8));
        }
        else
        {
            if (at + 20 > end) return;
            timescale = BinaryPrimitives.ReadUInt32BigEndian(d.AsSpan(at + 12, 4));
            duration = BinaryPrimitives.ReadUInt32BigEndian(d.AsSpan(at + 16, 4));
        }

        // The timescale is only the unit the duration is counted in - an implementation detail,
        // not something worth showing next to the track's own properties.
        if (timescale > 0 && duration > 0) seconds = (double)duration / timescale;
    }

    /// <summary>iTunes metadata: each child atom is named by its tag and holds a "data" atom.</summary>
    private static void ReadItunesList(byte[] d, int at, int end, Dictionary<string, string> tags)
    {
        int pos = at;
        while (pos + 8 <= end)
        {
            long size = BinaryPrimitives.ReadUInt32BigEndian(d.AsSpan(pos, 4));
            string name = Encoding.Latin1.GetString(d, pos + 4, 4);
            if (size < 8 || pos + size > end) return;

            int bodyEnd = (int)(pos + size);

            string? label = name switch
            {
                "©nam" => "Title",
                "©ART" => "Artist",
                "aART" => "Album artist",
                "©alb" => "Album",
                "©day" => "Year",
                "trkn" => "Track",
                "©gen" or "gnre" => "Genre",
                "©cmt" => "Comment",
                _ => null,
            };

            if (label != null && !tags.ContainsKey(label))
            {
                // The value sits in a nested "data" atom: 8-byte header, then 4 bytes of type and
                // 4 of locale before the payload itself.
                int inner = pos + 8;
                if (inner + 16 <= bodyEnd &&
                    Encoding.ASCII.GetString(d, inner + 4, 4) == "data")
                {
                    int valueAt = inner + 16;
                    int valueLength = bodyEnd - valueAt;
                    if (valueLength > 0)
                    {
                        string value = name == "trkn" && valueLength >= 4
                            ? BinaryPrimitives.ReadUInt16BigEndian(d.AsSpan(valueAt + 2, 2)).ToString()
                            : Encoding.UTF8.GetString(d, valueAt, valueLength);

                        if (!string.IsNullOrWhiteSpace(value)) tags[label] = value;
                    }
                }
            }

            pos = bodyEnd;
        }
    }
}
