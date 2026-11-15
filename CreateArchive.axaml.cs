using System;
using System.Collections.Generic;
using System.IO.Compression;
using System.Linq;
using System.Threading;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using DirOpusReImagined.FileSystem;
using DirOpusReImagined.FileSystem.Archive;

namespace DirOpusReImagined;

/// <summary>
/// Asks for an archive name, format and compression level, then writes the archive from the
/// panel's selection with <see cref="ArchiveCreator"/>, showing progress and allowing cancel.
/// <c>ShowDialog&lt;bool&gt;</c> returns true once an archive was written; <see cref="CreatedPath"/>
/// then holds where it went (the name may have been numbered to avoid overwriting a file).
/// </summary>
public partial class CreateArchive : Window
{
    private readonly string _folder = "";
    private readonly List<string> _sources = new();
    private CancellationTokenSource? _cts;
    private bool _running;
    // An archive was written but some items were skipped; the dialog stays open to say so, and
    // closing it still reports success so the panel refreshes.
    private bool _completedWithSkips;

    public string? CreatedPath { get; private set; }

    public CreateArchive()
    {
        InitializeComponent();
    }

    /// <param name="folder">The panel's current folder; the archive is created here.</param>
    /// <param name="selected">The selected rows; each is added under its own name.</param>
    public CreateArchive(string folder, IReadOnlyList<AFileEntry> selected) : this()
    {
        _folder = folder;
        _sources.AddRange(selected.Select(s => FileUtility.JoinPanelPath(folder, s.Name)));

        int dirs = selected.Count(s => s.Typ);
        int files = selected.Count - dirs;
        SummaryText.Text = "Archive " + Describe(files, dirs) + " into this folder.";
        NameBox.Text = DefaultName(folder, selected) + ArchiveCreator.Extension(ArchiveFormat.Zip);

        FormatBox.SelectionChanged += (_, _) => OnFormatChanged();
        OkButton.Click += (_, _) => Start();
        CancelButton.Click += (_, _) => CancelOrClose();
        Closing += (_, e) =>
        {
            // Closing the window mid-run cancels the run; the window closes once it has stopped.
            if (_running) { e.Cancel = true; _cts?.Cancel(); }
        };

        if (ProviderRegistry.For(folder) is ArchiveFileProvider)
        {
            ShowError("This panel is showing the inside of an archive, which is read-only. " +
                      "Move to a normal folder to create an archive.");
            OkButton.IsEnabled = false;
        }

        Opened += (_, _) =>
        {
            NameBox.Focus();
            // Select the name without its extension, ready to be typed over.
            var text = NameBox.Text ?? "";
            NameBox.SelectionStart = 0;
            NameBox.SelectionEnd = text.Length - ArchiveCreator.Extension(SelectedFormat).Length;
        };
    }

    private ArchiveFormat SelectedFormat => FormatBox.SelectedIndex switch
    {
        1 => ArchiveFormat.TarGz,
        2 => ArchiveFormat.Tar,
        _ => ArchiveFormat.Zip,
    };

    private CompressionLevel SelectedLevel => LevelBox.SelectedIndex switch
    {
        1 => CompressionLevel.SmallestSize,
        2 => CompressionLevel.Fastest,
        3 => CompressionLevel.NoCompression,
        _ => CompressionLevel.Optimal,
    };

    private void OnFormatChanged()
    {
        NameBox.Text = ArchiveCreator.WithExtension(NameBox.Text ?? "", SelectedFormat);
        // A plain tar isn't compressed, so there's no level to pick.
        LevelBox.IsEnabled = SelectedFormat != ArchiveFormat.Tar;
    }

    protected override void OnKeyDown(KeyEventArgs e)
    {
        if (e.Key is Key.Enter or Key.Return && !_running) { Start(); e.Handled = true; return; }
        if (e.Key == Key.Escape) { CancelOrClose(); e.Handled = true; return; }
        base.OnKeyDown(e);
    }

    private void CancelOrClose()
    {
        if (_running) { _cts?.Cancel(); CancelButton.IsEnabled = false; ProgressText.Text = "Cancelling…"; }
        else Close(_completedWithSkips);
    }

