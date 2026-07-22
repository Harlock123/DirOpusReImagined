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

    private async void OKButton_Click(object? sender, RoutedEventArgs e)
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

        OKButton.IsEnabled = false;
        CANCELButton.IsEnabled = false;

        // Off the UI thread: each delete is a blocking filesystem call, and on a network share a
        // whole selection's worth of them froze the window until Windows declared it unresponsive.
        var errors = await Task.Run(() =>
        {
            var failures = new List<string>();
            foreach (var (path, isDir) in targets)
            {
                string? err = isDir
                    ? FileUtility.TryDeleteFolder(path, useTrash)
                    : FileUtility.TryDeleteFile(path, useTrash);
                if (err != null) failures.Add($"{Path.GetFileName(path.TrimEnd('/', '\\'))}: {err}");
            }
            return failures;
        });

        FileUtility.PopulateFilePanel(ThePanel, RootPath,_ShowHidden);
        if (OtherRootPath == RootPath)
            FileUtility.PopulateFilePanel(OtherPanel, OtherRootPath,_ShowHidden);

        // One message for the whole batch, awaited — never one modal per failed item.
        if (errors.Count > 0)
        {
            const int maxShown = 10;
            string detail = string.Join("\n", errors.GetRange(0, Math.Min(maxShown, errors.Count)));
            if (errors.Count > maxShown) detail += $"\n… and {errors.Count - maxShown} more.";
            await new MessageBox(
                $"{errors.Count} of {targets.Count} item(s) could not be deleted:\n\n{detail}",
                "Delete failed").ShowDialog(this);
        }

        this.Close();
    }

    private void CANButton_Click(object? sender, RoutedEventArgs e)
    {
        // They said CANCEL so lets not delete the files
        this.Close();
    }
}