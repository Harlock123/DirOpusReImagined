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

    private void DismissButton_Click(object? sender, RoutedEventArgs e)
    {
        Close();
    }
}