    private async void Start()
    {
        if (_running || !OkButton.IsEnabled) return;

        string name = (NameBox.Text ?? "").Trim();
        if (ArchiveCreator.ValidateName(name) is { } problem) { ShowError(problem); return; }

        // A name typed with a different archive extension (e.g. ".tgz") picks that format;
        // a name with none gets the selected format's extension.
        var format = ArchiveCreator.FormatFromName(name) ?? SelectedFormat;
        if (ArchiveCreator.FormatFromName(name) == null) name = ArchiveCreator.WithExtension(name, format);

        HideError();
        SetRunning(true);
        _cts = new CancellationTokenSource();

        var progress = new Progress<ArchiveProgress>(p =>
        {
            Bar.Value = p.BytesTotal > 0 ? (double)p.BytesDone / p.BytesTotal : (p.FilesTotal > 0 ? (double)p.FilesDone / p.FilesTotal : 0);
            ProgressText.Text = $"{p.FilesDone:N0} of {p.FilesTotal:N0} files · {p.CurrentEntry}";
        });

        try
        {
            var result = await ArchiveCreator.CreateAsync(_folder, name, _sources, format, SelectedLevel, progress, _cts.Token);
            CreatedPath = result.ArchivePath;
            SetRunning(false);

            if (result.Skipped.Count > 0)
            {
                // Done, but say what was left out rather than closing as if all was well.
                var lines = result.Skipped.Take(8).Select(s => $"• {s.Path}: {s.Reason}");
                string more = result.Skipped.Count > 8 ? $"\n…and {result.Skipped.Count - 8} more." : "";
                ShowError($"Created {System.IO.Path.GetFileName(result.ArchivePath)}, but {result.Skipped.Count} " +
                          $"item(s) couldn't be added:\n{string.Join("\n", lines)}{more}");
                OkButton.IsVisible = false;
                CancelButton.Content = "Close";
                CancelButton.IsEnabled = true;
                _completedWithSkips = true;
                return;
            }
            Close(true);
        }
        catch (OperationCanceledException)
        {
            SetRunning(false);
            Close(false);
        }
        catch (Exception ex)
        {
            SetRunning(false);
            ShowError("The archive wasn't created: " + ex.Message);
        }
        finally
        {
            _cts?.Dispose();
            _cts = null;
        }
    }

    private void SetRunning(bool running)
    {
        _running = running;
        ProgressPanel.IsVisible = running;
        NameBox.IsEnabled = !running;
        FormatBox.IsEnabled = !running;
        LevelBox.IsEnabled = !running && SelectedFormat != ArchiveFormat.Tar;
        OkButton.IsEnabled = !running;
        CancelButton.IsEnabled = true;
        if (running) { Bar.Value = 0; ProgressText.Text = "Reading the selection…"; }
    }

    private void ShowError(string message)
    {
        ErrorText.Text = message;
        ErrorText.IsVisible = true;
    }

    private void HideError()
    {
        ErrorText.Text = "";
        ErrorText.IsVisible = false;
    }

    private static string Describe(int files, int dirs)
    {
        var parts = new List<string>();
        if (files > 0) parts.Add(files == 1 ? "1 file" : $"{files:N0} files");
        if (dirs > 0) parts.Add(dirs == 1 ? "1 folder" : $"{dirs:N0} folders");
        return parts.Count == 0 ? "the selection" : string.Join(" and ", parts);
    }

    /// <summary>
    /// One item: its name (without the extension, for a file). Several: the folder they're in.
    /// </summary>
    private static string DefaultName(string folder, IReadOnlyList<AFileEntry> selected)
    {
        if (selected.Count == 1)
        {
            var only = selected[0];
            string n = only.Typ ? only.Name : System.IO.Path.GetFileNameWithoutExtension(only.Name);
            if (!string.IsNullOrWhiteSpace(n)) return n;
        }

        string leaf = new PanelTab(folder).Title;
        return string.IsNullOrWhiteSpace(leaf) || leaf == "/" || leaf.EndsWith(':') ? "Archive" : leaf;
    }
}
