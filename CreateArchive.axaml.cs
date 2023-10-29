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
    
    public CreateArchive()
    {
        InitializeComponent();
        OkButton.Click += OkButton_Click;
        CancelButton.Click += CancelButton_Click;
    }
    
    public CreateArchive(ThePanelSetup panelSetup)
    {
        InitializeComponent();
        _panelSetup = panelSetup;
        
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
        
        FileUtility.PopulateFilePanel(_panelSetup!.PrimaryGrid, _panelSetup!.PrimaryPath);
        
        if (_panelSetup!.PrimaryPath == _panelSetup!.SecondaryPath)
        {
            FileUtility.PopulateFilePanel(_panelSetup!.SecondaryGrid, _panelSetup!.SecondaryPath);
        }
        
        Close();
        
    }

    public void CreateZipArchive()
    {
        string zipFilePath = Path.Combine(_panelSetup!.PrimaryPath, Zipname.Text );

        int i = 1;
        
        while (File.Exists(zipFilePath))
        {
            
            string fname = Path.GetFileNameWithoutExtension(zipFilePath);
            fname = fname + "_" + i.ToString();
            string ext = Path.GetExtension(zipFilePath);
            zipFilePath = Path.Combine(_panelSetup!.PrimaryPath, fname + ext);
            
        }
        // zipFilePath should be unique now

        var zipArchive = ZipFile.Open(zipFilePath, ZipArchiveMode.Create);
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
                DirectoryInfo directoryInfo = new DirectoryInfo(folderPath);
                foreach (FileInfo file in directoryInfo.GetFiles())
                {
                    string entryName = Path.Combine(directoryInfo.Name, file.Name);
                    zipArchive.CreateEntryFromFile(file.FullName, entryName);
                    Console.WriteLine($"Added {entryName} to ZIP archive.");
                }
            }
            Console.WriteLine("Folder added to the ZIP archive successfully.");
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Error adding folder to the ZIP archive: {ex.Message}");
        }
    }

}