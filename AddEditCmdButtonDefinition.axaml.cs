using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using System.Xml.Serialization;
using System.Xml.Linq;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Markup.Xaml;

using Avalonia.Interactivity;
using Avalonia.Data;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Media;

namespace DirOpusReImagined;

public partial class AddEditCmdButtonDefinition : Window
{
    List<ButtonSettings> TheButtons = new List<ButtonSettings>();
    
    public MainWindow? TheMainWindow ;//= null;

    private Button? TheCurrentButton ;//= null;
    
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
    
    /// <summary>The banks being edited: copies, so nothing changes in the main window until Save.</summary>
    private readonly List<ButtonBank> _banks = new();
    private int _bank;
    private bool _refreshingBanks;

    public AddEditCmdButtonDefinition(IEnumerable<ButtonBank> banks, int showing)
    {
        InitializeComponent();

        _banks.AddRange(banks.Select(b => b.Clone()));
        if (_banks.Count == 0) _banks.Add(new ButtonBank(ButtonBanks.DefaultName(0)));
        _bank = Math.Clamp(showing, 0, _banks.Count - 1);
        TheButtons = _banks[_bank].Buttons;

        DeployButtonSettings();
        InitBankControls();

        this.Loaded += AddEditCmdButtonDefinition_Loaded;
        
        //this.FindControl<Button>("btnADD").Click += Add_OnClick;
        //this.FindControl<Button>("btnEDIT").Click += Edit_OnClick;
        //this.FindControl<Button>("btnDELETE").Click += Delete_OnClick;
        
        this.FindControl<Button>("btnCommandHelp").Click += CommandHelp_OnClick;
        this.FindControl<Button>("btnArgHelp").Click += ArgHelp_OnClick;
        
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

        LoadTerminalSettings();
    }

    /// <summary>
    /// The config file to read and write. Prefers the path the owning <see cref="MainWindow"/>
    /// actually loaded, so the dialog can never edit a different file than the app is using; falls
    /// back to the shared resolution order when this dialog was opened without an owner.
    /// </summary>
    private string ConfigPath() => TheMainWindow?.ConfigFilePath ?? ConfigFile.Resolve();

    /// <summary>Populates every field on the System Wide Settings tab.</summary>
    private void LoadTerminalSettings()
    {
        LoadTerminalFields();

        // Reflect the live keep-rclone-warm option (loaded from config at startup).
        var cb = this.FindControl<CheckBox>("cbKeepRcloneWarm");
        if (cb != null) cb.IsChecked = AppOptions.KeepRcloneWarm;

        // Reflect the live verify-copies option (loaded from config at startup).
        var vc = this.FindControl<CheckBox>("cbVerifyCopies");
        if (vc != null) vc.IsChecked = AppOptions.VerifyCopies;

        LoadUiScaleFields();
    }

    /// <summary>
    /// Populates just the Terminal fields from Configuration.xml.
    ///
    /// Kept separate because it gives up early when the file is absent or has no &lt;Terminal&gt;
    /// element. While this lived inline in <see cref="LoadTerminalSettings"/> those early exits also
    /// skipped every option below it, so the checkboxes silently showed their defaults instead of the
    /// user's saved settings whenever the app was started from a directory without a Configuration.xml.
    /// </summary>
    private void LoadTerminalFields()
    {
        try
        {
            // Same file MainWindow loaded -- not whatever happens to sit in the working directory.
            string path = ConfigPath();
            if (!File.Exists(path)) return;

            var terminal = XDocument.Load(path).Descendants("Terminal").FirstOrDefault();
            if (terminal == null) return;

            this.FindControl<TextBox>("tbTerminalCommand").Text = (string?)terminal.Element("Command") ?? "";
            this.FindControl<TextBox>("tbTerminalArgs").Text = (string?)terminal.Element("Args") ?? "";
        }
        catch
        {
            // Missing or malformed config just leaves the fields blank.
        }
    }

