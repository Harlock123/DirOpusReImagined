using System;
using System.Collections.Concurrent;
using System.Collections.ObjectModel;
using System.Threading;
using System.Threading.Tasks;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Threading;
using DirOpusReImagined.FileSystem;

namespace DirOpusReImagined;

/// <summary>
/// Non-modal recursive search window. Searches a root folder by name and, optionally, by text
/// inside files, off-thread with a live status line and Cancel. Matches appear as they are found;
/// double-clicking one asks the owner to show it in the originating panel.
/// </summary>
public partial class SearchWindow : Window
{
    private readonly string _root;
    private readonly Action<SearchHit>? _onNavigate;
    private CancellationTokenSource? _cts;

    private readonly ObservableCollection<SearchHit> _results = new();
    private readonly ConcurrentQueue<SearchHit> _pending = new();

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
        resultsList.ItemsSource = _results;

        // Reading cloud files means downloading them, so the option only appears for a cloud folder.
        cloudContentsCheck.IsVisible = !string.IsNullOrEmpty(_root) && ProviderRegistry.For(_root).IsRemote;

        // Folders have no contents to search, so the folder option only applies to a name search.
        textBox.TextChanged += (_, _) => UpdateOptionStates();
        UpdateOptionStates();

        findButton.Click += async (_, _) => await RunSearchAsync();
        cancelButton.Click += (_, _) => _cts?.Cancel();
        patternBox.KeyDown += async (_, e) => { if (e.Key == Key.Enter) await RunSearchAsync(); };
        textBox.KeyDown += async (_, e) => { if (e.Key == Key.Enter) await RunSearchAsync(); };
        resultsList.DoubleTapped += (_, _) => NavigateToSelected();
        resultsList.KeyDown += (_, e) =>
        {
            if (e.Key is Key.Enter or Key.Return) { NavigateToSelected(); e.Handled = true; }
        };
        Closing += (_, _) => _cts?.Cancel();
    }

    protected override void OnOpened(EventArgs e)
    {
        base.OnOpened(e);
        patternBox.Focus();
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

    private bool IsTextSearch => !string.IsNullOrEmpty(textBox.Text);

    private void UpdateOptionStates()
    {
        bool running = _cts != null;
        includeFoldersCheck.IsEnabled = !running && !IsTextSearch;
        regexCheck.IsEnabled = !running && IsTextSearch;
        cloudContentsCheck.IsEnabled = !running && IsTextSearch;
        matchCaseCheck.IsEnabled = !running;
        patternBox.IsEnabled = !running;
        textBox.IsEnabled = !running;
        findButton.IsEnabled = !running;
        cancelButton.IsEnabled = running;
    }

    private void NavigateToSelected()
    {
        if (resultsList.SelectedItem is SearchHit hit) _onNavigate?.Invoke(hit);
    }

    private async Task RunSearchAsync()
    {
        if (_cts != null) return; // a search is already running

        string pattern = patternBox.Text ?? "";
        string text = textBox.Text ?? "";
        if (string.IsNullOrWhiteSpace(pattern) && text.Length == 0)
        {
            statusText.Text = "Enter a name pattern, text to look for, or both.";
            return;
        }

        var opt = new SearchOptions(
            MatchCase: matchCaseCheck.IsChecked == true,
            IncludeFolders: includeFoldersCheck.IsChecked == true,
            ContainingText: text.Length > 0 ? text : null,
            TextIsRegex: regexCheck.IsChecked == true,
            SearchRemoteContents: cloudContentsCheck.IsVisible && cloudContentsCheck.IsChecked == true);

        // Check a regular expression before starting, so a typo is reported straight away.
        if (opt.IsTextSearch && opt.TextIsRegex)
        {
            try { FileSearch.MakeTextMatcher(text, isRegex: true, opt.MatchCase); }
            catch (ArgumentException ex)
            {
                statusText.Text = "That isn't a valid regular expression: " + ex.Message;
                return;
            }
        }

        _cts = new CancellationTokenSource();
        var ct = _cts.Token;
        _results.Clear();
        while (_pending.TryDequeue(out _)) { }
        UpdateOptionStates();
        statusText.Text = "Searching…";

        var stats = new SearchStats();
        string currentFolder = _root;
        var progress = new Progress<string>(folder => currentFolder = folder);

        // The search thread queues matches; this timer moves them into the list a few times a
        // second, so results appear as they're found without a UI update per match.
        var flush = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(200) };
        flush.Tick += (_, _) =>
        {
            Drain();
            statusText.Text = $"{_results.Count:N0} found — scanning {Shorten(currentFolder)}";
        };
        flush.Start();

        bool canceled = false;
        string? error = null;
        try
        {
            await Task.Run(() => FileSearch.Search(_root, pattern, opt, _pending.Enqueue, ct, progress, stats), ct);
        }
        catch (OperationCanceledException) { canceled = true; }
        catch (Exception ex) { error = ex.Message; }

        flush.Stop();
        Drain();
        _cts.Dispose();
        _cts = null;
        UpdateOptionStates();

        if (error != null)
        {
            statusText.Text = "Error: " + error;
            return;
        }

        string summary = (canceled ? "Cancelled — " : "") + $"{_results.Count:N0} result(s)";
        string detail = opt.IsTextSearch ? stats.Describe() : "";
        statusText.Text = detail.Length > 0 ? $"{summary} · {detail}" : summary;
    }

    private void Drain()
    {
        while (_pending.TryDequeue(out var hit)) _results.Add(hit);
    }

    private static string Shorten(string p) => p.Length > 64 ? "…" + p.Substring(p.Length - 64) : p;
}
