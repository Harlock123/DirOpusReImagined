using Avalonia;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Markup.Xaml;

namespace DirOpusReImagined;

public partial class ConfigHelp : Window
{
    public ConfigHelp()
    {
        InitializeComponent();
    }

    private void OkButton_Click(object? sender, RoutedEventArgs e)
    {
        Close();
    }
}