    /// <summary>
    /// Fills the UI-scale box with the saved override (blank when it is on Auto) and captions it with
    /// what is actually in force this session, so "Auto" is not a black box — the user can see the
    /// number it resolved to and where it came from before deciding whether to override it.
    /// </summary>
    private void LoadUiScaleFields()
    {
        var box = this.FindControl<TextBox>("tbUiScale");
        if (box != null)
            box.Text = AppOptions.UiScale > 0
                ? AppOptions.UiScale.ToString("0.##", CultureInfo.InvariantCulture)
                : "";

        var status = this.FindControl<TextBlock>("tbUiScaleStatus");
        if (status != null)
            status.Text = $"Now running at {DisplayScaling.AppliedScale:0.##}x — {DisplayScaling.AppliedSource}. "
                        + $"Auto detects {DisplayScaling.DetectedScale:0.##}x ({DisplayScaling.DetectedSource}).";
    }

    /// <summary>
    /// Persists the UI-scale override. Blank, 0 or unparseable text all mean "auto", which is stored as
    /// 0 rather than removed so the element stays visible in the config file. The value is not applied
    /// live: Avalonia fixes the scale factor when its windowing platform initialises, so it is picked
    /// up by <see cref="DisplayScaling.Bootstrap"/> on the next launch.
    /// </summary>
    private void UpsertUiScaleSetting(XDocument doc)
    {
        string raw = this.FindControl<TextBox>("tbUiScale")?.Text ?? "";

        double scale = 0;
        if (!string.IsNullOrWhiteSpace(raw) && DisplayScaling.TryParseScale(raw, out var parsed) && parsed > 0)
            scale = Math.Round(Math.Clamp(parsed, 0.5, 4.0), 2);

        AppOptions.UiScale = scale;

        var el = doc.Root!.Element("UiScale");
        if (el == null) { el = new XElement("UiScale"); doc.Root!.Add(el); }
        el.Value = scale.ToString("0.##", CultureInfo.InvariantCulture);
    }

    /// <summary>Writes the Terminal fields into <paramref name="doc"/>, creating the element if absent.</summary>
    private void UpsertTerminalSettings(XDocument doc)
    {
        var command = this.FindControl<TextBox>("tbTerminalCommand").Text ?? "";
        var args = this.FindControl<TextBox>("tbTerminalArgs").Text ?? "";

        var terminal = doc.Descendants("Terminal").FirstOrDefault();
        if (terminal == null)
        {
            terminal = new XElement("Terminal");
            doc.Root!.Add(terminal);
        }

        terminal.RemoveAll();
        terminal.Add(new XElement("Command", command));
        terminal.Add(new XElement("Args", args));

        // Persist the keep-rclone-warm option and apply it live so it takes effect this session.
        bool keepWarm = this.FindControl<CheckBox>("cbKeepRcloneWarm")?.IsChecked ?? false;
        AppOptions.KeepRcloneWarm = keepWarm;
        DirOpusReImagined.FileSystem.Rclone.RcloneService.KeepWarm = keepWarm;

        var warmEl = doc.Root!.Element("KeepRcloneWarm");
        if (warmEl == null) { warmEl = new XElement("KeepRcloneWarm"); doc.Root!.Add(warmEl); }
        warmEl.Value = keepWarm ? "true" : "false";

        // Persist the verify-copies option and apply it live so it takes effect this session.
        bool verify = this.FindControl<CheckBox>("cbVerifyCopies")?.IsChecked ?? false;
        AppOptions.VerifyCopies = verify;

        var verifyEl = doc.Root!.Element("VerifyCopies");
        if (verifyEl == null) { verifyEl = new XElement("VerifyCopies"); doc.Root!.Add(verifyEl); }
        verifyEl.Value = verify ? "true" : "false";

        // Persist the UI scale override. Unlike the options above this one cannot be applied live.
        UpsertUiScaleSetting(doc);
    }

