using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Markup.Xaml;
using Avalonia.Threading;
using DirOpusReImagined.FileSystem;
using DirOpusReImagined.FileSystem.Preview;
using SyntaxColorizer;

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
        ContentBox.TextChanged += (_, _) => MarkDirty();
        SyntaxBox.PropertyChanged += (_, e) => { if (e.Property.Name == "Text") MarkDirty(); };
        KeyDown += (_, e) =>
        {
            if (e.Key == Avalonia.Input.Key.S && (e.KeyModifiers.HasFlag(Avalonia.Input.KeyModifiers.Control) || e.KeyModifiers.HasFlag(Avalonia.Input.KeyModifiers.Meta)))
            {
                if (_editing) SaveButton_Click(null, new RoutedEventArgs());
                e.Handled = true;
            }
        };
        Closing += OnClosingWhileEdited;
        // Tunnel, so the arrows step through files even while the text box has the focus.
        AddHandler(KeyDownEvent, OnNavigationKey, Avalonia.Interactivity.RoutingStrategies.Tunnel);
    }

    // ---- Stepping through the panel's files, the slideshow and the hex editor ----

    private System.Collections.Generic.IReadOnlyList<string> _siblings = Array.Empty<string>();
    private int _index = -1;

    /// <summary>Raised to show the panel's pictures as a slideshow, starting at the given file.</summary>
    public event Action<FileViewer, string>? SlideshowRequested;

    /// <summary>Raised to open the shown file in the hex editor.</summary>
    public event Action<FileViewer, string>? HexEditorRequested;

    /// <summary>Says whether a file (by name) is a picture, for offering the slideshow.</summary>
    public Func<string, bool>? IsPicture { get; set; }

    /// <summary>The panel's files in the order shown, so ◀ ▶ and the arrow keys can step through them.</summary>
    public System.Collections.Generic.IReadOnlyList<string> Siblings
    {
        get => _siblings;
        set
        {
            _siblings = value ?? Array.Empty<string>();
            _index = IndexOf(_path);
            UpdateNavigation();
        }
    }

    private int IndexOf(string path)
    {
        for (int i = 0; i < _siblings.Count; i++)
            if (string.Equals(_siblings[i], path, OperatingSystem.IsLinux() ? StringComparison.Ordinal : StringComparison.OrdinalIgnoreCase))
                return i;
        return -1;
    }

    private void UpdateNavigation()
    {
        bool nav = !FollowSelection && _siblings.Count > 1 && _index >= 0;
        PrevButton.IsVisible = NextButton.IsVisible = PositionText.IsVisible = nav;
        if (nav)
        {
            PrevButton.IsEnabled = _index > 0;
            NextButton.IsEnabled = _index < _siblings.Count - 1;
            PositionText.Text = $"{_index + 1} / {_siblings.Count}";
        }
        SlideshowButton.IsVisible = !FollowSelection && IsPicture != null && _siblings.Any(p => IsPicture(LeafName(p)));
        HexEditorButton.IsVisible = !FollowSelection && _path.Length > 0;
    }

    internal static string LeafName(string path)
    {
        string p = path.TrimEnd('/', '\\');
        int cut = Math.Max(p.LastIndexOf('/'), p.LastIndexOf('\\'));
        return cut >= 0 ? p[(cut + 1)..] : p;
    }

    /// <summary>Shows the file <paramref name="delta"/> places on (or back). False at either end, or while an edit isn't saved.</summary>
    public bool Step(int delta)
    {
        if (_siblings.Count < 2 || _index < 0) return false;
        int next = _index + delta;
        if (next < 0 || next >= _siblings.Count) return false;
        if (_dirty)
        {
            StatusText.Text = "Save or undo the changes first (Ctrl/Cmd+S saves).";
            return false;
        }
        _index = next;
        LoadFrom(_siblings[next], LeafName(_siblings[next]));
        UpdateNavigation();
        return true;
    }

    private void PrevButton_Click(object? sender, RoutedEventArgs e) => Step(-1);
    private void NextButton_Click(object? sender, RoutedEventArgs e) => Step(+1);
    private void SlideshowButton_Click(object? sender, RoutedEventArgs e) => SlideshowRequested?.Invoke(this, _path);
    private void HexEditorButton_Click(object? sender, RoutedEventArgs e)
    {
        if (_dirty) { StatusText.Text = "Save the changes first."; return; }
        HexEditorRequested?.Invoke(this, _path);
    }

    private void OnNavigationKey(object? sender, Avalonia.Input.KeyEventArgs e)
    {
        if (FollowSelection) return;
        var mods = e.KeyModifiers;
        bool alt = mods == Avalonia.Input.KeyModifiers.Alt;
        // Plain arrows step only where they wouldn't move a caret: a picture, or details with nothing to edit.
        bool plain = mods == Avalonia.Input.KeyModifiers.None && !_editing && PreviewImage.IsVisible;
        switch (e.Key)
        {
            case Avalonia.Input.Key.Left when alt || plain:
                Step(-1); e.Handled = true; break;
            case Avalonia.Input.Key.Right when alt || plain:
                Step(+1); e.Handled = true; break;
            case Avalonia.Input.Key.F5 when SlideshowButton.IsVisible:
                SlideshowRequested?.Invoke(this, _path); e.Handled = true; break;
        }
    }

    // ---- Quick edit: small text files can be changed and saved back through their provider ----

    private string _path = "";
    private bool _editing, _dirty, _closingAfterAsk;
    private System.Text.Encoding _encoding = new System.Text.UTF8Encoding(false);
    private bool _crlf;

    /// <summary>Raised after a save with the file's path, so panels showing its folder can refresh.</summary>
    public event Action<string>? Saved;

    /// <summary>Whole, small text files can be edited, unless they are in a 7z or RAR archive (which can't be written).</summary>
    private bool CanEdit(PreviewResult r)
    {
        if (FollowSelection || r is not PreviewResult.Bytes { IsBinary: false, Truncated: false }) return false;
        if (DirOpusReImagined.FileSystem.Archive.ArchivePath.IsArchiveUri(_path) &&
            DirOpusReImagined.FileSystem.Archive.ArchiveEditor.WhyReadOnly(DirOpusReImagined.FileSystem.Archive.ArchivePath.Parse(_path).ArchiveFsPath) != null)
            return false;
        return true;
    }

    private void EditButton_Click(object? sender, RoutedEventArgs e)
    {
        if (_result is not PreviewResult.Bytes b) return;
        // Keep what the file had: its byte-order mark (if any) and its line endings.
        var d = b.Data;
        _encoding = d.Length >= 3 && d[0] == 0xEF && d[1] == 0xBB && d[2] == 0xBF ? new System.Text.UTF8Encoding(true)
                  : d.Length >= 2 && d[0] == 0xFF && d[1] == 0xFE ? System.Text.Encoding.Unicode
                  : d.Length >= 2 && d[0] == 0xFE && d[1] == 0xFF ? System.Text.Encoding.BigEndianUnicode
                  : new System.Text.UTF8Encoding(false);
        _crlf = PreviewText.DecodeText(d).Contains("\r\n");
        _editing = true;
        _dirty = false;
        if (_hexMode) { _hexMode = false; Render(); }
        ContentBox.IsReadOnly = false;
        SyntaxBox.IsReadOnly = false;
        ModeButton.IsEnabled = false;
        EditButton.IsVisible = false;
        SaveButton.IsVisible = true;
        SaveButton.IsEnabled = false;
        StatusText.Text = "editing · Ctrl/Cmd+S saves";
        (SyntaxBox.IsVisible ? (Avalonia.Input.InputElement)SyntaxBox : ContentBox).Focus();
    }

    private void MarkDirty()
    {
        if (!_editing || _dirty) return;
        _dirty = true;
        SaveButton.IsEnabled = true;
        Title = TitlePrefix + _displayName + " •";
    }

    private string EditedText()
    {
        string text = (SyntaxBox.IsVisible ? SyntaxBox.Text : ContentBox.Text) ?? "";
        text = text.Replace("\r\n", "\n");
        return _crlf ? text.Replace("\n", "\r\n") : text;
    }

    private async void SaveButton_Click(object? sender, RoutedEventArgs? e) => await SaveAsync();

    private async Task<bool> SaveAsync()
    {
        if (!_editing) return true;
        string path = _path;
        byte[] bytes = _encoding.GetPreamble().Concat(_encoding.GetBytes(EditedText())).ToArray();
        StatusText.Text = "saving…";
        try
        {
            await Task.Run(() =>
            {
                using var stream = ProviderRegistry.For(path).OpenWrite(path);
                stream.Write(bytes, 0, bytes.Length);
            });
        }
        catch (Exception ex)
        {
            StatusText.Text = "not saved";
            await new MessageBox($"{_displayName} couldn't be saved: {ex.Message}", "Save").ShowDialog(this);
            return false;
        }
        _dirty = false;
        SaveButton.IsEnabled = false;
        Title = TitlePrefix + _displayName;
        StatusText.Text = $"saved {DateTime.Now:t} · {PreviewText.FormatSize(bytes.Length)}";
        Saved?.Invoke(path);
        return true;
    }

    /// <summary>Closing with changes not saved asks first.</summary>
    private async void OnClosingWhileEdited(object? sender, WindowClosingEventArgs e)
    {
        if (!_dirty || _closingAfterAsk) return;
        e.Cancel = true;
        bool save = await new MessageBox($"Save the changes to {_displayName}?", true, "Save", "Don't Save", "Edited file").ShowDialog<bool>(this);
        if (save && !await SaveAsync()) return;
        _closingAfterAsk = true;
        Close();
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
        _path = path;
        _editing = _dirty = false;
        ContentBox.IsReadOnly = true;
        SyntaxBox.IsReadOnly = true;
        SaveButton.IsVisible = false;
        _displayName = displayName;
        Title = TitlePrefix + displayName;
        InfoText.Text = displayName;
        StatusText.Text = "reading…";
        if (_siblings.Count > 0) _index = IndexOf(path);
        UpdateNavigation();

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
        EditButton.IsVisible = CanEdit(result);

        if (result is not PreviewResult.Image) PreviewImage.Source = null;

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
                string size = b.Truncated
                    ? $"first {MaxBytes / 1024} KB of {PreviewText.FormatSize(b.TotalBytes)} (truncated)"
                    : PreviewText.FormatSize(b.TotalBytes);

                if (_hexMode)
                {
                    ShowSurface(Surface.PlainText);
                    ContentBox.Text = PreviewText.BuildHex(b.Data);
                    StatusText.Text = $"hex · {size}";
                    break;
                }

                string text = PreviewText.DecodeText(b.Data);
                var language = SyntaxMapping.ForFileName(_displayName);

                // Highlight only when the language is known and the file is small enough that
                // tokenising it on every cursor move stays cheap; otherwise plain text.
                bool highlight = language != SyntaxLanguage.None
                                 && b.Data.Length <= SyntaxMapping.MaxHighlightBytes;

                if (highlight)
                {
                    ShowSurface(Surface.Highlighted);
                    SyntaxBox.SyntaxTheme = SyntaxMapping.ForTheme(ThemeManager.Current);
                    SyntaxBox.Language = language;
                    SyntaxBox.Text = text;
                    StatusText.Text = $"{language} · {size}";
                }
                else
                {
                    ShowSurface(Surface.PlainText);
                    ContentBox.Text = text;
                    StatusText.Text = $"text · {size}";
                }
                break;
            }

            case PreviewResult.Image img:
            {
                ShowSurface(Surface.Image);
                PreviewImage.Source = img.Bitmap;
                string dims = $"{img.SourceWidth} × {img.SourceHeight}";
                string scaled = img.Scaled ? " · scaled to fit" : "";
                StatusText.Text = $"{img.Format} · {dims} · {PreviewText.FormatSize(img.TotalBytes)}{scaled}";
                ShowPhotoDetails(img.Photo);
                break;
            }

            case PreviewResult.Text t:
            {
                ShowSurface(Surface.PlainText);
                ContentBox.Text = t.Content;
                // Prose wants wrapping; a spreadsheet grid does not. The file's own bytes are not
                // on offer here, so the Hex toggle stays disabled (Apply did that already).
                ContentBox.TextWrapping = t.Wrap
                    ? Avalonia.Media.TextWrapping.Wrap
                    : Avalonia.Media.TextWrapping.NoWrap;
                WrapCheck.IsChecked = t.Wrap;
                WrapCheck.IsEnabled = true;
                StatusText.Text = t.Subtitle;
                break;
            }

            case PreviewResult.Info info:
            {
                ShowSurface(Surface.PlainText);
                var sb = new System.Text.StringBuilder();
                // Pad labels to the longest, but only up to a point: an info card that lists file
                // paths would otherwise indent every value by the length of the deepest path.
                int width = 0;
                foreach (var f in info.Fields) width = Math.Max(width, f.Label.Length);
                width = Math.Min(width, 32);
                foreach (var f in info.Fields) sb.Append(f.Label.PadRight(width)).Append("  ").Append(f.Value).Append('\n');
                ContentBox.Text = sb.ToString();
                InfoText.Text = info.Title;
                StatusText.Text = string.IsNullOrEmpty(info.Subtitle)
                    ? $"{info.Fields.Count} field(s)"
                    : info.Subtitle;
                break;
            }

            case PreviewResult.Message m:
                ShowSurface(Surface.PlainText);
                ContentBox.Text = m.Detail;
                InfoText.Text = m.Header;
                StatusText.Text = "";
                break;

            case PreviewResult.Error e:
                ShowSurface(Surface.PlainText);
                ContentBox.Text = "Could not preview this file:\n\n" + e.Reason;
                StatusText.Text = "error";
                break;

            default:
                ShowSurface(Surface.PlainText);
                ContentBox.Text = "";
                StatusText.Text = "";
                break;
        }
    }

    /// <summary>The camera, exposure, date and place under a photo, or nothing for an image without EXIF.</summary>
    private void ShowPhotoDetails(PhotoDetails? photo)
    {
        var fields = photo?.Fields.Where(f => f.Label != "Orientation").ToList();
        PhotoBar.IsVisible = fields is { Count: > 0 };
        PhotoText.Text = fields is { Count: > 0 } ? string.Join("   ·   ", fields.Select(f => $"{f.Label}: {f.Value}")) : "";
    }

    /// <summary>The three mutually exclusive content surfaces the window can show.</summary>
    private enum Surface { PlainText, Highlighted, Image }

    /// <summary>
    /// Shows one content surface and hides the others. Clearing the hidden ones matters: a stale
    /// bitmap or a large highlighted document left behind would keep its memory alive for as long
    /// as the window stays open, which for the follow-mode preview is the whole session.
    /// </summary>
    private void ShowSurface(Surface surface)
    {
        Scroller.IsVisible = surface == Surface.PlainText;
        SyntaxBox.IsVisible = surface == Surface.Highlighted;
        PreviewImage.IsVisible = surface == Surface.Image;

        if (surface != Surface.Highlighted) SyntaxBox.Text = "";
        if (surface != Surface.Image)
        {
            PreviewImage.Source = null;
            PhotoBar.IsVisible = false;
        }

        // Wrap applies to the plain text box only; hex and highlighted code are laid out by column.
        // Extracted document text manages its own wrapping in Render().
        if (_result is not PreviewResult.Text)
            WrapCheck.IsEnabled = surface == Surface.PlainText && _result is PreviewResult.Bytes;
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
