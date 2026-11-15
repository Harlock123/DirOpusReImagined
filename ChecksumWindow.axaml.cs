using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Media;
using Avalonia.Threading;
using DirOpusReImagined.FileSystem;
using DirOpusReImagined.FileSystem.Archive;

namespace DirOpusReImagined;

/// <summary>
/// Computes checksums for a panel's selection (files, and everything inside selected folders),
/// compares them with a pasted hash, and saves or copies them as a checksum file. Opened on
/// checksum files (.sha256, SHA256SUMS…), it verifies every file they list instead.
/// </summary>
public partial class ChecksumWindow : Window
{
    /// <summary>One line in the results list.</summary>
    public sealed record Row(string Name, string Detail, string Status, IBrush StatusBrush);

    private static readonly IBrush Good = Brushes.SeaGreen;
    private static readonly IBrush Bad = Brushes.IndianRed;
    private static readonly IBrush Neutral = Brushes.Gray;

    private readonly string _folder = "";
    private readonly List<(string Name, bool IsDir)> _selection = new();
    private readonly List<string> _checksumFiles = new();
    private readonly bool _verifyMode;

    private readonly ObservableCollection<Row> _rows = new();
    private readonly List<(string Hash, string Name)> _computed = new();
    private ChecksumAlgorithm _computedWith;
    private CancellationTokenSource? _cts;

    /// <summary>Called after a checksum file is saved, with its folder, so the panel can refresh.</summary>
    public Action<string>? Saved { get; set; }

    public ChecksumWindow()
    {
        InitializeComponent();
    }

    /// <param name="folder">The panel's folder.</param>
    /// <param name="selection">Selected names. If they're all checksum files, the window verifies them.</param>
    public ChecksumWindow(string folder, IReadOnlyList<(string Name, bool IsDir)> selection) : this()
    {
        _folder = folder;
        _selection.AddRange(selection);
        _verifyMode = selection.Count > 0 && selection.All(s => !s.IsDir && Checksums.IsChecksumFileName(s.Name));
        if (_verifyMode) _checksumFiles.AddRange(selection.Select(s => s.Name));

        ResultsList.ItemsSource = _rows;
        ComputeOptions.IsVisible = !_verifyMode;
        SaveButton.IsVisible = !_verifyMode;
        CopyButton.IsVisible = !_verifyMode;
        Title = _verifyMode ? "Verify Checksums" : "Checksums";

        AlgorithmBox.SelectionChanged += (_, _) => { if (IsLoaded) _ = RunAsync(); };
        ExpectedBox.TextChanged += (_, _) => ShowComparison();
        CopyButton.Click += async (_, _) => await CopyAsync();
        SaveButton.Click += async (_, _) => await SaveAsync();
        CloseButton.Click += (_, _) => { if (_cts != null) _cts.Cancel(); else Close(); };
        Closing += (_, _) => _cts?.Cancel();

        Opened += (_, _) => _ = RunAsync();
    }

    protected override void OnKeyDown(KeyEventArgs e)
    {
        if (e.Key == Key.Escape)
        {
            if (_cts != null) _cts.Cancel(); else Close();
            e.Handled = true;
            return;
        }
        base.OnKeyDown(e);
    }

    private ChecksumAlgorithm SelectedAlgorithm => AlgorithmBox.SelectedIndex switch
    {
        0 => ChecksumAlgorithm.MD5,
        1 => ChecksumAlgorithm.SHA1,
        3 => ChecksumAlgorithm.SHA512,
        _ => ChecksumAlgorithm.SHA256,
    };

    private Task RunAsync() => _verifyMode ? VerifyAsync() : ComputeAsync();

    // ---------------- compute ----------------

