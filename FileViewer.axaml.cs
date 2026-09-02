using System;
using System.Threading;
using System.Threading.Tasks;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Markup.Xaml;
using Avalonia.Threading;
using DirOpusReImagined.FileSystem.Preview;

namespace DirOpusReImagined;

/// <summary>
/// Read-only file viewer. Content comes from <see cref="PreviewRegistry"/>, so it renders whatever
/// the registered providers can produce — text, a hex dump, or a decoded image — and picks up new
/// formats as providers are added, without changes here.
///
/// <para>Serves two roles: the one-shot F3 viewer, and (with <see cref="FollowSelection"/>) a live
/// preview window that is re-pointed at each file as the panel cursor moves.</para>
/// </summary>
public partial class FileViewer : Window
{
    /// <summary>How much of a file a text/hex preview reads. Enough to be useful, small enough that
    /// a multi-gigabyte file cannot stall the window.</summary>
    private const int MaxBytes = 256 * 1024;

    private PreviewResult? _result;
    private bool _hexMode;
    private string _displayName = "";

    /// <summary>Cancels an in-flight load when the window is re-pointed at another file.</summary>
    private CancellationTokenSource? _loadCts;

    /// <summary>Increments per load so a slow read that finishes late cannot paint over a newer
    /// file's content. Compared on the UI thread before anything is rendered.</summary>
    private int _loadId;

    /// <summary>
    /// When true this window is a live preview following the panel cursor rather than a one-shot
    /// view of a single file. Affects presentation only; the host drives which file is shown.
    /// </summary>
    public bool FollowSelection { get; init; }

    public FileViewer()
    {
        InitializeComponent();
    }

    /// <param name="path">A provider path — a filesystem path, an archive:// URI, or a cloud path.</param>
    /// <param name="displayName">Friendly name shown in the title and header.</param>
    public FileViewer(string path, string displayName) : this()
    {
        LoadFrom(path, displayName);
    }

    /// <summary>
    /// Points this window at a file, replacing whatever it was showing. Returns immediately; the
    /// read happens on a background thread and the result is applied when it arrives.
    /// </summary>
    public void LoadFrom(string path, string displayName)
    {
        _displayName = displayName;
        Title = TitlePrefix + displayName;
        InfoText.Text = displayName;
        StatusText.Text = "reading…";

        _loadCts?.Cancel();
        _loadCts = new CancellationTokenSource();

        var cts = _loadCts;
        int id = ++_loadId;

        _ = LoadAsyncCore(path, displayName, id, cts.Token);
    }

    private async Task LoadAsyncCore(string path, string displayName, int id, CancellationToken ct)
    {
        PreviewResult result;
        try
        {
            result = await PreviewRegistry.LoadAsync(path, displayName, MaxBytes, ct)
                                          .ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            return;                       // superseded by a newer file; leave the window alone
        }
        catch (Exception ex)
        {
            result = new PreviewResult.Error(ex.Message);
        }

        await Dispatcher.UIThread.InvokeAsync(() =>
        {
            if (id != _loadId) return;    // a newer load won the race
            Apply(result);
        });
    }

    /// <summary>
    /// Shows an explanatory message instead of file content — nothing selected, a folder under the
    /// cursor, or a file deliberately not read (a remote one).
    /// </summary>
    public void ShowMessage(string header, string detail = "")
    {
        _loadCts?.Cancel();
        _loadId++;                        // discard anything still in flight

        _displayName = header;
        Title = TitlePrefix + header;
        Apply(new PreviewResult.Message(header, detail));
    }

    private string TitlePrefix => FollowSelection ? "Preview — " : "View — ";

    /// <summary>Renders a result, switching the window between its text and image surfaces.</summary>
    private void Apply(PreviewResult result)
    {
        _result = result;

        // Hex only means something for a byte preview; start binary content in hex, text in text.
        _hexMode = result is PreviewResult.Bytes { IsBinary: true };
        ModeButton.IsEnabled = result is PreviewResult.Bytes;
        WrapCheck.IsEnabled = result is PreviewResult.Bytes;

        bool isImage = result is PreviewResult.Image;
        PreviewImage.IsVisible = isImage;
        Scroller.IsVisible = !isImage;

        if (!isImage) PreviewImage.Source = null;

        Render();
    }

    private void Render()
    {
        InfoText.Text = _displayName;
        ModeButton.Content = _hexMode ? "Text" : "Hex";   // the button offers the OTHER mode

        switch (_result)
        {
            case PreviewResult.Bytes b:
            {
                ContentBox.Text = _hexMode ? PreviewText.BuildHex(b.Data) : PreviewText.DecodeText(b.Data);
                string size = b.Truncated
                    ? $"first {MaxBytes / 1024} KB of {PreviewText.FormatSize(b.TotalBytes)} (truncated)"
                    : PreviewText.FormatSize(b.TotalBytes);
                StatusText.Text = $"{(_hexMode ? "hex" : "text")} · {size}";
                break;
            }

            case PreviewResult.Image img:
            {
                PreviewImage.Source = img.Bitmap;
                string dims = $"{img.SourceWidth} × {img.SourceHeight}";
                string scaled = img.Scaled ? " · scaled to fit" : "";
                StatusText.Text = $"{img.Format} · {dims} · {PreviewText.FormatSize(img.TotalBytes)}{scaled}";
                break;
            }

            case PreviewResult.Info info:
            {
                var sb = new System.Text.StringBuilder();
                int width = 0;
                foreach (var f in info.Fields) width = Math.Max(width, f.Label.Length);
                foreach (var f in info.Fields) sb.Append(f.Label.PadRight(width)).Append("  ").Append(f.Value).Append('\n');
                ContentBox.Text = sb.ToString();
                InfoText.Text = info.Title;
                StatusText.Text = $"{info.Fields.Count} field(s)";
                break;
            }

            case PreviewResult.Message m:
                ContentBox.Text = m.Detail;
                InfoText.Text = m.Header;
                StatusText.Text = "";
                break;

            case PreviewResult.Error e:
                ContentBox.Text = "Could not preview this file:\n\n" + e.Reason;
                StatusText.Text = "error";
                break;

            default:
                ContentBox.Text = "";
                StatusText.Text = "";
                break;
        }
    }

    private void ModeButton_Click(object? sender, RoutedEventArgs e)
    {
        if (_result is not PreviewResult.Bytes) return;
        _hexMode = !_hexMode;
        Render();
    }

    private void WrapCheck_Changed(object? sender, RoutedEventArgs e)
    {
        // Wrapping only makes sense for text; hex is fixed-width by construction.
        ContentBox.TextWrapping = (WrapCheck.IsChecked == true && !_hexMode)
            ? Avalonia.Media.TextWrapping.Wrap
            : Avalonia.Media.TextWrapping.NoWrap;
    }

    private void DismissButton_Click(object? sender, RoutedEventArgs e) => Close();

    protected override void OnClosed(EventArgs e)
    {
        _loadCts?.Cancel();
        base.OnClosed(e);
    }

    protected override void OnKeyDown(Avalonia.Input.KeyEventArgs e)
    {
        if (e.Key == Avalonia.Input.Key.Escape) { Close(); e.Handled = true; return; }
        base.OnKeyDown(e);
    }
}
