using Avalonia.Controls;
using Avalonia.Interactivity;

namespace DirOpusReImagined;

public partial class RenameFileInterface : Window
{
    
    bool FrmCanceled;
    
    public string NewName { get; set; }
    public string newprefix { get; set; }
    public string newsuffix { get; set; }
    
    public bool Canceled { get { return FrmCanceled; } }
    
    
    public RenameFileInterface()
    {
        InitializeComponent();
        OKButton.Click += OKButton_Click;
        CANCELButton.Click += CANCELButton_Click;
        
    }

    private void OKButton_Click(object? sender, RoutedEventArgs e)
    {
        FrmCanceled = false;
        Close();
        
    }

    private void CANCELButton_Click(object? sender, RoutedEventArgs e)
    {
        FrmCanceled = true;
        Close();
        
    }
}