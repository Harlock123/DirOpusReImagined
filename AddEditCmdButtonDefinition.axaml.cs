using System;
using System.Collections.Generic;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Markup.Xaml;
using Avalonia;

using Avalonia.Interactivity;
using Avalonia.Data;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Media;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace DirOpusReImagined;

public partial class AddEditCmdButtonDefinition : Window
{
    List<ButtonSettings> TheButtons = new List<ButtonSettings>();

    private Button TheCurrentButton = null;
    
    //SolidColorBrush theDefaultBackground = new SolidColorBrush((Color)Color.Parse("Grey"));
    
    public AddEditCmdButtonDefinition()
    {
        InitializeComponent();
        
        //TheButtons = theButtons;

        //this.Loaded += AddEditCmdButtonDefinition_Loaded;
        
        //this.FindControl<Button>("btnADD").Click += Add_OnClick;
        //this.FindControl<Button>("btnEDIT").Click += Edit_OnClick;
        //this.FindControl<Button>("btnDELETE").Click += Delete_OnClick;
        //this.FindControl<ComboBox>("cbBACKGROUND").SelectionChanged += CbBACKGROUND_OnSelectionChanged;
        //this.FindControl<ComboBox>("cbFOREGROUND").SelectionChanged += CbFOREGROUND_OnSelectionChanged;
        //this.FindControl<ComboBox>("cbHorizontal").SelectionChanged += CbHorizontal_OnSelectionChanged;
        //this.FindControl<ComboBox>("cbVertical").SelectionChanged += CbVertical_OnSelectionChanged;
        
    }
    
    public AddEditCmdButtonDefinition(List<ButtonSettings> theButtons)
    {
        InitializeComponent();
        
        TheButtons = theButtons;

        DeployButtonSettings();

        this.Loaded += AddEditCmdButtonDefinition_Loaded;
        
        //this.FindControl<Button>("btnADD").Click += Add_OnClick;
        //this.FindControl<Button>("btnEDIT").Click += Edit_OnClick;
        //this.FindControl<Button>("btnDELETE").Click += Delete_OnClick;
        this.FindControl<ComboBox>("cbBACKGROUND").SelectionChanged += CbBACKGROUND_OnSelectionChanged;
        this.FindControl<ComboBox>("cbFOREGROUND").SelectionChanged += CbFOREGROUND_OnSelectionChanged;
        this.FindControl<ComboBox>("cbHorizontal").SelectionChanged += CbHorizontal_OnSelectionChanged;
        this.FindControl<ComboBox>("cbVertical").SelectionChanged += CbVertical_OnSelectionChanged;
        
        this.FindControl<TextBox>("tbContent").KeyUp += HandleButtonContentChanged;    
        
        for(int i=1;i<=36;i++)
        {
            Button b = this.FindControl<Button>("LPB" + i.ToString());
            b.Click += HandleButtonClicked;
        }
        
        //Button b = this.FindControl<Button>("LPB1");
        
        //b.
    }

    private void HandleButtonContentChanged(object? sender, KeyEventArgs e)
    {
        TextBox tb = (TextBox)sender;
        this.FindControl<Button>("SampleButton").Content = tb.Text;
    }

    private void PersistCurrentButtonInterface()
    {
        if (TheCurrentButton == null)
        {
            return;
        }

        ButtonSettings bs = (ButtonSettings)TheCurrentButton.Tag; 
        
        if (bs == null)
        {
            return;

            // bs = new ButtonSettings();
            // bs.Content = "{What Will Show}";
            //
            // bs.Action = "{Action}";
            // bs.Args = "{Arguments}";
            // bs.Background = "LightGray";
            // bs.Foreground = "Black";
            // bs.Name = TheCurrentButton.Name;
            // bs.Name = bs.Name.Replace("LPB", "LPButton");
            // bs.HorizontalAlignment = "Center";
            // bs.VerticalAlignment = "Center";
            // bs.ShellExecute = "False";
            // bs.ShowWindow = "False";
            // bs.ToolTip = "{ToolTip}";
            //
            // TheCurrentButton.Tag = bs;
        }
        
        bs.Content = this.FindControl<TextBox>("tbContent").Text;
        bs.Background = this.FindControl<ComboBox>("cbBACKGROUND").SelectedItem + "";
        bs.Foreground = this.FindControl<ComboBox>("cbFOREGROUND").SelectedItem + "";
        bs.HorizontalAlignment = this.FindControl<ComboBox>("cbHorizontal").SelectedItem + "";
        bs.VerticalAlignment = this.FindControl<ComboBox>("cbVertical").SelectedItem + "";
        bs.Action = this.FindControl<TextBox>("tbCommand").Text;
        bs.Args = this.FindControl<TextBox>("tbArguments").Text;
        bs.ShellExecute = this.FindControl<CheckBox>("cbShellExecute").IsChecked + "";
        bs.ShowWindow = this.FindControl<CheckBox>("cbShowWindow").IsChecked + "";
        bs.ToolTip = this.FindControl<TextBox>("tbToolTip").Text;
        
        Button b = this.FindControl<Button>(TheCurrentButton.Name);
        
        b.Tag = bs;
        b.Content = bs.Content;
        b.Background = new SolidColorBrush((Color)Color.Parse(bs.Background));
        b.Foreground = new SolidColorBrush((Color)Color.Parse(bs.Foreground));
        //b.VerticalAlignment = (VerticalAlignment)Enum.Parse(typeof(VerticalAlignment), bs.VerticalAlignment);
    }