    private async Task ComputeAsync()
    {
        _cts?.Cancel();
        while (_cts != null) await Task.Delay(20);   // let a previous run wind down

        var algorithm = SelectedAlgorithm;
        _cts = new CancellationTokenSource();
        var ct = _cts.Token;
        _rows.Clear();
        _computed.Clear();
        SetBusy(true, "Finding files…");

        bool anyRemote = ProviderRegistry.For(_folder).IsRemote;
        try
        {
            var files = await Task.Run(() => Checksums.CollectFiles(_folder, _selection, ct), ct);
            HeaderText.Text = $"{Checksums.DisplayName(algorithm)} of {Describe(files.Count)} in {_folder}";
            if (files.Count == 0)
            {
                SetBusy(false, "There are no files in the selection.");
                return;
            }

            long total = files.Sum(f => f.Size), done = 0;
            for (int i = 0; i < files.Count; i++)
            {
                var (path, name, _) = files[i];
                StatusText.Text = $"Reading {name} ({i + 1} of {files.Count})" +
                                  (anyRemote ? " — cloud files are downloaded to be read" : "");
                string? hash = null, error = null;
                try
                {
                    hash = await Task.Run(() => Checksums.Compute(path, algorithm, n =>
                    {
                        long d = Interlocked.Add(ref done, n);
                        if (total > 0) Dispatcher.UIThread.Post(() => Bar.Value = (double)d / total);
                    }, ct), ct);
                }
                catch (OperationCanceledException) { throw; }
                catch (Exception ex) { error = ex.Message; }

                if (hash != null)
                {
                    _computed.Add((hash, name));
                    _rows.Add(new Row(name, hash, "", Neutral));
                }
                else
                {
                    _rows.Add(new Row(name, "Couldn't be read: " + error, "Error", Bad));
                }
            }

            _computedWith = algorithm;
            int failed = files.Count - _computed.Count;
            SetBusy(false, failed == 0 ? $"Done — {Describe(files.Count)}." : $"Done — {failed} of {files.Count} couldn't be read.");
        }
        catch (OperationCanceledException)
        {
            SetBusy(false, "Cancelled.");
        }
        finally
        {
            _cts?.Dispose();
            _cts = null;
            CopyButton.IsEnabled = SaveButton.IsEnabled = _computed.Count > 0;
            if (ProviderRegistry.For(_folder) is ArchiveFileProvider) SaveButton.IsEnabled = false;
        }
        // A hash pasted before (or while) computing is compared now that the results are in.
        ShowComparison();
    }

    /// <summary>Marks the rows whose hash matches the pasted one, and says whether any did.</summary>
    private void ShowComparison()
    {
        if (_verifyMode || _cts != null) return;
        string expected = Checksums.NormalizeHash(ExpectedBox.Text);
        bool typed = !string.IsNullOrWhiteSpace(ExpectedBox.Text);

        for (int i = 0; i < _rows.Count; i++)
        {
            var r = _rows[i];
            if (r.Status == "Error") continue;
            bool match = expected.Length > 0 && Checksums.Matches(r.Detail, expected);
            var updated = r with { Status = match ? "✓ Matches" : "", StatusBrush = match ? Good : Neutral };
            if (updated != r) _rows[i] = updated;
        }

        if (!typed || _computed.Count == 0) return;
        if (expected.Length == 0)
        {
            StatusText.Text = "That doesn't look like an MD5, SHA-1, SHA-256 or SHA-512 hash.";
            return;
        }

        var matched = _computed.Where(c => c.Hash == expected).Select(c => c.Name).ToList();
        var expectedAlg = Checksums.FromHexLength(expected.Length);
        if (matched.Count > 0)
            StatusText.Text = "✓ Matches " + string.Join(", ", matched.Take(3)) + (matched.Count > 3 ? $" and {matched.Count - 3} more" : "");
        else if (expectedAlg != _computedWith)
            StatusText.Text = $"That is a {Checksums.DisplayName(expectedAlg!.Value)} hash — choose {Checksums.DisplayName(expectedAlg.Value)} above to compare.";
        else
            StatusText.Text = "✗ No file matches that hash.";
    }

    private async Task CopyAsync()
    {
        if (Clipboard is { } clipboard)
        {
            await clipboard.SetTextAsync(Checksums.FormatFile(_computed));
            StatusText.Text = $"Copied the checksums of {Describe(_computed.Count)}.";
        }
    }

    private async Task SaveAsync()
    {
        var algorithm = _computedWith;
        string ext = Checksums.Extension(algorithm);
        // One file: "report.pdf.sha256". Several: "SHA256SUMS", the name the sha256sum tools use.
        string wanted = _computed.Count == 1 ? _computed[0].Name.Split('/')[^1] + ext : algorithm.ToString().ToUpperInvariant() + "SUMS";
        var provider = ProviderRegistry.For(_folder);
        try
        {
            string name = await Task.Run(() =>
            {
                string unique = ArchiveCreator.UniqueName(wanted, n =>
                {
                    string p = FileUtility.JoinPanelPath(_folder, n);
                    return provider.FileExists(p) || provider.DirectoryExists(p);
                });
                using var stream = provider.OpenWrite(FileUtility.JoinPanelPath(_folder, unique));
                var bytes = new UTF8Encoding(false).GetBytes(Checksums.FormatFile(_computed));
                stream.Write(bytes, 0, bytes.Length);
                return unique;
            });
            StatusText.Text = $"Saved {name}. Open it later (right-click → Checksums) to check the files haven't changed.";
            Saved?.Invoke(_folder);
        }
        catch (Exception ex)
        {
            StatusText.Text = "Couldn't save the checksum file: " + ex.Message;
        }
    }

