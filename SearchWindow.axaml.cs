using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Avalonia.Controls;
using Avalonia.Input;

namespace DirOpusReImagined;

/// <summary>
/// Non-modal recursive search window. Searches a root folder for a name pattern off-thread (with a
/// live count, current-folder status, and Cancel) and lists matches; double-clicking a result asks
/// the owner to navigate the originating panel to the match's folder.
/// </summary>
public partial class SearchWindow : Window
{
    private readonly string _root;
    private readonly Action<SearchHit>? _onNavigate;
    private CancellationTokenSource? _cts;

    public SearchWindow()
    {
        InitializeComponent();
        _root = "";
    }

    public SearchWindow(string root, Action<SearchHit> onNavigate)
    {
        InitializeComponent();
        _root = root ?? "";
        _onNavigate = onNavigate;

        rootText.Text = "Search in: " + _root;
        includeFoldersCheck.IsChecked = true;

        findButton.Click += async (_, _) => await RunSearchAsync();
        cancelButton.Click += (_, _) => _cts?.Cancel();
        patternBox.KeyDown += async (_, e) => { if (e.Key == Key.Enter) await RunSearchAsync(); };
        resultsList.DoubleTapped += (_, _) =>
        {
            if (resultsList.SelectedItem is SearchHit hit) _onNavigate?.Invoke(hit);
        };
    }

    protected override void OnOpened(EventArgs e)
    {
        base.OnOpened(e);
        patternBox.Focus();
    }

    private async Task RunSearchAsync()
    {
        if (_cts != null) return; // a search is already running

        string pattern = patternBox.Text ?? "";
        if (string.IsNullOrWhiteSpace(pattern)) return;

        var opt = new SearchOptions(matchCaseCheck.IsChecked == true, includeFoldersCheck.IsChecked == true);
        _cts = new CancellationTokenSource();
        var ct = _cts.Token;

        resultsList.ItemsSource = null;
        findButton.IsEnabled = false;
        patternBox.IsEnabled = false;
        cancelButton.IsEnabled = true;
        statusText.Text = "Searching…";

        // Single-threaded recursion writes hits; the UI only reads the count for the status line.
        var hits = new List<SearchHit>();
        int found = 0;
        Action<SearchHit> onHit = h => { hits.Add(h); Interlocked.Increment(ref found); };
        var progress = new Progress<string>(folder => statusText.Text = $"Found {found} — scanning {Shorten(folder)}");

        bool canceled = false;
        string? error = null;
        try
        {
            await Task.Run(() => FileSearch.Search(_root, pattern, opt, onHit, ct, progress));
        }
        catch (OperationCanceledException) { canceled = true; }
        catch (Exception ex) { error = ex.Message; }

        resultsList.ItemsSource = hits;
        findButton.IsEnabled = true;
        patternBox.IsEnabled = true;
        cancelButton.IsEnabled = false;
        _cts.Dispose();
        _cts = null;

        statusText.Text = error != null
            ? "Error: " + error
            : (canceled ? "Cancelled — " : "") + $"{hits.Count} result(s)";
    }

    private static string Shorten(string p) => p.Length > 64 ? "…" + p.Substring(p.Length - 64) : p;
}
