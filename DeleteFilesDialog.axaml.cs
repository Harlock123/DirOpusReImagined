using System;
using System.Collections.Generic;
using System.Drawing.Imaging;
using System.IO;
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
    private TaiDataGrid ThePanel = null;
    private TaiDataGrid OtherPanel = null;
    
    public DeleteFilesDialog()
    {
        InitializeComponent();
    }
    
    public DeleteFilesDialog(List<Object> filesToDelete, 
        string rootPath, TaiDataGrid thepanel,
        string otherrootPath,TaiDataGrid otherpanel)
    {
        InitializeComponent();
        
        OKButton.Click += OKButton_Click;
        CANCELButton.Click += CANButton_Click;
        
        FilesToDelete = filesToDelete;
        RootPath = rootPath;
        ThePanel = thepanel;
        OtherRootPath = otherrootPath;
        OtherPanel = otherpanel;

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

    private void OKButton_Click(object? sender, RoutedEventArgs e)
    {
        // They said OK so lets delete the files

        foreach (AFileEntry af in FilesToDelete)
        {
            if (af.Typ)
            {
                // This is a folder
                
                
                FileUtility.DeleteFolder(Path.Combine(RootPath, af.Name));
            }
            else
            {
                // This is a file
                FileUtility.DeleteFile(Path.Combine(RootPath, af.Name));
            }
            
        }
        
        FileUtility.PopulateFilePanel(ThePanel, RootPath);
        if (OtherRootPath == RootPath)
            FileUtility.PopulateFilePanel(OtherPanel, OtherRootPath);
        
        
        this.Close();
    }

    private void CANButton_Click(object? sender, RoutedEventArgs e)
    {
        // They said CANCEL so lets not delete the files
        this.Close();
    }
}