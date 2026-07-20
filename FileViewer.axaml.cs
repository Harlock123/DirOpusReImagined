using System;
using System.IO;
using System.Text;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Markup.Xaml;
using DirOpusReImagined.FileSystem;

namespace DirOpusReImagined;

/// <summary>
/// Read-only text/hex file viewer. Reads bytes through <see cref="ProviderRegistry"/>, so it works
/// uniformly for local files and files inside archives (archive://…!/…). The content is sniffed to
/// pick Text or Hex initially; a toggle switches modes. Large files are capped so the UI stays
/// responsive.
/// </summary>
public partial class FileViewer : Window
{
    // Cap how much we read/display so a huge file can't hang the viewer.
    private const int MaxBytes = 256 * 1024;

    private byte[] _bytes = Array.Empty<byte>();
    private bool _truncated;
    private bool _hexMode;
    private string _displayName = "";

    public FileViewer()
    {
        InitializeComponent();
    }

    /// <param name="path">A provider path — a normal filesystem path or an archive:// URI.</param>
    /// <param name="displayName">Friendly name shown in the title/header.</param>
    public FileViewer(string path, string displayName) : this()
    {
        _displayName = displayName;
        Title = "View — " + displayName;

        try
        {
            _bytes = ReadCapped(path, out _truncated);
        }
        catch (Exception ex)
        {
            InfoText.Text = displayName;
            ContentBox.Text = "Could not read file:\n\n" + ex.Message;
            ModeButton.IsEnabled = false;
            return;
        }

        _hexMode = LooksBinary(_bytes);
        Render();
    }

    private static byte[] ReadCapped(string path, out bool truncated)
    {
        var provider = ProviderRegistry.For(path);
        using var stream = provider.OpenRead(path);
        using var ms = new MemoryStream();

        var buffer = new byte[64 * 1024];
        int total = 0, read;
        while (total < MaxBytes + 1 && (read = stream.Read(buffer, 0, buffer.Length)) > 0)
        {
            ms.Write(buffer, 0, read);
            total += read;
        }

        var all = ms.ToArray();
        truncated = all.Length > MaxBytes;
        if (truncated)
        {
            var trimmed = new byte[MaxBytes];
            Array.Copy(all, trimmed, MaxBytes);
            return trimmed;
        }
        return all;
    }

    /// <summary>
    /// Heuristic: a file is treated as binary if it contains NUL bytes or a high fraction of
    /// non-printable, non-whitespace bytes in the sampled region.
    /// </summary>
    private static bool LooksBinary(byte[] data)
    {
        if (data.Length == 0) return false;
        int sample = Math.Min(data.Length, 8192);
        int suspicious = 0;
        for (int i = 0; i < sample; i++)
        {
            byte b = data[i];
            if (b == 0) return true;                       // NUL → definitely binary
            bool printable = b >= 0x20 && b < 0x7F;
            bool ws = b == 0x09 || b == 0x0A || b == 0x0D || b == 0x0C || b == 0x08;
            if (!printable && !ws && b < 0x80) suspicious++; // control chars (ignore high/UTF-8 bytes)
        }
        return suspicious > sample / 10;                   // >10% control chars → binary
    }

    private void Render()
    {
        ModeButton.Content = _hexMode ? "Text" : "Hex";   // button shows the OTHER mode
        ContentBox.Text = _hexMode ? BuildHex(_bytes) : DecodeText(_bytes, out _);

        string enc = _hexMode ? "hex" : "text";
        string size = _truncated ? $"first {MaxBytes / 1024} KB (truncated)" : $"{_bytes.Length:N0} bytes";
        InfoText.Text = _displayName;
        StatusText.Text = $"{enc} · {size}";
    }

    private static string DecodeText(byte[] data, out Encoding encoding)
    {
        // Honor a BOM if present; otherwise decode as UTF-8 (invalid sequences shown as U+FFFD).
        if (data.Length >= 3 && data[0] == 0xEF && data[1] == 0xBB && data[2] == 0xBF)
        { encoding = Encoding.UTF8; return new UTF8Encoding(false, false).GetString(data, 3, data.Length - 3); }
        if (data.Length >= 2 && data[0] == 0xFF && data[1] == 0xFE)
        { encoding = Encoding.Unicode; return Encoding.Unicode.GetString(data, 2, data.Length - 2); }
        if (data.Length >= 2 && data[0] == 0xFE && data[1] == 0xFF)
        { encoding = Encoding.BigEndianUnicode; return Encoding.BigEndianUnicode.GetString(data, 2, data.Length - 2); }

        encoding = Encoding.UTF8;
        return new UTF8Encoding(false, false).GetString(data);
    }

    private static string BuildHex(byte[] data)
    {
        var sb = new StringBuilder(data.Length * 4);
        var ascii = new StringBuilder(16);
        for (int i = 0; i < data.Length; i += 16)
        {
            sb.Append(i.ToString("X8")).Append("  ");
            ascii.Clear();
            for (int j = 0; j < 16; j++)
            {
                if (i + j < data.Length)
                {
                    byte b = data[i + j];
                    sb.Append(b.ToString("X2")).Append(' ');
                    ascii.Append(b >= 0x20 && b < 0x7F ? (char)b : '.');
                }
                else
                {
                    sb.Append("   ");
                }
                if (j == 7) sb.Append(' ');                 // gap between the two 8-byte halves
            }
            sb.Append(' ').Append(ascii).Append('\n');
        }
        return sb.ToString();
    }

    private void ModeButton_Click(object? sender, RoutedEventArgs e)
    {
        _hexMode = !_hexMode;
        Render();
    }

    private void WrapCheck_Changed(object? sender, RoutedEventArgs e)
    {
        // Wrapping only makes sense in text mode; hex is fixed-width.
        ContentBox.TextWrapping = (WrapCheck.IsChecked == true && !_hexMode)
            ? Avalonia.Media.TextWrapping.Wrap
            : Avalonia.Media.TextWrapping.NoWrap;
    }

    private void DismissButton_Click(object? sender, RoutedEventArgs e) => Close();

    protected override void OnKeyDown(Avalonia.Input.KeyEventArgs e)
    {
        if (e.Key == Avalonia.Input.Key.Escape) { Close(); e.Handled = true; return; }
        base.OnKeyDown(e);
    }
}