    private void ArgHelp_OnClick(object? sender, RoutedEventArgs e)
    {
        // we need to open the button config window
        ConfigHelp BC = new ConfigHelp();
        //BC.TheMainWindow = this;
        BC.ShowDialog(this);
        
    }
    
    private void CommandHelp_OnClick(object? sender, RoutedEventArgs e)
    {
        // we need to open the button config window
        ConfigHelp BC = new ConfigHelp();
        //BC.TheMainWindow = this;
        BC.ShowDialog(this);
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
        
        // if the sheelexecute or showwindow elements are null then set them to false
        if (bs.ShellExecute == null)
        {
            bs.ShellExecute = "False";
        }
        if (bs.ShowWindow == null)
        {
            bs.ShowWindow = "False";
        }   

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

    private void InitBankControls()
    {
        var combo = this.FindControl<ComboBox>("cbBank")!;
        combo.SelectionChanged += (_, _) =>
        {
            if (_refreshingBanks || combo.SelectedIndex < 0 || combo.SelectedIndex == _bank) return;
            SwitchBank(combo.SelectedIndex);
        };

        var name = this.FindControl<TextBox>("tbBankName")!;
        name.LostFocus += (_, _) => CommitBankName();
        name.KeyUp += (_, e) => { if (e.Key == Key.Enter) CommitBankName(); };

        this.FindControl<Button>("btnNewBank")!.Click += (_, _) =>
        {
            LeaveBank();
            _banks.Add(new ButtonBank(ButtonBanks.NewName(_banks.Select(b => b.Name))));
            EnterBank(_banks.Count - 1);
        };
        this.FindControl<Button>("btnDeleteBank")!.Click += (_, _) =>
        {
            if (_banks.Count == 1)
            {
                new MessageBox("There has to be at least one bank. Clear its buttons instead.", "Delete Bank").ShowDialog(this);
                return;
            }
            // Nothing is lost until Save: Exit without saving keeps the bank.
            TheCurrentButton = null;
            ClearFields();
            _banks.RemoveAt(_bank);
            EnterBank(Math.Min(_bank, _banks.Count - 1));
        };
        this.FindControl<Button>("btnBankEarlier")!.Click += (_, _) => MoveBank(-1);
        this.FindControl<Button>("btnBankLater")!.Click += (_, _) => MoveBank(+1);

        RefreshBankList();
    }

    /// <summary>Stores what's on screen into the current bank before showing another.</summary>
    private void LeaveBank()
    {
        PersistCurrentButtonInterface();
        CommitBankName();
        CollectCurrentBank();
        TheCurrentButton = null;
        ClearFields();
    }

    private void EnterBank(int index)
    {
        _bank = index;
        TheButtons = _banks[_bank].Buttons;
        DeployButtonSettings();
        RefreshBankList();
    }

    private void SwitchBank(int index)
    {
        LeaveBank();
        EnterBank(index);
    }

    private void MoveBank(int delta)
    {
        int to = _bank + delta;
        if (to < 0 || to >= _banks.Count) return;
        LeaveBank();
        (_banks[_bank], _banks[to]) = (_banks[to], _banks[_bank]);
        EnterBank(to);
    }

    /// <summary>The 36 editor buttons, in order, become the current bank's buttons.</summary>
    private void CollectCurrentBank()
    {
        var list = _banks[_bank].Buttons;
        list.Clear();
        for (int i = 1; i <= ButtonBanks.ButtonsPerBank; i++)
            if (this.FindControl<Button>("LPB" + i)?.Tag is ButtonSettings bs)
                list.Add(bs);
    }

    private void CommitBankName()
    {
        string text = (this.FindControl<TextBox>("tbBankName")!.Text ?? "").Trim();
        if (text.Length == 0 || text == _banks[_bank].Name) return;
        _banks[_bank].Name = text;
        RefreshBankList();
    }

    private void RefreshBankList()
    {
        _refreshingBanks = true;
        try
        {
            var combo = this.FindControl<ComboBox>("cbBank")!;
            combo.ItemsSource = _banks.Select((b, i) => $"{i + 1}. {b.Name}").ToList();
            combo.SelectedIndex = _bank;
            this.FindControl<TextBox>("tbBankName")!.Text = _banks[_bank].Name;
            this.FindControl<Button>("btnBankEarlier")!.IsEnabled = _bank > 0;
            this.FindControl<Button>("btnBankLater")!.IsEnabled = _bank < _banks.Count - 1;
        }
        finally { _refreshingBanks = false; }
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
            B.Tag = null;
        }
        
        foreach (ButtonSettings b in TheButtons)
        {
            int slot = ButtonBanks.SlotOf(b.Name);
            Button? theButton = slot == 0 ? null : this.FindControl<Button>("LPB" + slot);
            if (theButton == null) continue;
            
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

    /// <summary>
    /// This method is called when the AddEditCmdButtonDefinition is loaded.
    /// It initializes the available colors, horizontal alignments, and vertical alignments for ComboBox controls.
    /// </summary>
    /// <param name="sender">The object that triggered the event.</param>
    /// <param name="e">The event arguments.</param>
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
        
        this.FindControl<ComboBox>("cbHorizontal").ItemsSource = theHorzalignments;
        this.FindControl<ComboBox>("cbVertical").ItemsSource = theVertalignments;

        this.FindControl<ComboBox>("cbBACKGROUND").ItemsSource = thecolors;
        this.FindControl<ComboBox>("cbFOREGROUND").ItemsSource = thecolors;
        
    }

    /// <summary>
    /// Handles the Click event of the Save button.
    /// </summary>
    /// <param name="sender">The object that raised the event.</param>
    /// <param name="e">The event arguments.</param>
    private void Save_OnClick(object sender, RoutedEventArgs e)
    {
        // Implement save logic here.
        
        PersistCurrentButtonInterface();
        CommitBankName();
        CollectCurrentBank();

        // Load the same config the app is actually using. Resolving this per-call against the
        // working directory used to mean the dialog could edit a different file than the one loaded.
        string configPath = ConfigPath();
        if (!File.Exists(configPath))
        {
            new MessageBox($"No configuration file found at:\n{configPath}", "Save").ShowDialog(this);
            return;
        }

        var doc = XDocument.Load(configPath);
        ButtonBanks.Write(doc.Root!, _banks);
        ButtonBanks.WriteActive(doc.Root!, _bank);

        // Persist the System Wide Settings (Terminal) alongside the buttons.
        UpsertTerminalSettings(doc);

        // Save the modifications back to the same file they came from.
        ConfigFile.EnsureDirectory(configPath);
        doc.Save(configPath);

        TheMainWindow?.SetButtonBanks(_banks, _bank);
    }

    private void Clear_OnClick(object sender, RoutedEventArgs e) => ClearFields();

    private void ClearFields()
    {
        this.FindControl<TextBox>("tbContent").Text = "";
        this.FindControl<Button>("SampleButton").Content = "";
        this.FindControl<ComboBox>("cbBACKGROUND").SelectedItem = "";
        this.FindControl<ComboBox>("cbFOREGROUND").SelectedItem = "";
        this.FindControl<ComboBox>("cbHorizontal").SelectedItem = "";
        this.FindControl<ComboBox>("cbVertical").SelectedItem = "";
        this.FindControl<TextBox>("tbCommand").Text = "";
        this.FindControl<TextBox>("tbArguments").Text = "";
        this.FindControl<CheckBox>("cbShellExecute").IsChecked = false;
        this.FindControl<CheckBox>("cbShowWindow").IsChecked = false;
        this.FindControl<TextBox>("tbToolTip").Text = "";
        
        TheCurrentButton = null;
    }

    private void Exit_OnClick(object sender, RoutedEventArgs e)
    {
        // Implement delete logic here.
        this.Close();
    }

    
}