    private void HandleButtonClicked(object? sender, RoutedEventArgs e)
    {
        Button b= (Button)sender;
        
        PersistCurrentButtonInterface();
        
        TheCurrentButton = b;
        
        if (b.Tag == null)
        {
            ButtonSettings bs1 = new ButtonSettings();
            
            bs1.Content = "{What Will Show}";
            
            bs1.Action = "{Action}";
            bs1.Args = "{Arguments}";
            bs1.Background = "LightGray";
            bs1.Foreground = "Black";
            bs1.Name = TheCurrentButton.Name;
            bs1.Name = bs1.Name.Replace("LPB", "LPButton");
            bs1.HorizontalAlignment = "Center";
            bs1.VerticalAlignment = "Center";
            bs1.ShellExecute = "False";
            bs1.ShowWindow = "False";
            bs1.ToolTip = "{ToolTip}";
            
            TheCurrentButton.Tag = bs1;
            
             
        }
        
        ButtonSettings bs = (ButtonSettings)b.Tag;

        //ComboBox cb = this.FindControl<ComboBox>("cbHorizontal");
        
        this.FindControl<TextBox>("tbContent").Text = bs.Content + "";
        this.FindControl<Button>("SampleButton").Content = bs.Content;
        this.FindControl<ComboBox>("cbBACKGROUND").SelectedItem = bs.Background + "";
        this.FindControl<ComboBox>("cbFOREGROUND").SelectedItem = bs.Foreground  + "";
        this.FindControl<ComboBox>("cbHorizontal").SelectedItem = bs.HorizontalAlignment + "";
        this.FindControl<ComboBox>("cbVertical").SelectedItem = bs.VerticalAlignment + "";
        this.FindControl<TextBox>("tbCommand").Text = bs.Action + "";
        this.FindControl<TextBox>("tbArguments").Text = bs.Args + "";
        this.FindControl<CheckBox>("cbShellExecute").IsChecked = bool.Parse(bs.ShellExecute + "");
        this.FindControl<CheckBox>("cbShowWindow").IsChecked = bool.Parse(bs.ShowWindow + "");
        this.FindControl<TextBox>("tbToolTip").Text = bs.ToolTip + "";

    }

    private void InitializeComponent()
    {
        AvaloniaXamlLoader.Load(this);
    }

    private void DeployButtonSettings()
    {
        
        
        for (int i = 1; i <= 36; i++)
        {
            Button B = this.FindControl<Button>("LPB" + i.ToString());
            B.Content = "";
            
            B.Background = new SolidColorBrush(Colors.LightGray);
            B.Foreground = new SolidColorBrush(Colors.Black);
        }
        
        foreach (ButtonSettings b in TheButtons)
        {
            string name = b.Name.Replace("LPButton", "LPB");
            Button theButton = this.FindControl<Button>(name);
            theButton.Content = b.Content;
            
            if (b.Background == null)
            {
                b.Background = "LightGray"; 
            }
            
            if (b.Foreground == null)
            {
                b.Foreground = "Black"; 
            }   
            
            theButton.Background = new SolidColorBrush((Color)Color.Parse(b.Background));
            theButton.Foreground = new SolidColorBrush((Color)Color.Parse(b.Foreground));
            
            theButton.Tag = b;
            
            
        }
    }

    private void CbVertical_OnSelectionChanged(object? sender, SelectionChangedEventArgs e)
    {
        if (e.AddedItems.Count == 0)
        {
            return;
        }
        
        Button b = this.FindControl<Button>("SampleButton");
        b.VerticalContentAlignment = (VerticalAlignment)Enum.Parse(typeof(VerticalAlignment), (string)e.AddedItems[0]);
    }

    private void CbHorizontal_OnSelectionChanged(object? sender, SelectionChangedEventArgs e)
    {
        if (e.AddedItems.Count == 0)
        {
            return;
        }
        
        Button b = this.FindControl<Button>("SampleButton");
        b.HorizontalContentAlignment = (HorizontalAlignment)Enum.Parse(typeof(HorizontalAlignment), (string)e.AddedItems[0]);
    }

    private void CbFOREGROUND_OnSelectionChanged(object? sender, SelectionChangedEventArgs e)
    {
        if (e.AddedItems.Count == 0)
        {
            return;
        }
        
        Button b = this.FindControl<Button>("SampleButton"); 
        b.Foreground = new SolidColorBrush((Color)Color.Parse((string)e.AddedItems[0]));
        
    }
 
    private void CbBACKGROUND_OnSelectionChanged(object? sender, SelectionChangedEventArgs e)
    {
        if (e.AddedItems.Count == 0)
        {
            return;
        }
        
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
    
    private void Save_OnClick(object sender, RoutedEventArgs e)
    {
        // Implement add logic here.
    }

    private void Clear_OnClick(object sender, RoutedEventArgs e)
    {
        // Implement edit logic here.
    }

    private void Exit_OnClick(object sender, RoutedEventArgs e)
    {
        // Implement delete logic here.
    }
}