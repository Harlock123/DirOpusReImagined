using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
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
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace DirOpusReImagined;

public partial class AddEditCmdButtonDefinition : Window
{
    List<ButtonSettings> TheButtons = new List<ButtonSettings>();
    
    public MainWindow? TheMainWindow ;//= null;

    /// <summary>
    /// How many slots the editor offers. Capped to match the buttons MainWindow.axaml declares:
    /// the main window still resolves each saved button to a control named LpButtonNN, so a slot
    /// past this number could be edited and saved but never rendered.
    /// </summary>
    private const int SlotCount = 36;

    /// <summary>The editable slots, one per row of SlotList.</summary>
    private readonly ObservableCollection<ButtonSlot> _slots = new();

    /// <summary>The slot the form is currently editing, or null when nothing is selected.</summary>
    private ButtonSlot? _currentSlot;

    /// <summary>
    /// Set by any edit the user makes, cleared by a successful save. Nothing typed here reaches the
    /// config file until Save runs, so without this the Exit button and the window's close box threw
    /// the work away silently.
    /// </summary>
    private bool _dirty;

    /// <summary>
    /// Suppresses dirty-tracking while the dialog fills its own controls. Loading a button into the
    /// form raises the same TextChanged and SelectionChanged events typing does, so without this the
    /// dialog would believe it had been modified before the user touched anything.
    /// </summary>
    private bool _loading;

    /// <summary>Set once the user has confirmed a discard, so the re-issued Close is not re-prompted.</summary>
    private bool _closeConfirmed;

    /// <summary>
    /// Whether the form has been touched since the current slot was loaded into it. Distinct from
    /// <see cref="_dirty"/>, which covers the whole dialog: this one decides whether an unused slot
    /// has earned a ButtonSettings, so browsing the list does not fill the config with placeholders.
    /// </summary>
    private bool _formEdited;
    
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
        
        this.FindControl<Button>("btnCommandHelp").Click += CommandHelp_OnClick;
        this.FindControl<Button>("btnArgHelp").Click += ArgHelp_OnClick;
        
        this.FindControl<ComboBox>("cbBACKGROUND").SelectionChanged += CbBACKGROUND_OnSelectionChanged;
        this.FindControl<ComboBox>("cbFOREGROUND").SelectionChanged += CbFOREGROUND_OnSelectionChanged;
        this.FindControl<ComboBox>("cbHorizontal").SelectionChanged += CbHorizontal_OnSelectionChanged;
        this.FindControl<ComboBox>("cbVertical").SelectionChanged += CbVertical_OnSelectionChanged;
        
        this.FindControl<TextBox>("tbContent").KeyUp += HandleButtonContentChanged;    
        
        var list = this.FindControl<ListBox>("SlotList");
        list.ItemsSource = _slots;
        list.SelectionChanged += HandleSlotSelected;

        LoadTerminalSettings();

