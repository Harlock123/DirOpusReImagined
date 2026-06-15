using System;
using System.Threading;
using Avalonia.Controls;
using Avalonia.Interactivity;

namespace DirOpusReImagined;

/// <summary>
/// Modal "comparing…" dialog: runs the (recursive, possibly content/cloud) compare off-thread behind
/// an indeterminate progress bar with a Cancel button and a live "current folder" line. The caller
/// awaits <c>ShowDialog</c>, then reads <see cref="Result"/> / <see cref="Canceled"/> / <see cref="Error"/>.
/// </summary>
public partial class CompareBusyDialog : Window
{
    private readonly string _left;
    private readonly string _right;
    private readonly bool _recursive;
    private readonly bool _content;
    private readonly CancellationTokenSource _cts = new();
    private bool _finished;

    public DirectoryComparer.CompareResult? Result { get; private set; }
    public bool Canceled { get; private set; }
    public Exception? Error { get; private set; }

    public CompareBusyDialog()
    {
        InitializeComponent();
        _left = "";
        _right = "";
    }

    public CompareBusyDialog(string left, string right, bool recursive, bool content)
    {
        InitializeComponent();
        _left = left;
        _right = right;
        _recursive = recursive;
        _content = content;

        headerText.Text = content ? "Comparing by content (hash)…" : "Comparing panels…";
        cancelButton.Click += (_, _) =>
        {
            _cts.Cancel();
            cancelButton.IsEnabled = false;
            statusText.Text = "Cancelling…";
        };
    }

    protected override async void OnOpened(EventArgs e)
    {
        base.OnOpened(e);

        // Progress captures this UI thread's context, so the status updates on the UI thread.
        var progress = new Progress<string>(name => statusText.Text = "Comparing: " + name);
        try
        {
            Result = await System.Threading.Tasks.Task.Run(() =>
                DirectoryComparer.Compare(_left, _right, _recursive, _content, _cts.Token, progress));
        }
        catch (OperationCanceledException) { Canceled = true; }
        catch (Exception ex) { Error = ex; }
        finally
        {
            _finished = true;
            Close();
        }
    }

    protected override void OnClosing(WindowClosingEventArgs e)
    {
        if (!_finished) _cts.Cancel(); // closed via the title bar before completion
        base.OnClosing(e);
    }

    protected override void OnClosed(EventArgs e)
    {
        base.OnClosed(e);
        _cts.Dispose();
    }
}
