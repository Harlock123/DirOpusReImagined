using System;
using System.Collections.Generic;
using System.Drawing.Imaging;
using System.IO;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Documents;
using Avalonia.Interactivity;
using Avalonia.Markup.Xaml;
using Avalonia.Threading;
using DirOpusReImagined.FileSystem;

namespace DirOpusReImagined;

public partial class DeleteFilesDialog : Window
{
    private List<Object> FilesToDelete = new List<Object>();
    private string RootPath = "";
    private string OtherRootPath = "";
    private TaiDataGrid ThePanel = null!;
    private TaiDataGrid OtherPanel = null!;
    private bool _ShowHidden = true;
    
    public DeleteFilesDialog()
    {
        InitializeComponent();
    }
    
    public DeleteFilesDialog(List<Object> filesToDelete, 
        string rootPath, TaiDataGrid thepanel,
        string otherrootPath,TaiDataGrid otherpanel,
        bool ShowHidden)
    {
        InitializeComponent();
        
        OKButton.Click += OKButton_Click;
        CANCELButton.Click += CANButton_Click;

        TrashCheck.IsChecked = AppOptions.UseTrash;   // default from the saved setting

        FilesToDelete = filesToDelete;
        RootPath = rootPath;
        ThePanel = thepanel;
        OtherRootPath = otherrootPath;
        OtherPanel = otherpanel;
        _ShowHidden = ShowHidden;

        int f = 0;
        int d = 0;

        foreach (AFileEntry af in FilesToDelete)
        {
            if (af.Typ)
                d += 1;
            else
                f += 1;
        }
        
        //string message = "Are you sure you want to delete " + f + " files and " + d + " folders?";
        //TheMessage.Text= message;

        if (TheMessage.Inlines != null)
            foreach (Run r in TheMessage.Inlines)
            {
                if (r.Text.Contains("%ORD%"))
                {
                    r.Text = f.ToString();
                }
                else if (r.Text.Contains("%NAME%"))
                {
                    r.Text = d.ToString();
                }
            }
    }

    // async void: an exception escaping here goes straight to the dispatcher and (before CrashLog)
    // took the process down with no dialog. Wrap the whole body so a fault is always reportable.
    private async void OKButton_Click(object? sender, RoutedEventArgs e)
    {
        try { await DeleteSelectedAsync(); }
        catch (Exception ex)
        {
            CrashLog.Write("Delete", ex);
            OKButton.IsEnabled = true;
            CANCELButton.IsEnabled = true;
            try
            {
                await new MessageBox(
                    $"The delete could not be completed:\n\n{ex.GetType().Name}: {ex.Message}\n\n" +
                    $"Details were written to:\n{CrashLog.LogPath}",
                    "Delete failed").ShowDialog(this);
            }
            catch { }
            Close();
        }
    }

    private async Task DeleteSelectedAsync()
    {
        // They said OK so lets delete the files

        bool useTrash = TrashCheck.IsChecked == true;
        AppOptions.UseTrash = useTrash;   // remember the choice for next time (persisted on app close)

        var targets = new List<(string Path, bool IsDir)>();
        foreach (AFileEntry af in FilesToDelete)
            targets.Add((FileUtility.JoinPanelPath(RootPath, af.Name), af.Typ));
        if (targets.Count == 0) { Close(); return; }

        // A network location has no Recycle Bin, so "recoverable" is not on offer there. Say so and
        // get an explicit go-ahead rather than silently turning a recoverable delete into a
        // permanent one — or worse, grinding through per-item shell calls that are going to fail.
        if (useTrash && !TrashService.IsSupported(targets[0].Path))
        {
            var confirm = new MessageBox(
                "These items are on a network location, which has no Recycle Bin.\n\n" +
                "They can only be deleted permanently — this cannot be undone.\n\nContinue?",
                showCancel: true, okText: "Delete Permanently", cancelText: "Cancel",
                title: "No Recycle Bin here");
            if (!await confirm.ShowDialog<bool>(this)) return;
            useTrash = false;
        }

        // Enqueue the delete on the shared operations queue and show the (non-modal) operations
        // window. The dialog closes immediately so the UI is never blocked, the deletes run off the
        // UI thread one batch at a time, and any failures are shown inline in the operations window.
        string what = targets.Count == 1 ? "1 item" : $"{targets.Count} items";
        string title = $"Deleting {what} from {RootPath.TrimEnd('/', '\\')}";
        var op = FileOperation.DeleteBatch(targets, useTrash, title);

        var owner = this.Owner as Window;
        OperationQueue.Instance.Enqueue(op);
        if (owner != null) OperationsWindow.ShowSingleton(owner);

        // Refresh the affected panel(s) when the delete finishes. Captured locals survive this
        // dialog closing; the grids belong to the main window and persist.
        var panel = ThePanel; var root = RootPath;
        var otherPanel = OtherPanel; var otherRoot = OtherRootPath; var showHidden = _ShowHidden;
        _ = op.Completion.ContinueWith(_ =>
            Dispatcher.UIThread.Post(() =>
            {
                FileUtility.PopulateFilePanel(panel, root, showHidden);
                if (otherRoot == root)
                    FileUtility.PopulateFilePanel(otherPanel, otherRoot, showHidden);
                if (op.Status == OperationStatus.Failed && owner != null)
                    OperationsWindow.ShowSingleton(owner);
            }), TaskScheduler.Default);

        this.Close();
    }

    private void CANButton_Click(object? sender, RoutedEventArgs e)
    {
        // They said CANCEL so lets not delete the files
        this.Close();
    }
}