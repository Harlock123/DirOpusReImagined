using System;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Markup.Xaml;
using Avalonia.Media.Imaging;

namespace DirOpusReImagined;

public partial class ImageViewer : Window
{
    public ImageViewer()
    {
        InitializeComponent();
    }
    
    public ImageViewer(string path)
    {
        try
        {
            InitializeComponent();
            TheImage.Source = new Bitmap(path);

        }
        catch (Exception ex)    
        {
            Console.WriteLine(ex);
            
        }
        
        
    }
}