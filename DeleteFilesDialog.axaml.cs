using System;
using System.Collections.Generic;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Markup.Xaml;

namespace DirOpusReImagined;

public partial class DeleteFilesDialog : Window
{
    List<Object> FilesToDelete = new List<Object>();
    
    public DeleteFilesDialog()
    {
        InitializeComponent();
    }
    
    public DeleteFilesDialog(List<Object> filesToDelete)
    {
        InitializeComponent();
        
        OKButton.Click += OKButton_Click;
        CANCELButton.Click += CANButton_Click;
        
        FilesToDelete = filesToDelete;

        int f = 0;
        int d = 0;

        foreach (AFileEntry af in FilesToDelete)
        {
            if (af.Typ)
                d += 1;
            else
                f += 1;
           
        }
        
        string message = "Are you sure you want to delete " + f + " files and " + d + " folders?";
        
        TheMessage.Text= message;
        
    }

    private void OKButton_Click(object? sender, RoutedEventArgs e)
    {
        this.Close();
    }

    private void CANButton_Click(object? sender, RoutedEventArgs e)
    {
        this.Close();
    }
}