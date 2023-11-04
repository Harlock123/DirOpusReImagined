using System.IO;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Markup.Xaml;

namespace DirOpusReImagined;

public partial class CreateFolder : Window
{
    private ThePanelSetup? _panelSetup;
    private bool _ShowHidden = true;
    
    public CreateFolder()
    {
        InitializeComponent();
        OkButton.Click += OkButton_Click;
        CancelButton.Click += CancelButton_Click;
    }

    

    public CreateFolder(ThePanelSetup panelSetup, bool ShowHidden)
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
        CreateFolderMethod();
        
        FileUtility.PopulateFilePanel(_panelSetup!.PrimaryGrid, _panelSetup!.PrimaryPath, _ShowHidden);
        
        if (_panelSetup!.PrimaryPath == _panelSetup!.SecondaryPath)
        {
            FileUtility.PopulateFilePanel(_panelSetup!.SecondaryGrid, _panelSetup!.SecondaryPath,_ShowHidden);
        }
        
        Close();
    }

    private void CreateFolderMethod()
    {
        int i = 1;
        
        string basename = Foldername.Text;
        
        while(Directory.Exists(Path.Combine(_panelSetup!.PrimaryPath, basename)))
        {
            basename = Foldername.Text + "_" + i.ToString();
            i++;
        }
        string folderPath = Path.Combine(_panelSetup!.PrimaryPath, basename);

        Directory.CreateDirectory(folderPath);
        
        FileUtility.PopulateFilePanel(_panelSetup!.PrimaryGrid, _panelSetup!.PrimaryPath, _ShowHidden);
        
        if (_panelSetup!.PrimaryPath == _panelSetup!.SecondaryPath)
        {
            FileUtility.PopulateFilePanel(_panelSetup!.SecondaryGrid, _panelSetup!.SecondaryPath,_ShowHidden);
        }

        Close();
    }
}