        // Last: LoadTerminalSettings populates controls, and wiring before it would record those
        // programmatic writes as user edits.
        WireDirtyTracking();
    }

    /// <summary>Marks the dialog modified unless the change came from the dialog populating itself.</summary>
    private void MarkDirty()
    {
        if (_loading) return;
        _dirty = true;
        _formEdited = true;
    }

    /// <summary>
    /// Subscribes every editable control to the dirty flag. Done in one place rather than per-handler
    /// so a control added to the XAML later is one string away from being covered.
    /// </summary>
    private void WireDirtyTracking()
    {
        foreach (var name in new[] { "tbContent", "tbCommand", "tbArguments", "tbToolTip",
                                     "tbTerminalCommand", "tbTerminalArgs", "tbUiScale" })
        {
            var box = this.FindControl<TextBox>(name);
            if (box != null) box.TextChanged += (_, _) => MarkDirty();
        }

        foreach (var name in new[] { "cbShellExecute", "cbShowWindow", "cbKeepRcloneWarm", "cbVerifyCopies" })
        {
            var check = this.FindControl<CheckBox>(name);
            if (check != null) check.IsCheckedChanged += (_, _) => MarkDirty();
        }

        foreach (var name in new[] { "cbBACKGROUND", "cbFOREGROUND", "cbHorizontal", "cbVertical" })
        {
            var combo = this.FindControl<ComboBox>(name);
            if (combo != null) combo.SelectionChanged += (_, _) => MarkDirty();
        }
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
        _loading = true;
        try
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
        finally { _loading = false; }
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

        // Awaited, like the other modals in this dialog: an unawaited ShowDialog leaves a window
        // outliving the handler that opened it.
        private async void ArgHelp_OnClick(object? sender, RoutedEventArgs e)
        {
            await new ConfigHelp().ShowDialog(this);
        }

        private async void CommandHelp_OnClick(object? sender, RoutedEventArgs e)
        {
            await new ConfigHelp().ShowDialog(this);
        }

        private void HandleButtonContentChanged(object? sender, KeyEventArgs e)
        {
            TextBox tb = (TextBox)sender;
            this.FindControl<Button>("SampleButton").Content = tb.Text;
        }

        /// <summary>
    /// Copies the form back into the selected slot. Materialises the slot's ButtonSettings on the
    /// first real edit -- see <see cref="HandleSlotSelected"/> for why not on selection.
    /// </summary>
    private void PersistCurrentButtonInterface()
    {
        if (_currentSlot == null) return;

        // An unused slot that was only looked at keeps its ButtonSettings null, so Save leaves it
        // out of the file. Without this, arrow-keying down the list would write 36 placeholders.
        if (_currentSlot.Settings == null && !_formEdited) return;

        ButtonSettings bs = _currentSlot.Settings ?? new ButtonSettings { Name = _currentSlot.Name };

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

        _currentSlot.Settings = bs;
    }

        /// <summary>
    /// Loads the newly selected slot into the form, flushing the previous one first.
    /// </summary>
    /// <remarks>
    /// An unconfigured slot is shown with placeholder text but is NOT materialised here. The old
    /// code created a ButtonSettings the moment a slot button was clicked, which Save then wrote
    /// out; harmless with 36 separate buttons you had to click deliberately, but a ListBox can be
    /// arrow-keyed, and walking the list would otherwise have written 36 placeholder buttons into
    /// the config. The slot becomes real on the first edit instead - see PersistCurrentButtonInterface.
    /// </remarks>
    private void HandleSlotSelected(object? sender, SelectionChangedEventArgs e)
    {
        PersistCurrentButtonInterface();

        _currentSlot = this.FindControl<ListBox>("SlotList").SelectedItem as ButtonSlot;
        if (_currentSlot == null) return;

        ButtonSettings bs = _currentSlot.Settings ?? ButtonSlot.Placeholder(_currentSlot.Name);

        bs.ShellExecute ??= "False";
        bs.ShowWindow ??= "False";

        // Filling the form fires the same events typing does; this is not a user edit.
        _loading = true;
        try
        {
            this.FindControl<TextBox>("tbContent").Text = bs.Content + "";
            this.FindControl<Button>("SampleButton").Content = bs.Content;
            this.FindControl<ComboBox>("cbBACKGROUND").SelectedItem = bs.Background + "";
            this.FindControl<ComboBox>("cbFOREGROUND").SelectedItem = bs.Foreground + "";
            this.FindControl<ComboBox>("cbHorizontal").SelectedItem = bs.HorizontalAlignment + "";
            this.FindControl<ComboBox>("cbVertical").SelectedItem = bs.VerticalAlignment + "";
            this.FindControl<TextBox>("tbCommand").Text = bs.Action + "";
            this.FindControl<TextBox>("tbArguments").Text = bs.Args + "";
            this.FindControl<CheckBox>("cbShellExecute").IsChecked = bool.TryParse(bs.ShellExecute, out var se) && se;
            this.FindControl<CheckBox>("cbShowWindow").IsChecked = bool.TryParse(bs.ShowWindow, out var sw) && sw;
            this.FindControl<TextBox>("tbToolTip").Text = bs.ToolTip + "";
        }
        finally { _loading = false; }

        // The form now mirrors the slot; nothing has been edited against it yet.
        _formEdited = false;
    }

    private void InitializeComponent()
    { 
        AvaloniaXamlLoader.Load(this);
    }

    /// <summary>
    /// Builds the 36 slot rows and attaches each saved button to the slot its name points at.
    /// </summary>
    /// <remarks>
    /// The slot is chosen by parsing the trailing digits of the stored name rather than by
    /// rewriting the name into a control name and calling FindControl. The old code tried
    /// Replace("LpButton","LPB"), then Replace("LPButton","LPB") as a fallback -- both spellings
    /// occur in the shipped configs -- and then dereferenced the result with no null check, so any
    /// third spelling or a slot number past the last control took the dialog down on open.
    /// Anything unparseable or out of range is now skipped.
    /// </remarks>
    private void DeployButtonSettings()
    {
        _slots.Clear();
        for (int i = 1; i <= SlotCount; i++) _slots.Add(new ButtonSlot(i));

        foreach (ButtonSettings b in TheButtons)
        {
            int index = SlotIndexOf(b.Name);
            if (index < 1 || index > SlotCount) continue;

            b.Background ??= "LightGray";
            b.Foreground ??= "Black";

            _slots[index - 1].Settings = b;
        }
    }

    /// <summary>
    /// The slot number embedded in a stored button name, or -1 when there isn't one.
    /// Matches the trailing digits of any casing -- LpButton7, LPButton7, LPB7 all give 7.
    /// </summary>
    private static int SlotIndexOf(string? name)
    {
        if (string.IsNullOrWhiteSpace(name)) return -1;
        var m = Regex.Match(name, @"(\d+)\s*$");
        if (!m.Success) return -1;
        return int.TryParse(m.Groups[1].Value, out int n) ? n : -1;
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
    /// Method to serialize a list of button settings to XML format. </summary>
    /// <param name="settings">List of button settings to be serialized.</param>
    /// <returns>Serialized XML string of the button settings list.</returns>
    /// /
    public string SerializeButtonSettingsListToXml(List<ButtonSettings> settings)
    {
        
        
        // make sure each button has a Margin element
        foreach (var button in settings)
        {
            if (button.Margin == null)
            {
                button.Margin = "2,2,2,2";
            }
        }
        
        var serializer = new XmlSerializer(typeof(List<ButtonSettings>));

        using (var stream = new MemoryStream())
        {
            using (var writer = new StreamWriter(stream, Encoding.UTF8))
            {
                serializer.Serialize(writer, settings);
                var xml = Encoding.UTF8.GetString(stream.ToArray());

                // Remove the ArrayOfButtonSettings line
                xml = xml.Substring(xml.IndexOf('>') + 1);
                xml = xml.Substring(xml.IndexOf('>') + 1);
                xml = xml.Substring(0, xml.LastIndexOf('<'));

                // Replace ButtonSettings with Button tags
                xml = xml.Replace("<ButtonSettings>", "<Button>");
                xml = xml.Replace("</ButtonSettings>", "</Button>");

                return xml;
            }
        }
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
        
        // Configured slots only, in slot order. Unconfigured ones are simply absent from the
        // file, which is what the reader already expects.
        List<ButtonSettings> theButtonSettings = _slots
            .Where(slot => slot.Settings != null)
            .Select(slot => slot.Settings!)
            .ToList();
        
        string xml = SerializeButtonSettingsListToXml(theButtonSettings);
        
        // Load the same config the app is actually using. Resolving this per-call against the
        // working directory used to mean the dialog could edit a different file than the one loaded.
        string configPath = ConfigPath();
        if (!File.Exists(configPath))
        {
            new MessageBox($"No configuration file found at:\n{configPath}", "Save").ShowDialog(this);
            return;
        }

        var doc = XDocument.Load(configPath);

        // Parse the xml string into an XElement
        var newElement = XElement.Parse("<Buttons>" + xml + "</Buttons>").Elements();

        // Find the Buttons element
        var buttonsElement = doc.Descendants("Buttons").FirstOrDefault();

        // If the Buttons element exists, add the new element
        if (buttonsElement != null)
        {
            buttonsElement.RemoveAll();

            buttonsElement.Add(newElement);

            // Persist the System Wide Settings (Terminal) alongside the buttons.
            UpsertTerminalSettings(doc);

            // Save the modifications back to the same file they came from.
            ConfigFile.EnsureDirectory(configPath);
            doc.Save(configPath);

            TheMainWindow.DoButtonRefresh();

            // Only here: the early return above and the else below both leave the file untouched.
            _dirty = false;
        }
        else
        {
            Console.WriteLine("Buttons element not found in the XML file");
        }
        
    }

    private void Clear_OnClick(object sender, RoutedEventArgs e)
    {
        // Blanking the form abandons the current selection instead of changing stored data, so it
        // must not raise the unsaved-changes flag. An edit made *before* this still leaves it set.
        _loading = true;
        try
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
        }
        finally { _loading = false; }

        _currentSlot = null;
        this.FindControl<ListBox>("SlotList").SelectedItem = null;
    }

    private void Exit_OnClick(object sender, RoutedEventArgs e)
    {
        // The unsaved-changes guard lives in OnClosing so the window's own close box is covered too,
        // not just this button.
        this.Close();
    }

    /// <summary>
    /// Intercepts every close - the Exit button, the title-bar close box, Alt+F4 - and offers to keep
    /// editing when there are unsaved changes. The confirmation is itself a dialog, so the close has
    /// to be cancelled and re-issued once the answer comes back.
    /// </summary>
    protected override void OnClosing(WindowClosingEventArgs e)
    {
        if (_dirty && !_closeConfirmed)
        {
            e.Cancel = true;
            _ = ConfirmThenClose();
            return;
        }

        base.OnClosing(e);
    }

    private async Task ConfirmThenClose()
    {
        bool discard = await new MessageBox(
            "You have unsaved changes. Close without saving?",
            showCancel: true,
            okText: "Discard",
            cancelText: "Keep Editing",
            title: "Unsaved Changes").ShowDialog<bool>(this);

        // Closing the prompt itself yields false, which keeps the editor open - the safe default.
        if (!discard) return;

        _closeConfirmed = true;
        Close();
    }

    
}