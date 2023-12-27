using System;
using System.Collections.Generic;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Markup.Xaml;
using Avalonia;

using Avalonia.Interactivity;
using Avalonia.Data;
using Avalonia.Layout;
using Avalonia.Media;

namespace DirOpusReImagined;

public partial class AddEditCmdButtonDefinition : Window
{
    public AddEditCmdButtonDefinition()
    {
        InitializeComponent();

        this.Loaded += AddEditCmdButtonDefinition_Loaded;
        
        //this.FindControl<Button>("btnADD").Click += Add_OnClick;
        //this.FindControl<Button>("btnEDIT").Click += Edit_OnClick;
        //this.FindControl<Button>("btnDELETE").Click += Delete_OnClick;
        this.FindControl<ComboBox>("cbBACKGROUND").SelectionChanged += CbBACKGROUND_OnSelectionChanged;
        this.FindControl<ComboBox>("cbFOREGROUND").SelectionChanged += CbFOREGROUND_OnSelectionChanged;
        this.FindControl<ComboBox>("cbHorizontal").SelectionChanged += CbHorizontal_OnSelectionChanged;
        this.FindControl<ComboBox>("cbVertical").SelectionChanged += CbVertical_OnSelectionChanged;
    }

    private void CbVertical_OnSelectionChanged(object? sender, SelectionChangedEventArgs e)
    {
        Button b = this.FindControl<Button>("SampleButton");
        b.VerticalContentAlignment = (VerticalAlignment)Enum.Parse(typeof(VerticalAlignment), (string)e.AddedItems[0]);
    }

    private void CbHorizontal_OnSelectionChanged(object? sender, SelectionChangedEventArgs e)
    {
        Button b = this.FindControl<Button>("SampleButton");
        b.HorizontalContentAlignment = (HorizontalAlignment)Enum.Parse(typeof(HorizontalAlignment), (string)e.AddedItems[0]);
    }

    private void CbFOREGROUND_OnSelectionChanged(object? sender, SelectionChangedEventArgs e)
    {
        Button b = this.FindControl<Button>("SampleButton"); 
        b.Foreground = new SolidColorBrush((Color)Color.Parse((string)e.AddedItems[0]));
        
    }
 
    private void CbBACKGROUND_OnSelectionChanged(object? sender, SelectionChangedEventArgs e)
    {
        Button b = this.FindControl<Button>("SampleButton");
        b.Background = new SolidColorBrush((Color)Color.Parse((string)e.AddedItems[0]));
    }

    private void AddEditCmdButtonDefinition_Loaded(object? sender, RoutedEventArgs e)
    {
        List<string> thecolors = new List<string>();
        List<string> theHorzalignments = new List<string>();
        List<string> theVertalignments = new List<string>();
        
        theHorzalignments.Add("Strtech");
        theHorzalignments.Add("Left");
        theHorzalignments.Add("Center");
        theHorzalignments.Add("Right");
        
        theVertalignments.Add("Stretch");
        theVertalignments.Add("Top");
        theVertalignments.Add("Center");
        theVertalignments.Add("Bottom");
        
        

        foreach (var it in typeof(Colors).GetProperties())
        {
            thecolors.Add(it.Name);
        }
        
        this.FindControl<ComboBox>("cbHorizontal").Items = theHorzalignments;
        this.FindControl<ComboBox>("cbVertical").Items = theVertalignments;
        
        this.FindControl<ComboBox>("cbBACKGROUND").Items = thecolors;
        this.FindControl<ComboBox>("cbFOREGROUND").Items = thecolors;
        
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