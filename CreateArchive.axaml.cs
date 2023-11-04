using System;
using System.IO;
using System.IO.Compression;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Markup.Xaml;

namespace DirOpusReImagined;

public partial class CreateArchive : Window
{
    private ThePanelSetup? _panelSetup;
    private bool _ShowHidden = true;
    
    public CreateArchive()
    {
        InitializeComponent();
        OkButton.Click += OkButton_Click;
        CancelButton.Click += CancelButton_Click;
    }
    
    public CreateArchive(ThePanelSetup panelSetup, bool ShowHidden)
    {
        InitializeComponent();
        _panelSetup = panelSetup;
        _ShowHidden = ShowHidden;
        
        OkButton.Click += OkButton_Click;
        CancelButton.Click += CancelButton_Click;   
    }
    
    private void CancelButton_Click(object? sender, RoutedEventArgs e)
    {
        Close();
    }

    private void OkButton_Click(object? sender, RoutedEventArgs e)
    {
        CreateZipArchive();
        
        FileUtility.PopulateFilePanel(_panelSetup!.PrimaryGrid, _panelSetup!.PrimaryPath, _ShowHidden);
        
        if (_panelSetup!.PrimaryPath == _panelSetup!.SecondaryPath)
        {
            FileUtility.PopulateFilePanel(_panelSetup!.SecondaryGrid, _panelSetup!.SecondaryPath,_ShowHidden);
        }
        
        Close();
        
    }

    public void CreateZipArchive()
    {
        string zipFilePath = Path.Combine(_panelSetup!.PrimaryPath, Zipname.Text );

        int i = 1;
        
        while (File.Exists(zipFilePath))
        {
            string TempFilePath = Path.Combine(_panelSetup!.PrimaryPath, Zipname.Text);
            string fname = Path.GetFileNameWithoutExtension(TempFilePath);
            fname = fname + "_" + i.ToString();
            string ext = Path.GetExtension(TempFilePath);
            zipFilePath = Path.Combine(_panelSetup!.PrimaryPath, fname + ext);
            i += 1;

        }
        // zipFilePath should be unique now

        var zipArchive = ZipFile.Open(zipFilePath, ZipArchiveMode.Create);
        
        foreach (var item in _panelSetup!.PrimaryGrid.SelectedItems)
        {
            if (item is AFileEntry fileItem)
            {
                if (fileItem.Typ)
                {
                    // This particular Item is a Folder
                    string entryName = Path.GetFileName(fileItem.Name);
                    string entryPath = Path.Combine(_panelSetup!.PrimaryPath, entryName);
                }
                else
                {
                    // This particular Item is a File
                    string entryName = Path.GetFileName(fileItem.Name);
                    string entryPath = Path.Combine(_panelSetup!.PrimaryPath, entryName);
                    
                    zipArchive.CreateEntryFromFile(entryPath, entryName);
                    
                    Console.WriteLine($"Added {entryName} to ZIP archive.");
                }
                
            }
        }

        foreach (var item in _panelSetup!.PrimaryGrid.SelectedItems)
        {
            if (item is AFileEntry fileItem)
            {
                if (fileItem.Typ)
                {
                    // This particular Item is a Folder
                    string entryName = Path.GetFileName(fileItem.Name);
                    string entryPath = Path.Combine(_panelSetup!.PrimaryPath, entryName);
                    
                    AddFolderToZipRecursive(zipArchive,entryPath ,entryName);
                    
                    Console.WriteLine($"Added {entryName} to ZIP archive.");
                }
                
            }

        }

        zipArchive.Dispose();
        
        
    }
    
    public static void UpdateZipArchive(string zipFilePath, params string[] filesToAdd)
    {
        try
        {
            using (var zipArchive = ZipFile.Open(zipFilePath, ZipArchiveMode.Update))
            {
                foreach (string fileToAdd in filesToAdd)
                {
                    if (File.Exists(fileToAdd))
                    {
                        string entryName = Path.GetFileName(fileToAdd);
                        zipArchive.CreateEntryFromFile(fileToAdd, entryName);
                        Console.WriteLine($"Added {entryName} to ZIP archive.");
                    }
                    else
                    {
                        Console.WriteLine($"File not found: {fileToAdd}");
                    }
                }
            }
            Console.WriteLine("Files added to the ZIP archive successfully.");
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Error adding files to the ZIP archive: {ex.Message}");
        }
    }
    
    public static void AddFolderToZipArchive(string zipFilePath, string folderPath)
    {
        try
        {
            using (var zipArchive = ZipFile.Open(zipFilePath, ZipArchiveMode.Update))
            {
                AddFolderToZipRecursive(zipArchive, folderPath, string.Empty);
            }
            Console.WriteLine("Folder and its contents added to the ZIP archive successfully.");
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Error adding folder to the ZIP archive: {ex.Message}");
        }
    }
    
    private static void AddFolderToZipRecursive(ZipArchive zipArchive, string folderPath, string relativePath)
    {
        foreach (var file in Directory.GetFiles(folderPath))
        {
            string entryName = Path.Combine(relativePath, Path.GetFileName(file));
            zipArchive.CreateEntryFromFile(file, entryName);
            Console.WriteLine($"Added {entryName} to ZIP archive.");
        }

        foreach (var subdirectory in Directory.GetDirectories(folderPath))
        {
            string subdirectoryName = Path.GetFileName(subdirectory);
            string subdirectoryRelativePath = Path.Combine(relativePath, subdirectoryName);
            AddFolderToZipRecursive(zipArchive, subdirectory, subdirectoryRelativePath);
        }
    }

}