using Avalonia;
using Avalonia.Controls;
using Avalonia.Markup.Xaml;
using Avalonia;

using Avalonia.Interactivity;
using Avalonia.Data;

namespace DirOpusReImagined;

public partial class AddEditCmdButtonDefinition : Window
{
    public AddEditCmdButtonDefinition()
    {
        InitializeComponent();
    }
    
    private void InitializeComponent()
    {
        AvaloniaXamlLoader.Load(this);
    }
    
    private void Add_OnClick(object sender, RoutedEventArgs e)
    {
        // Implement add logic here.
    }

    private void Edit_OnClick(object sender, RoutedEventArgs e)
    {
        // Implement edit logic here.
    }

    private void Delete_OnClick(object sender, RoutedEventArgs e)
    {
        // Implement delete logic here.
    }
}