    // ---------------- verify ----------------

    private async Task VerifyAsync()
    {
        _cts = new CancellationTokenSource();
        var ct = _cts.Token;
        _rows.Clear();
        SetBusy(true, "Reading the checksum file…");
        HeaderText.Text = $"Verifying {string.Join(", ", _checksumFiles)} in {_folder}";

        int ok = 0, bad = 0, missing = 0, unreadable = 0;
        try
        {
            foreach (var checksumFile in _checksumFiles)
            {
                string path = FileUtility.JoinPanelPath(_folder, checksumFile);
                var entries = await Task.Run(() =>
                {
                    using var s = ProviderRegistry.For(path).OpenRead(path);
                    using var reader = new System.IO.StreamReader(s);
                    return Checksums.Parse(reader.ReadToEnd(), FallbackFor(checksumFile));
                }, ct);

                if (entries.Count == 0)
                {
                    _rows.Add(new Row(checksumFile, "No checksums found in this file.", "—", Neutral));
                    continue;
                }

                int index = 0;
                await Task.Run(() => Checksums.Verify(_folder, entries, result =>
                {
                    int n = Interlocked.Increment(ref index);
                    Dispatcher.UIThread.Post(() =>
                    {
                        _rows.Add(ToRow(result));
                        Bar.Value = (double)n / entries.Count;
                        StatusText.Text = $"Checked {n} of {entries.Count} in {checksumFile}";
                    });
                    switch (result.Status)
                    {
                        case VerifyStatus.Ok: Interlocked.Increment(ref ok); break;
                        case VerifyStatus.Mismatch: Interlocked.Increment(ref bad); break;
                        case VerifyStatus.Missing: Interlocked.Increment(ref missing); break;
                        default: Interlocked.Increment(ref unreadable); break;
                    }
                }, ct: ct), ct);
            }

            var parts = new List<string> { $"{ok} OK" };
            if (bad > 0) parts.Add($"{bad} changed");
            if (missing > 0) parts.Add($"{missing} missing");
            if (unreadable > 0) parts.Add($"{unreadable} unreadable");
            string verdict = bad + missing + unreadable == 0 && ok > 0 ? "✓ All files match. " : "";
            await Dispatcher.UIThread.InvokeAsync(() => { }, DispatcherPriority.Background);   // let queued rows land first
            SetBusy(false, verdict + string.Join(" · ", parts));
        }
        catch (OperationCanceledException)
        {
            SetBusy(false, "Cancelled.");
        }
        catch (Exception ex)
        {
            SetBusy(false, "Couldn't read the checksum file: " + ex.Message);
        }
        finally
        {
            _cts?.Dispose();
            _cts = null;
        }
    }

    /// <summary>The algorithm a checksum file's name implies, used only if its hashes don't say.</summary>
    private static ChecksumAlgorithm? FallbackFor(string name)
    {
        var n = name.ToLowerInvariant();
        foreach (var a in Enum.GetValues<ChecksumAlgorithm>())
            if (n.Contains(a.ToString().ToLowerInvariant())) return a;
        return null;
    }

    private static Row ToRow(VerifyResult r) => r.Status switch
    {
        VerifyStatus.Ok => new Row(r.Entry.Name, r.Entry.Hash, "✓ OK", Good),
        VerifyStatus.Mismatch => new Row(r.Entry.Name, $"expected {r.Entry.Hash}\nactual   {r.ActualHash}", "✗ Changed", Bad),
        VerifyStatus.Missing => new Row(r.Entry.Name, "The file isn't there.", "Missing", Bad),
        _ => new Row(r.Entry.Name, "Couldn't be read: " + r.Error, "Unreadable", Bad),
    };

    // ---------------- helpers ----------------

    private void SetBusy(bool busy, string status)
    {
        Bar.IsVisible = busy;
        if (busy) Bar.Value = 0;
        StatusText.Text = status;
        CloseButton.Content = busy ? "Cancel" : "Close";
        AlgorithmBox.IsEnabled = !busy;
        if (busy) { CopyButton.IsEnabled = false; SaveButton.IsEnabled = false; }
    }

    private static string Describe(int files) => files == 1 ? "1 file" : $"{files:N0} files";
}
