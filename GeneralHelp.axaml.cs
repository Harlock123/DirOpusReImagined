using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Markup.Xaml;

namespace DirOpusReImagined;

public partial class GeneralHelp : Window
{
    public GeneralHelp()
    {
        InitializeComponent();
    }

    private void OkButton_Click(object? sender, RoutedEventArgs e)
    {
        Close();
    }
}
