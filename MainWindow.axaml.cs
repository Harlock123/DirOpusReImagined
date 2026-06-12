using Avalonia;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Media;
//using Microsoft.CodeAnalysis.CSharp.Syntax;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using System.Xml.Linq;
using Avalonia.Input;
using DirOpusReImagined.FileSystem;
using DirOpusReImagined.FileSystem.Rclone;
using NickStrupat;
using Tomlyn;
//using NickStrupat.ComputerInfo;
using Bitmap = Avalonia.Media.Imaging.Bitmap;
using Color = Avalonia.Media.Color;
using Image = Avalonia.Controls.Image;

namespace DirOpusReImagined
{
    public partial class MainWindow : Window
    {
        #region Local Variables
        
        
        private Avalonia.Rect LRB = new Avalonia.Rect();
        private Avalonia.Rect RRB = new Avalonia.Rect();
        private Avalonia.Size OrigSize = new Avalonia.Size();

        private List<string> ExecutableStuff = new List<string>();
        private List<string> ImageStuff = new List<string>();

        private List<ButtonEntry> TheButtons = new List<ButtonEntry>();
        private List<DriveButtonEntry> TheDriveButtons = new List<DriveButtonEntry>();
        
        private List<Button> TheLowerPanelButtons = new List<Button>();
        
        private List<ButtonSettings> TheButtonSettings = new List<ButtonSettings>();
        
        private string StartRightPath = "";
        private string StartLeftPath = "";

        private Stack<string> _lpHistory = new Stack<string>();
        private Stack<string> _rpHistory = new Stack<string>();

        private bool UseIntegratedImageViewer = true;
        
        private string LastButtonPopupName = "";
        
        private AFileEntry LastFileHovered = null;

        private PopUp _pop;

        private List<object> _lpUnfilteredItems = new List<object>();
        private List<object> _rpUnfilteredItems = new List<object>();

        #endregion
        
        public MainWindow()
        {
            InitializeComponent();

            ProviderRegistry.Register(new RcloneFileProvider());
            FileUtility.PanelPopulated += OnPanelPopulated;
            Closing += (_, _) => RcloneService.Shutdown();

            var cinf = new ComputerInfo();
            
            var globalMEM = cinf.TotalPhysicalMemory;

            this.Title += " " + (globalMEM / 1024 / 1024 / 1024).ToString() + " GB ";
            
            // Apply The Settings if possible
            // Search order:
            // 1. Current working directory
            // 2. Directory where the executable lives
            // 3. Platform-specific config location:
            //    - macOS: ~/Library/Application Support/dori/Configuration.xml
            //    - Linux/Unix: ~/.config/dori/Configuration.xml
            //    - Windows: %APPDATA%\dori\Configuration.xml

            string configFile = FindConfigurationFile();
            if (configFile != null)
            {
                ClearLowerButtons();
                ApplyButtonSettingsFromXml(configFile, this);
            }
            
            MainWindowGridContainer.SizeChanged += MainWindowGridContainer_SizeChanged;

            //Bitmap B1 = LoadImage(ImageStrings.BackButton);

            string assetsDir = FindAssetsDirectory();
            if (assetsDir != null)
            {

                Bitmap B2 = new Bitmap(Path.Combine(assetsDir, "BackFolder.png"));
                Bitmap B3 = new Bitmap(Path.Combine(assetsDir, "Drives.png"));
                Bitmap B4 = new Bitmap(Path.Combine(assetsDir, "LeftArrow.png"));
                Bitmap B5 = new Bitmap(Path.Combine(assetsDir, "RightArrow.png"));
                Bitmap B6 = new Bitmap(Path.Combine(assetsDir, "LeftRightArrows.png"));

                Image I1 = new Image();
                Image I2 = new Image();
                Image I3 = new Image();
                Image I4 = new Image();
                Image I5 = new Image();
                Image I6 = new Image();
                Image I7 = new Image();

                I1.Source = B2;
                I1.Width = RpBackButton.Width + 8;
                I1.Height = RpBackButton.Height + 8;

                I2.Source = B2;
                I2.Width = LpBackButton.Width + 8;
                I2.Height = LpBackButton.Height + 8;

                I3.Source = B3;
                I3.Width = 24;
                I3.Height = 24;

                I4.Source = B3;
                I4.Width = 24;
                I4.Height = 24;

                I5.Source = B4;
                I5.Width = 12;
                I5.Height = 12;

                I6.Source = B5;
                I6.Width = 12;
                I6.Height = 12;

                I7.Source = B6;
                I7.Width = 12;
                I7.Height = 12;


                RpBackButton.Content = I1;
                LpBackButton.Content = I2;

                SwapButton.Content = I7;
                LeftToRightButton.Content = I5;
                RightToLeftButton.Content = I6;

                LpDriveButton.Content = I3;
                RpDriveButton.Content = I4;
            }
            else
            {
                this.Title = "The ASSETS Folder is missing. Some Icons will not display correctly";

            }

            RPgrid.TruncateColumnLength = 30;
            LPgrid.TruncateColumnLength = 30;

            RPgrid.TruncateColumns.Add(1); // truncate the NAME column if its more than 30 characters
            LPgrid.TruncateColumns.Add(1); // truncate the NAME column if its more than 30 characters

            LPgrid.GridItemDoubleClick += LPgrid_GridItemDoubleClick;
            RPgrid.GridItemDoubleClick += RPgrid_GridItemDoubleClick;

            LPgrid.GridItemClick += LPgrid_GridItemClick;
            RPgrid.GridItemClick += RPgrid_GridItemClick;
            
            LPgrid.GridHover += Handle_GridHover;
            RPgrid.GridHover += Handle_GridHover;

            LPgrid.GridContextCalculateSize += Handle_CalculateFolderSize;
            RPgrid.GridContextCalculateSize += Handle_CalculateFolderSize;

            LPgrid.GridContextPermissions += Handle_Permissions;
            RPgrid.GridContextPermissions += Handle_Permissions;

            LPgrid.GridContextCopyPath += Handle_CopyPath;
            RPgrid.GridContextCopyPath += Handle_CopyPath;
            LPgrid.GridContextCopyFullPath += Handle_CopyFullPath;
            RPgrid.GridContextCopyFullPath += Handle_CopyFullPath;

            LPgrid.JustifyColumns.Add(2);
            LPgrid.JustifyColumns.Add(3);
            LPgrid.JustifyColumns.Add(4);
            RPgrid.JustifyColumns.Add(2);
            RPgrid.JustifyColumns.Add(3);
            RPgrid.JustifyColumns.Add(4);
            
            LPpath.KeyUp += LPpath_KeyUp;
            RPpath.KeyUp += RPpath_KeyUp;

            LPfilter.KeyUp += LPfilter_KeyUp;
            RPfilter.KeyUp += RPfilter_KeyUp;
            LPfilterClear.Click += LPfilterClear_Click;
            RPfilterClear.Click += RPfilterClear_Click;

            LPgrid.GridItemClick += (_, _) => UpdateStatusBar();
            RPgrid.GridItemClick += (_, _) => UpdateStatusBar();

            //ChkShowHidden.PointerReleased += ChkShowHidden_Checked;

            if (LPpath.Text != null)
                if (ChkShowHidden != null)
                    FileUtility.PopulateFilePanel(LPgrid, LPpath.Text,
                        ChkShowHidden.IsChecked != null && ChkShowHidden.IsChecked.Value);
            CaptureUnfilteredItems(LPgrid, ref _lpUnfilteredItems);
            if (RPpath.Text != null)
                if (ChkShowHidden != null)
                    FileUtility.PopulateFilePanel(RPgrid, RPpath.Text,
                        ChkShowHidden.IsChecked != null && ChkShowHidden.IsChecked.Value);
            CaptureUnfilteredItems(RPgrid, ref _rpUnfilteredItems);
            UpdateStatusBar();

            // Breadcrumb bar setup — switch from TextBox to breadcrumb mode
            // after initial load so TextBox.Text is reliably set during config loading
            ExitPathEditMode("LP");
            ExitPathEditMode("RP");
            LPbreadcrumbBorder.PointerPressed += (_, _) => EnterPathEditMode("LP");
            RPbreadcrumbBorder.PointerPressed += (_, _) => EnterPathEditMode("RP");
            LPpath.LostFocus += (_, _) => { if (LPpath.IsVisible) ExitPathEditMode("LP"); };
            RPpath.LostFocus += (_, _) => { if (RPpath.IsVisible) ExitPathEditMode("RP"); };

            WireUpButtonHandlers();

            var version = Assembly.GetExecutingAssembly().GetName().Version;
            if (version != null)
                Title = Title + " " + version.ToString();
        }

        private void Handle_GridHover(object? sender, GridHoverItem e)
        {
            if (e.ItemUnderMouse is null)
            {
                ToolTip.SetIsOpen((TaiDataGrid)sender,false);
                
                return;
            }
            
            if (e.ItemUnderMouse is AFileEntry)
            {
                AFileEntry af = (AFileEntry)e.ItemUnderMouse;
                
                if (LastFileHovered != null && LastFileHovered.Name == af.Name)
                {
                    return;
                }
                
                ToolTip.SetIsOpen((TaiDataGrid)sender,false);
                //
                
                SetToolTipForGridItem((TaiDataGrid)sender, af);
            }
    
        }

        private async void Handle_CalculateFolderSize(object? sender, GridHoverItem e)
        {
            if (e.ItemUnderMouse is not AFileEntry entry || !entry.Typ) return;

            var grid = sender as TaiDataGrid;
            var currentPath = grid == LPgrid ? LPpath.Text : RPpath.Text;
            var separator = Path.DirectorySeparatorChar;
            var folderPath = currentPath.TrimEnd(separator) + separator + entry.Name;

            // Show a calculating indicator
            entry.FileSize = "...";
            grid?.ReRender();

            // Run the recursive calculation on a background thread
            long size = await Task.Run(() => FileUtility.GetDirectorySizeRecursive(folderPath));

            // Update the entry with the calculated size
            entry.FileSize = entry.ConvertNumberToReadableString(size);
            grid?.ReRender();
        }

        private async void Handle_Permissions(object? sender, GridHoverItem e)
        {
            if (e.ItemUnderMouse is not AFileEntry entry) return;

            var grid = sender as TaiDataGrid;
            var currentPath = grid == LPgrid ? LPpath.Text : RPpath.Text;
            var separator = Path.DirectorySeparatorChar;
            var filePath = currentPath.TrimEnd(separator) + separator + entry.Name;

            var dialog = new PermissionsDialog(filePath);
            await dialog.ShowDialog(this);
        }

        private async void Handle_CopyPath(object? sender, GridHoverItem e)
        {
            if (e.ItemUnderMouse is not AFileEntry entry) return;

            var grid = sender as TaiDataGrid;
            var currentPath = grid == LPgrid ? LPpath.Text : RPpath.Text;

            if (this.Clipboard != null)
                await this.Clipboard.SetTextAsync(currentPath);
        }

        private async void Handle_CopyFullPath(object? sender, GridHoverItem e)
        {
            if (e.ItemUnderMouse is not AFileEntry entry) return;

            var grid = sender as TaiDataGrid;
            var currentPath = grid == LPgrid ? LPpath.Text : RPpath.Text;
            var separator = Path.DirectorySeparatorChar;
            var fullPath = currentPath.TrimEnd(separator) + separator + entry.Name;

            if (this.Clipboard != null)
                await this.Clipboard.SetTextAsync(fullPath);
        }

        /// <summary>
        /// Populates the buttons in the lower panel.
        /// reads from the configuration.xml file and populates the buttons
        /// </summary>
        private void PopulateTheButtons()
        {
            for (int i = 1; i <= 36; i++)
            {
                var buttonName = $"LPButton{i}";
                var lpButton = this.FindControl<Button>(buttonName);
    
                if (lpButton != null)
                {
                    TheLowerPanelButtons.Add(lpButton);
                }
            }
        }

        /// <summary>
        /// Clears the lower buttons by setting their content and tag to null.
        /// Also clears the content of the DrivePreset buttons.
        /// </summary>
        private void ClearLowerButtons()
        {
            foreach (Button b in TheLowerPanelButtons)
            {
                b.Content = "";
                b.Tag = null;
            }
            
            DrivePreset1A.Content = "";
            DrivePreset1B.Content = "";
            DrivePreset2A.Content = "";
            DrivePreset2B.Content = "";
            DrivePreset3A.Content = "";
            DrivePreset3B.Content = "";
            DrivePreset4A.Content = "";
            DrivePreset4B.Content = "";
            DrivePreset5A.Content = "";
            DrivePreset5B.Content = "";
        }

        /// <summary>
        /// Handles the click event of the "Rename Left Button".
        /// @param sender The source of the event.
        /// @param e The event data.
        /// </summary>
        private void RenameLeftButton_Click(object? sender, RoutedEventArgs e)
        {
            Button B = (Button)sender;
            ToolTip.SetIsOpen(B,false);
            
            bool fileselected = false;

            if (LPgrid.SelectedItems.Count > 0)
            {
                foreach (AFileEntry af in LPgrid.SelectedItems)
                {
                    if (true) //(!af.Typ)
                    {
                        fileselected = true;
                        break;
                    }
                }
            }

            if (!fileselected)
            {
                MessageBox MB = new MessageBox("You have to have a file or a folder selected in the Left panel");
                MB.ShowDialog(this);
                return;
            }

            RenameFileInterface fi = 
                new RenameFileInterface(LPgrid, LPpath.Text+"",
                    RPgrid,RPpath.Text+"", ChkShowHidden.IsChecked.Value);
            fi.Width = 600;
            fi.Height = 180;
            fi.Show(this);
            
        }

        /// <summary>
        /// Handles the Click event of the RenameRightButton.
        /// </summary>
        /// <param name="sender">The object that raises the event.</param>
        /// <param name="e">The
        private void RenameRightButton_Click(object? sender, RoutedEventArgs e)
        {
            Button B = (Button)sender;
            ToolTip.SetIsOpen(B,false);
            
            bool fileselected = false;

            if (RPgrid.SelectedItems.Count > 0)
            {
                foreach (AFileEntry af in RPgrid.SelectedItems)
                {
                    if (true)//(!af.Typ)
                    {
                        fileselected = true;
                        break;
                    }
                }
            }

            if (!fileselected)
            {
                MessageBox MB = new MessageBox("You have to have a file selected in the Right panel");
                MB.ShowDialog(this);
                return;
            }

            
            RenameFileInterface fi = 
                new RenameFileInterface(RPgrid, RPpath.Text+ "",
                    LPgrid,LPpath.Text + "", ChkShowHidden.IsChecked.Value);
            fi.Width = 600;
            fi.Height = 180;
            fi.Show(this);
            
        }

        /// <summary>
        /// This method wire up the event handlers for the buttons in the UI.
        /// </summary>
        private void WireUpButtonHandlers()
        {
            #region Click  Handlers
            
            SwapButton.Click += SwapButton_Click;
            LeftToRightButton.Click += LeftToRightButton_Click;
            RightToLeftButton.Click += RightToLeftButton_Click;

            AllRightButton.Click += AllRightButton_Click;
            AllLeftButton.Click += AllLeftButton_Click;
            
            ClearLeftButton.Click += ClearLeftButton_Click;
            ClearRightButton.Click += ClearRightButton_Click;

            CopyLeftButton.Click += CopyLeftButton_Click;
            CopyRightButton.Click += CopyRightButton_Click;

            MoveLeftButton.Click += MoveLeftButton_Click;  
            MoveRightButton.Click += MoveRightButton_Click;

            RpBackButton.Click += RPBackButton_Click;
            LpBackButton.Click += LPBackButton_Click;

            LpCloudButton.Click += async (_, _) => await ShowCloudRemotesAsync(LpCloudButton, "LP");
            RpCloudButton.Click += async (_, _) => await ShowCloudRemotesAsync(RpCloudButton, "RP");
            
            RenameRightButton.Click += RenameRightButton_Click;
            RenameLeftButton.Click += RenameLeftButton_Click;
            
            DeleteLeftButton.Click += DeleteLeftButton_Click;
            DeleteRightButton.Click += DeleteRightButton_Click;

            MkDirRightButton.Click += MkDirRightButton_Click;
            MkDirLeftButton.Click += MkDirLeftButton_Click;
            
            ArchiveLeftButton.Click += ArchiveLeftButton_Click;
            ArchiveRightButton.Click += ArchiveRightButton_Click;   
            
            
            LpButton1.Click += Handle_Lower_Panel_Button_Clicks;
            LpButton2.Click += Handle_Lower_Panel_Button_Clicks;
            LpButton3.Click += Handle_Lower_Panel_Button_Clicks;
            LpButton4.Click += Handle_Lower_Panel_Button_Clicks;
            LpButton5.Click += Handle_Lower_Panel_Button_Clicks;
            LpButton6.Click += Handle_Lower_Panel_Button_Clicks;
            LpButton7.Click += Handle_Lower_Panel_Button_Clicks;
            LpButton8.Click += Handle_Lower_Panel_Button_Clicks;
            LpButton9.Click += Handle_Lower_Panel_Button_Clicks;
            LpButton10.Click += Handle_Lower_Panel_Button_Clicks;
            LpButton11.Click += Handle_Lower_Panel_Button_Clicks;
            LpButton12.Click += Handle_Lower_Panel_Button_Clicks;
            LpButton13.Click += Handle_Lower_Panel_Button_Clicks;
            LpButton14.Click += Handle_Lower_Panel_Button_Clicks;
            LpButton15.Click += Handle_Lower_Panel_Button_Clicks;
            LpButton16.Click += Handle_Lower_Panel_Button_Clicks;
            LpButton17.Click += Handle_Lower_Panel_Button_Clicks;
            LpButton18.Click += Handle_Lower_Panel_Button_Clicks;
            LpButton19.Click += Handle_Lower_Panel_Button_Clicks;
            LpButton20.Click += Handle_Lower_Panel_Button_Clicks;
            LpButton21.Click += Handle_Lower_Panel_Button_Clicks;
            LpButton22.Click += Handle_Lower_Panel_Button_Clicks;
            LpButton23.Click += Handle_Lower_Panel_Button_Clicks;
            LpButton24.Click += Handle_Lower_Panel_Button_Clicks;
            LpButton25.Click += Handle_Lower_Panel_Button_Clicks;
            LpButton26.Click += Handle_Lower_Panel_Button_Clicks;
            LpButton27.Click += Handle_Lower_Panel_Button_Clicks;
            LpButton28.Click += Handle_Lower_Panel_Button_Clicks;
            LpButton29.Click += Handle_Lower_Panel_Button_Clicks;
            LpButton30.Click += Handle_Lower_Panel_Button_Clicks;
            LpButton31.Click += Handle_Lower_Panel_Button_Clicks;
            LpButton32.Click += Handle_Lower_Panel_Button_Clicks;
            LpButton33.Click += Handle_Lower_Panel_Button_Clicks;
            LpButton34.Click += Handle_Lower_Panel_Button_Clicks;
            LpButton35.Click += Handle_Lower_Panel_Button_Clicks;
            LpButton36.Click += Handle_Lower_Panel_Button_Clicks;
            
            DrivePreset1A.Click += Handle_Drive_Button_Clicks;
            DrivePreset1B.Click += Handle_Drive_Button_Clicks;
            DrivePreset2A.Click += Handle_Drive_Button_Clicks;
            DrivePreset2B.Click += Handle_Drive_Button_Clicks;
            DrivePreset3A.Click += Handle_Drive_Button_Clicks;
            DrivePreset3B.Click += Handle_Drive_Button_Clicks;
            DrivePreset4A.Click += Handle_Drive_Button_Clicks;
            DrivePreset4B.Click += Handle_Drive_Button_Clicks;
            DrivePreset5A.Click += Handle_Drive_Button_Clicks;
            DrivePreset5B.Click += Handle_Drive_Button_Clicks;
            
            

            #endregion

            #region CheckBox Handlers
            
            ChkShowHidden.Checked += ChkShowHidden_Checked;
            ChkShowHidden.Unchecked += ChkShowHidden_Checked;
            
            #endregion
            
            #region Pointer Enter/Leave Handlers

            LpButton1.PointerEntered += Handle_Lower_Panel_Button_PointerEntered;
            LpButton2.PointerEntered += Handle_Lower_Panel_Button_PointerEntered;
            LpButton3.PointerEntered += Handle_Lower_Panel_Button_PointerEntered;
            LpButton4.PointerEntered += Handle_Lower_Panel_Button_PointerEntered;
            LpButton5.PointerEntered += Handle_Lower_Panel_Button_PointerEntered;
            LpButton6.PointerEntered += Handle_Lower_Panel_Button_PointerEntered;
            LpButton7.PointerEntered += Handle_Lower_Panel_Button_PointerEntered;
            LpButton8.PointerEntered += Handle_Lower_Panel_Button_PointerEntered;
            LpButton9.PointerEntered += Handle_Lower_Panel_Button_PointerEntered;
            LpButton10.PointerEntered += Handle_Lower_Panel_Button_PointerEntered;
            LpButton11.PointerEntered += Handle_Lower_Panel_Button_PointerEntered;
            LpButton12.PointerEntered += Handle_Lower_Panel_Button_PointerEntered;
            LpButton13.PointerEntered += Handle_Lower_Panel_Button_PointerEntered;
            LpButton14.PointerEntered += Handle_Lower_Panel_Button_PointerEntered;
            LpButton15.PointerEntered += Handle_Lower_Panel_Button_PointerEntered;
            LpButton16.PointerEntered += Handle_Lower_Panel_Button_PointerEntered;
            LpButton17.PointerEntered += Handle_Lower_Panel_Button_PointerEntered;
            LpButton18.PointerEntered += Handle_Lower_Panel_Button_PointerEntered;
            LpButton19.PointerEntered += Handle_Lower_Panel_Button_PointerEntered;
            LpButton20.PointerEntered += Handle_Lower_Panel_Button_PointerEntered;
            LpButton21.PointerEntered += Handle_Lower_Panel_Button_PointerEntered;
            LpButton22.PointerEntered += Handle_Lower_Panel_Button_PointerEntered;
            LpButton23.PointerEntered += Handle_Lower_Panel_Button_PointerEntered;
            LpButton24.PointerEntered += Handle_Lower_Panel_Button_PointerEntered;
            LpButton25.PointerEntered += Handle_Lower_Panel_Button_PointerEntered;
            LpButton26.PointerEntered += Handle_Lower_Panel_Button_PointerEntered;
            LpButton27.PointerEntered += Handle_Lower_Panel_Button_PointerEntered;
            LpButton28.PointerEntered += Handle_Lower_Panel_Button_PointerEntered;
            LpButton29.PointerEntered += Handle_Lower_Panel_Button_PointerEntered;
            LpButton30.PointerEntered += Handle_Lower_Panel_Button_PointerEntered;
            LpButton31.PointerEntered += Handle_Lower_Panel_Button_PointerEntered;
            LpButton32.PointerEntered += Handle_Lower_Panel_Button_PointerEntered;
            LpButton33.PointerEntered += Handle_Lower_Panel_Button_PointerEntered;
            LpButton34.PointerEntered += Handle_Lower_Panel_Button_PointerEntered;
            LpButton35.PointerEntered += Handle_Lower_Panel_Button_PointerEntered;
            LpButton36.PointerEntered += Handle_Lower_Panel_Button_PointerEntered;

            LpButton1.PointerExited += Handle_Lower_Panel_Button_PointerLeave;
            LpButton2.PointerExited += Handle_Lower_Panel_Button_PointerLeave;
            LpButton3.PointerExited += Handle_Lower_Panel_Button_PointerLeave;
            LpButton4.PointerExited += Handle_Lower_Panel_Button_PointerLeave;
            LpButton5.PointerExited += Handle_Lower_Panel_Button_PointerLeave;
            LpButton6.PointerExited += Handle_Lower_Panel_Button_PointerLeave;
            LpButton7.PointerExited += Handle_Lower_Panel_Button_PointerLeave;
            LpButton8.PointerExited += Handle_Lower_Panel_Button_PointerLeave;
            LpButton9.PointerExited += Handle_Lower_Panel_Button_PointerLeave;
            LpButton10.PointerExited += Handle_Lower_Panel_Button_PointerLeave;
            LpButton11.PointerExited += Handle_Lower_Panel_Button_PointerLeave;
            LpButton12.PointerExited += Handle_Lower_Panel_Button_PointerLeave;
            LpButton13.PointerExited += Handle_Lower_Panel_Button_PointerLeave;
            LpButton14.PointerExited += Handle_Lower_Panel_Button_PointerLeave;
            LpButton15.PointerExited += Handle_Lower_Panel_Button_PointerLeave;
            LpButton16.PointerExited += Handle_Lower_Panel_Button_PointerLeave;
            LpButton17.PointerExited += Handle_Lower_Panel_Button_PointerLeave;
            LpButton18.PointerExited += Handle_Lower_Panel_Button_PointerLeave;
            LpButton19.PointerExited += Handle_Lower_Panel_Button_PointerLeave;
            LpButton20.PointerExited += Handle_Lower_Panel_Button_PointerLeave;
            LpButton21.PointerExited += Handle_Lower_Panel_Button_PointerLeave;
            LpButton22.PointerExited += Handle_Lower_Panel_Button_PointerLeave;
            LpButton23.PointerExited += Handle_Lower_Panel_Button_PointerLeave;
            LpButton24.PointerExited += Handle_Lower_Panel_Button_PointerLeave;
            LpButton25.PointerExited += Handle_Lower_Panel_Button_PointerLeave;
            LpButton26.PointerExited += Handle_Lower_Panel_Button_PointerLeave;
            LpButton27.PointerExited += Handle_Lower_Panel_Button_PointerLeave;
            LpButton28.PointerExited += Handle_Lower_Panel_Button_PointerLeave;
            LpButton29.PointerExited += Handle_Lower_Panel_Button_PointerLeave;
            LpButton30.PointerExited += Handle_Lower_Panel_Button_PointerLeave;
            LpButton31.PointerExited += Handle_Lower_Panel_Button_PointerLeave;
            LpButton32.PointerExited += Handle_Lower_Panel_Button_PointerLeave;
            LpButton33.PointerExited += Handle_Lower_Panel_Button_PointerLeave;
            LpButton34.PointerExited += Handle_Lower_Panel_Button_PointerLeave;
            LpButton35.PointerExited += Handle_Lower_Panel_Button_PointerLeave;
            LpButton36.PointerExited += Handle_Lower_Panel_Button_PointerLeave;

            #endregion
        }

        /// <summary>
        /// Method to handle the Checked event of the ChkShowHidden checkbox. </summary> <param name="sender">The object that raised the event.</param> <param name="e">The RoutedEventArgs containing event data.</param> <returns>Void.</returns>
        /// /
        private void ChkShowHidden_Checked(object? sender, RoutedEventArgs e)
        {
            FileUtility.PopulateFilePanel(LPgrid, LPpath.Text, ChkShowHidden.IsChecked.Value,RbSortName.IsChecked.Value);
            FileUtility.PopulateFilePanel(RPgrid, RPpath.Text, ChkShowHidden.IsChecked.Value,RbSortName.IsChecked.Value);
            RefreshLPGridPostActions();
            RefreshRPGridPostActions();
        }

        private void ArchiveRightButton_Click(object? sender, RoutedEventArgs e)
        {
            Button B = (Button)sender;
            ToolTip.SetIsOpen(B,false);
            
            if (RPgrid.SelectedItems.Count == 0)
            {
                MessageBox MB = new MessageBox("You have to have a file selected in the Right panel");
                MB.ShowDialog(this);
                return;
            }
            
            ThePanelSetup PS = new ThePanelSetup(RPgrid, RPpath.Text, LPgrid, LPpath.Text);
            
            CreateArchive CA = new CreateArchive(PS, ChkShowHidden.IsChecked.Value);

            CA.ShowDialog(this);
        }

        private void ArchiveLeftButton_Click(object? sender, RoutedEventArgs e)
        {
            Button B = (Button)sender;
            ToolTip.SetIsOpen(B,false);
            
            if (LPgrid.SelectedItems.Count == 0)
            {
                MessageBox MB = new MessageBox("You have to have a file selected in the Left panel");
                MB.ShowDialog(this);
                return;
            }   
            
            ThePanelSetup PS = new ThePanelSetup(LPgrid, LPpath.Text, RPgrid, RPpath.Text);
            
            CreateArchive CA = new CreateArchive(PS, ChkShowHidden.IsChecked.Value);

            CA.ShowDialog(this);
            
            
        }

        private void MkDirLeftButton_Click(object? sender, RoutedEventArgs e)
        {
            Button B = (Button)sender;
            ToolTip.SetIsOpen(B,false);
            
            ThePanelSetup PS = new ThePanelSetup(LPgrid, LPpath.Text, RPgrid, RPpath.Text);
            
            CreateFolder CF = new CreateFolder(PS, ChkShowHidden.IsChecked.Value);

            CF.ShowDialog(this);
        }

        private void MkDirRightButton_Click(object? sender, RoutedEventArgs e)
        {
            Button B = (Button)sender;
            ToolTip.SetIsOpen(B,false);
            
            ThePanelSetup PS = new ThePanelSetup(RPgrid, RPpath.Text, LPgrid, LPpath.Text);
            
            CreateFolder CF = new CreateFolder(PS, ChkShowHidden.IsChecked.Value);

            CF.ShowDialog(this);
        }

        private void DeleteRightButton_Click(object? sender, RoutedEventArgs e)
        {
            Button B = (Button)sender;
            ToolTip.SetIsOpen(B,false);
            
            if (RPgrid.SelectedItems.Count == 0)
            {
                MessageBox MB = new MessageBox("You have to have a file selected in the Right panel");
                MB.ShowDialog(this);
                return;
            }
            
            // Here vwe want to iterate over the selected items and delete them

            DeleteFilesDialog df = new DeleteFilesDialog(RPgrid.SelectedItems, RPpath.Text, RPgrid,
                LPpath.Text, LPgrid, ChkShowHidden.IsChecked.Value);

            df.ShowDialog(this);
        }

        private void DeleteLeftButton_Click(object? sender, RoutedEventArgs e)
        {
            Button B = (Button)sender;
            ToolTip.SetIsOpen(B,false);
            
            if (LPgrid.SelectedItems.Count == 0)
            {
                MessageBox MB = new MessageBox("You have to have a file selected in the Left panel");
                MB.ShowDialog(this);
                return;
            }
            
            // Here vwe want to iterate over the selected items and delete them

            DeleteFilesDialog df = new DeleteFilesDialog(LPgrid.SelectedItems, LPpath.Text, LPgrid,
                RPpath.Text, RPgrid, ChkShowHidden.IsChecked.Value);

            df.ShowDialog(this); 
        }

        private void AllLeftButton_Click(object? sender, RoutedEventArgs e)
        {
            Button B = (Button)sender;
            ToolTip.SetIsOpen(B,false);
            
            LPgrid.SelectAllFilesOnly();
        }

        private void AllRightButton_Click(object? sender, RoutedEventArgs e)
        {
            Button B = (Button)sender;
            ToolTip.SetIsOpen(B,false);
            
            RPgrid.SelectAllFilesOnly();
        }

        private void Handle_Drive_Button_Clicks(object? sender, RoutedEventArgs e)
        {
            Console.WriteLine("made it here");
            if (sender is null)
            {
                return;
            }

            Button bb = (Button)sender;
            
            string nm = bb.Name;

            DriveButtonEntry dbe = (DriveButtonEntry)bb.Tag;

            // Push current path to history before navigating
            if (nm.EndsWith("A") && !string.IsNullOrEmpty(LPpath.Text))
                _lpHistory.Push(LPpath.Text);
            else if (!string.IsNullOrEmpty(RPpath.Text))
                _rpHistory.Push(RPpath.Text);

            switch (dbe.Path.ToUpper())
            {
                case "$HOME":
                    if (nm.EndsWith("A")) // Left
                    {
                        LPpath.Text = GetHomeDirectoryPath();
                        if (ChkShowHidden != null) 
                            FileUtility.PopulateFilePanel(LPgrid, LPpath.Text, ChkShowHidden.IsChecked.Value);
                    }
                    else // Right
                    {
                        RPpath.Text = GetHomeDirectoryPath();
                        if (ChkShowHidden != null) 
                            FileUtility.PopulateFilePanel(RPgrid, RPpath.Text, ChkShowHidden.IsChecked.Value);
                    }

                    break;
                case "$ROOT":
                    if (nm.EndsWith("A")) // Left
                    {
                        LPpath.Text = GetRootDirectoryPath();
                        if (ChkShowHidden != null) 
                            FileUtility.PopulateFilePanel(LPgrid, LPpath.Text, ChkShowHidden.IsChecked.Value); 
                    }
                    else // Right
                    {
                        RPpath.Text = GetRootDirectoryPath();
                        if (ChkShowHidden != null) 
                            FileUtility.PopulateFilePanel(RPgrid, RPpath.Text, ChkShowHidden.IsChecked.Value); 
                    }

                    break;
                case "$DESKTOP":
                    if (nm.EndsWith("A")) // Left
                    {
                        LPpath.Text = GetDesktopDirectoryPath();
                        if (ChkShowHidden != null) 
                            FileUtility.PopulateFilePanel(LPgrid, LPpath.Text, ChkShowHidden.IsChecked.Value);
                    }
                    else // Right
                    {
                        RPpath.Text = GetDesktopDirectoryPath();
                        if (ChkShowHidden != null) 
                            FileUtility.PopulateFilePanel(RPgrid, RPpath.Text, ChkShowHidden.IsChecked.Value); 
                    }

                    break;
                case "$DOCUMENTS":
                    if (nm.EndsWith("A")) // Left
                    {
                        LPpath.Text = GetDocumentsDirectoryPath();
                        if (ChkShowHidden != null) 
                            FileUtility.PopulateFilePanel(LPgrid, LPpath.Text, ChkShowHidden.IsChecked.Value);
                    }
                    else // Right
                    {
                        RPpath.Text = GetDocumentsDirectoryPath();
                        if (ChkShowHidden != null) 
                            FileUtility.PopulateFilePanel(RPgrid, RPpath.Text, ChkShowHidden.IsChecked.Value);
                    }

                    break;
                case "$PICTURES":
                    if (nm.EndsWith("A")) // Left
                    {
                        LPpath.Text = GetPicturesDirectoryPath();
                        if (ChkShowHidden != null) 
                            FileUtility.PopulateFilePanel(LPgrid, LPpath.Text, ChkShowHidden.IsChecked.Value);
                    }
                    else // Right
                    {
                        RPpath.Text = GetPicturesDirectoryPath();
                        if (ChkShowHidden != null) 
                            FileUtility.PopulateFilePanel(RPgrid, RPpath.Text, ChkShowHidden.IsChecked.Value);
                    }

                    break;
                case "$DOWNLOADS":
                    if (nm.EndsWith("A")) // Left
                    {
                        LPpath.Text = GetPicturesDirectoryPath();
                        if (ChkShowHidden != null) 
                            FileUtility.PopulateFilePanel(LPgrid, LPpath.Text, ChkShowHidden.IsChecked.Value);
                    }
                    else // Right
                    {
                        RPpath.Text = GetPicturesDirectoryPath();
                        if (ChkShowHidden != null) 
                            FileUtility.PopulateFilePanel(RPgrid, RPpath.Text, ChkShowHidden.IsChecked.Value);
                    }

                    break;
                default:
                    if (nm.EndsWith("A")) // Left
                    {
                        LPpath.Text = dbe.Path;
                        if (ChkShowHidden != null) 
                            FileUtility.PopulateFilePanel(LPgrid, LPpath.Text, ChkShowHidden.IsChecked.Value);
                    }
                    else // Right
                    {
                        RPpath.Text = dbe.Path;
                        if (ChkShowHidden != null) 
                            FileUtility.PopulateFilePanel(RPgrid, RPpath.Text, ChkShowHidden.IsChecked.Value);
                    }

                    break;
            }

            if (nm.EndsWith("A"))
            {
                LPfilter.Text = "";
                RefreshLPGridPostActions();
            }
            else
            {
                RPfilter.Text = "";
                RefreshRPGridPostActions();
            }
        }

        private void Handle_Lower_Panel_Button_PointerLeave(object? sender, PointerEventArgs e)
        {
            //if (_pop != null && _pop.IsVisible )
            //{
            //    _pop.Close();
            //    _pop = null;
            //}
            
            //LastButtonPopupName = "";
            
            KillPop();
        }

        private void KillPop()
        {
            if (_pop != null && _pop.IsVisible )
            {
                _pop.Close();
                _pop = null;
            }
            
            LastButtonPopupName = "";
        }

        private void Handle_Lower_Panel_Button_PointerEntered(object? sender, PointerEventArgs e)
        {
            if (sender is null)
            {
                return;
            }

            Button B = (Button)sender;

            string nm = B.Name;

            foreach (ButtonEntry item in TheButtons)
            {
                if (item.Bname.ToUpper() == nm.ToUpper() && (item.ToolTip + "") !="" && LastButtonPopupName != B.Name)
                {
                    // We have a winner - do the action

                    SetToolTipForItem(B, item);

                    break;
                }
            }
        }

        private void SetToolTipForItem(Button B, ButtonEntry item)
        {
            LastButtonPopupName = B.Name;
            ToolTip.SetPlacement(B, PlacementMode.Top); // this is a hack to get the tooltip to show up in the right place
            ToolTip.SetHorizontalOffset(B,10.0);
            ToolTip.SetVerticalOffset(B,10.0);
            ToolTip.SetTip(B, item.ToolTip);
            ToolTip.SetIsOpen(B, true);
        }
        
        private void KillToolTipForItem(Button B)
        {
            LastButtonPopupName = B.Name;
            ToolTip.SetPlacement(B, PlacementMode.Top); // this is a hack to get the tooltip to show up in the right place
            ToolTip.SetHorizontalOffset(B,10.0);
            ToolTip.SetVerticalOffset(B,10.0);
            ToolTip.SetTip(B, "");
            ToolTip.SetIsOpen(B, false);
        }
        
        private void SetToolTipForGridItem(TaiDataGrid grid, AFileEntry item)
        {
            if (LastFileHovered != null && LastFileHovered.Name == item.Name)
            {
                return;
            }
            else 
            {

                ToolTip.SetHorizontalOffset(grid, 10.0);
                ToolTip.SetVerticalOffset(grid, 10.0);
                if (item.Typ)
                    ToolTip.SetTip(grid, "Folder: " + item.Name);
                else
                    ToolTip.SetTip(grid, "File: " + item.Name);

                ToolTip.SetIsOpen(grid, true);

                LastFileHovered = item;
            }
        }
        
        private void RightToLeftButton_Click(object? sender, RoutedEventArgs e)
        {
            Button B = (Button)sender;
            ToolTip.SetIsOpen(B,false);

            LPpath.Text = RPpath.Text;
            LPfilter.Text = "";
            if (LPpath.Text != null)
                if (ChkShowHidden != null)
                    FileUtility.PopulateFilePanel(LPgrid, LPpath.Text, ChkShowHidden.IsChecked.Value);
            RefreshLPGridPostActions();
        }

        private void LeftToRightButton_Click(object? sender, RoutedEventArgs e)
        {
            Button B = (Button)sender;
            ToolTip.SetIsOpen(B,false);

            RPpath.Text = LPpath.Text;
            RPfilter.Text = "";
            if (RPpath.Text != null)
                if (ChkShowHidden != null)
                    FileUtility.PopulateFilePanel(RPgrid, RPpath.Text, ChkShowHidden.IsChecked.Value);
            RefreshRPGridPostActions();
        }

        private string _rpPathBeforeEdit = "";
        private string _lpPathBeforeEdit = "";

        private void RPpath_KeyUp(object? sender, KeyEventArgs e)
        {
            if (e.Key == Key.Enter)
            {
                if (!string.IsNullOrEmpty(_rpPathBeforeEdit) && _rpPathBeforeEdit != RPpath.Text)
                    _rpHistory.Push(_rpPathBeforeEdit);
                RPfilter.Text = "";
                if (RPpath.Text != null)
                    if (ChkShowHidden != null)
                        FileUtility.PopulateFilePanel(RPgrid, RPpath.Text, ChkShowHidden.IsChecked.Value);
                RefreshRPGridPostActions();
                ExitPathEditMode("RP");
            }
            else if (e.Key == Key.Escape)
            {
                ExitPathEditMode("RP");
            }
        }

        private void LPpath_KeyUp(object? sender, KeyEventArgs e)
        {
            if (e.Key == Key.Enter)
            {
                if (!string.IsNullOrEmpty(_lpPathBeforeEdit) && _lpPathBeforeEdit != LPpath.Text)
                    _lpHistory.Push(_lpPathBeforeEdit);
                LPfilter.Text = "";
                if (LPpath.Text != null)
                    if (ChkShowHidden != null)
                        FileUtility.PopulateFilePanel(LPgrid, LPpath.Text, ChkShowHidden.IsChecked.Value);
                RefreshLPGridPostActions();
                ExitPathEditMode("LP");
            }
            else if (e.Key == Key.Escape)
            {
                ExitPathEditMode("LP");
            }
        }

        private void ClearRightButton_Click(object? sender, RoutedEventArgs e)
        {
            Button B = (Button)sender;
            ToolTip.SetIsOpen(B,false);
            
            RPgrid.SelectedItems.Clear();
            RPgrid.ReRender();
        }

        private void ClearLeftButton_Click(object? sender, RoutedEventArgs e)
        {
            Button B = (Button)sender;
            ToolTip.SetIsOpen(B,false);
            
            LPgrid.SelectedItems.Clear();
            LPgrid.ReRender();
        }

        private void Handle_Lower_Panel_Button_Clicks(object? sender, RoutedEventArgs e)
        {
            // This will handle all the lower panel button clicks
            if (sender is null)
            {
                return;
            }
            
            Button B = (Button)sender;
            KillToolTipForItem(B);

            string nm = B.Name;

            foreach (ButtonEntry item in TheButtons)
            {
                if (item.Bname.ToUpper() == nm.ToUpper())
                {
                    // We have a winner - do the action
                    
                    if (item.Bcontent.ToUpper().Trim() == "%BUTTONCONFIG%")
                    {
                        // we need to open the button config window
                        AddEditCmdButtonDefinition BC = new AddEditCmdButtonDefinition(TheButtonSettings);
                        BC.TheMainWindow = this;
                        BC.ShowDialog(this);
                        break;
                    }
                    if (item.Bcontent.ToUpper().Trim() == "%DRIVEINFO%")
                    {
                        DriveInfoDialog DI = new DriveInfoDialog();
                        DI.ShowDialog(this);
                        break;
                    }
                    if (item.Bcontent.ToUpper().Trim() == "%RCLONEDIAG%")
                    {
                        ShowRcloneDiagnostics();
                        break;
                    }
                    if (item.Bcontent.ToUpper().Trim() == "%RCLONECONFIG%")
                    {
                        ShowRcloneConfig();
                        break;
                    }
                    string newaction = ParseTheArgs(item.Bargs);

                    if (newaction != "%ERROR%")
                    {
                        try
                        {
                            if (newaction.Contains(","))
                            {
                                // we have comma seperated arguments so
                                // we need to split them up and pass them
                                // to the process start info one at a time

                                string[] args = newaction.Split(',', StringSplitOptions.RemoveEmptyEntries);

                                foreach (string arg in args)
                                {
                                    StartDetachedProcess(item.Bcontent, arg, item.ShellExecute, item.ShowWindow);
                                }
                            }
                            else
                            {
                                StartDetachedProcess(item.Bcontent.Trim(), newaction, item.ShellExecute, item.ShowWindow);
                            }
                        }
                        catch (Exception ex)
                        {
                            ProgressWindow PW = new ProgressWindow();

                            PW.MessageText.Text = "Error: " + ex.Message;

                            PW.ShowDialog(this);
                        }
                    }

                    break;

                }
            }
        }
        
        private void StartDetachedProcess(string fileName, string arguments, bool useShellExecute, bool createNoWindow)
        {
            try
            {
                if (Environment.OSVersion.Platform == PlatformID.Unix ||
                    Environment.OSVersion.Platform == PlatformID.MacOSX)
                {
                    // Check if the program exists before attempting to launch
                    if (!File.Exists(fileName))
                    {
                        // Not an absolute path — check if it's on PATH using 'which'
                        var whichInfo = new ProcessStartInfo()
                        {
                            FileName = "/usr/bin/which",
                            Arguments = fileName,
                            UseShellExecute = false,
                            RedirectStandardOutput = true,
                            CreateNoWindow = true
                        };
                        var whichProc = Process.Start(whichInfo);
                        whichProc?.WaitForExit(3000);
                        if (whichProc == null || whichProc.ExitCode != 0)
                        {
                            MessageBox mb = new MessageBox($"Program not found: {fileName}\nMake sure it is installed and available on your PATH.");
                            mb.ShowDialog(this);
                            return;
                        }
                    }

                    // Launch the process directly with proper argument handling
                    // instead of routing through /bin/sh which can mangle arguments
                    var startInfo = new ProcessStartInfo()
                    {
                        FileName = fileName,
                        UseShellExecute = false,
                        CreateNoWindow = true,
                        RedirectStandardOutput = true,
                        RedirectStandardError = true
                    };

                    // Parse arguments respecting quoted strings and add them individually
                    foreach (var arg in SplitArguments(arguments))
                    {
                        startInfo.ArgumentList.Add(arg);
                    }

                    var process = Process.Start(startInfo);
                }
                else // Windows
                {
                    // Check if the program exists before attempting to launch
                    if (!File.Exists(fileName))
                    {
                        // Check if it's on PATH using 'where'
                        var whereInfo = new ProcessStartInfo()
                        {
                            FileName = "where",
                            Arguments = fileName,
                            UseShellExecute = false,
                            RedirectStandardOutput = true,
                            CreateNoWindow = true
                        };
                        var whereProc = Process.Start(whereInfo);
                        whereProc?.WaitForExit(3000);
                        if (whereProc == null || whereProc.ExitCode != 0)
                        {
                            MessageBox mb = new MessageBox($"Program not found: {fileName}\nMake sure it is installed and available on your PATH.");
                            mb.ShowDialog(this);
                            return;
                        }
                    }

                    // On Windows with UseShellExecute, ArgumentList is not supported
                    // so build a properly quoted Arguments string instead
                    var parsedArgs = SplitArguments(arguments);
                    var quotedArgs = string.Join(" ", parsedArgs.Select(a => a.Contains(' ') ? $"\"{a}\"" : a));

                    var startInfo = new ProcessStartInfo()
                    {
                        FileName = fileName,
                        Arguments = quotedArgs,
                        UseShellExecute = true,
                        CreateNoWindow = createNoWindow
                    };

                    var process = Process.Start(startInfo);
                }
            }
            catch (Exception ex)
            {
                MessageBox mb = new MessageBox($"Failed to start process: {fileName}\nError: {ex.Message}");
                mb.ShowDialog(this);
            }
        }

        private static string QuoteIfNeeded(string path)
        {
            if (path.Contains(' ')) return $"\"{path}\"";
            return path;
        }

        private static List<string> SplitArguments(string arguments)
        {
            var args = new List<string>();
            if (string.IsNullOrWhiteSpace(arguments)) return args;

            var current = new System.Text.StringBuilder();
            bool inQuotes = false;

            for (int i = 0; i < arguments.Length; i++)
            {
                char c = arguments[i];

                if (c == '"' || c == '\'')
                {
                    inQuotes = !inQuotes;
                }
                else if (c == ' ' && !inQuotes)
                {
                    if (current.Length > 0)
                    {
                        args.Add(current.ToString());
                        current.Clear();
                    }
                }
                else
                {
                    current.Append(c);
                }
            }

            if (current.Length > 0)
                args.Add(current.ToString());

            return args;
        }

        private string ParseTheArgs(string bcontent)
        {
            // this will attempt to parse the bcontent string and
            // return the action to be performed

            // Single folder selected in either panel
            if (bcontent.Contains("%FD%"))
            {
                // we are looking for the first folder selected in
                // either the right or left panel

                string PTH = "";

                if ( LPgrid.GetFirstSelectedFolder() != "")
                {
                    // the left grid has a folder selected

                    if (LPpath.Text != null) PTH = QuoteIfNeeded(MakePathEnvSafe(LPpath.Text) + LPgrid.GetFirstSelectedFolder());

                    string ret = bcontent.Replace("%FD%", PTH);

                    return ret;

                }
                else if (RPgrid.GetFirstSelectedFolder() != "")
                {
                    // the right grid has a folder selected

                    if (RPpath.Text != null) PTH = QuoteIfNeeded(MakePathEnvSafe(RPpath.Text) + RPgrid.GetFirstSelectedFolder());

                    string ret = bcontent.Replace("%FD%", PTH);

                    return ret;
                }
                else
                {
                    // neither grid has a folder selected
                    // do nothing
                    ProgressWindow PW =
                    new ProgressWindow("Error",
                    "You have to have at least one Folder selected in either Pane");

                    PW.ShowDialog(this);

                    //PW.Close();

                    return "%ERROR%";
                }
            }

            // Single file selected in either panel
            if (bcontent.Contains("%AF%"))
            {
                string PTH = "";

                if (LPgrid.GetListOfSelectedFiles().Count > 0)
                {
                    // the left grid has some files selected

                    foreach(AFileEntry af in LPgrid.GetListOfSelectedFiles())
                    {
                        PTH += QuoteIfNeeded(MakePathEnvSafe(LPpath.Text) + af.Name) + " ";
                    }

                    string ret = bcontent.Replace("%AF%", PTH);

                    return ret;

                }
                else if (RPgrid.GetListOfSelectedFiles().Count > 0)
                {
                    // the left grid has some files selected

                    foreach (AFileEntry af in RPgrid.GetListOfSelectedFiles())
                    {
                        PTH += QuoteIfNeeded(MakePathEnvSafe(RPpath.Text) + af.Name) + " ";
                    }

                    string ret = bcontent.Replace("%AF%", PTH);

                    return ret;

                }
                else
                {
                    // neither grid has a folder selected
                    // do nothing
                    ProgressWindow PW =
                    new ProgressWindow("Error",
                    "You have to have at least one file selected in either pane");

                    PW.ShowDialog(this);

                    //PW.Close();

                    return "%ERROR%";
                }

            }

            // List of files Selected in either panel Left First
            if (bcontent.Contains("%LAF%"))
            {
                string PTH = "";

                if (LPgrid.GetListOfSelectedFiles().Count > 0)
                {
                    // the left grid has some files selected

                    foreach (AFileEntry af in LPgrid.GetListOfSelectedFiles())
                    {
                        PTH += QuoteIfNeeded(MakePathEnvSafe(LPpath.Text) + af.Name) + ",";
                    }

                    string ret = bcontent.Replace("%LAF%", PTH);

                    return ret;

                }
                else if (RPgrid.GetListOfSelectedFiles().Count > 0)
                {
                    // the left grid has some files selected

                    foreach (AFileEntry af in RPgrid.GetListOfSelectedFiles())
                    {
                        PTH += QuoteIfNeeded(MakePathEnvSafe(RPpath.Text) + af.Name) + ",";
                    }

                    string ret = bcontent.Replace("%LAF%", PTH);

                    return ret;

                }
                else
                {
                    // neither grid has a folder selected
                    // do nothing


                    ProgressWindow PW = 
                        new ProgressWindow("Error", 
                        "You have to have at least one file selected in either pane");

                    PW.ShowDialog(this);

                    //PW.Close();

                    return "%ERROR%";
                }

            }

            // First file in the left panel and first file in the right panel Order is unimportant
            if (bcontent.Contains("%LF1%") && bcontent.Contains("%RF1%"))
            {
                string PTH = "";

                if (LPgrid.GetListOfSelectedFiles().Count > 0 &&
                    RPgrid.GetListOfSelectedFiles().Count > 0)
                {
                    // the left grid has some files selected

                    List<AFileEntry> thelista = LPgrid.GetListOfSelectedFiles();
                    List<AFileEntry> thelistb = RPgrid.GetListOfSelectedFiles();

                    PTH = QuoteIfNeeded(MakePathEnvSafe(LPpath.Text) + thelista[0].Name) + " " + QuoteIfNeeded(MakePathEnvSafe(RPpath.Text) + thelistb[0].Name);

                    string ret = bcontent.Replace("%LF1%", PTH).Replace("%RF1%","");

                    return ret;

                }                
                else
                {
                    // neither grid has a folder selected
                    // do nothing

                    ProgressWindow PW = new ProgressWindow("Error","You have to have a file selected in each panel");

                    PW.ShowDialog(this);

                    //PW.Close();

                    return "%ERROR%";
                }

            }

            // First file in the left panel only
            if (bcontent.Contains("%LF1%"))
            {
                string PTH = "";

                if (LPgrid.GetListOfSelectedFiles().Count > 0)
                {
                    // the left grid has some files selected

                    List<AFileEntry> thelista = LPgrid.GetListOfSelectedFiles();

                    PTH = QuoteIfNeeded(MakePathEnvSafe(LPpath.Text) + thelista[0].Name);

                    string ret = bcontent.Replace("%LF1%", PTH);

                    return ret;

                }
                else
                {
                    // neither grid has a folder selected
                    // do nothing

                    ProgressWindow PW = new ProgressWindow("Error", "You have to have a file selected in the left panel");

                    PW.ShowDialog(this);

                    //PW.Close();

                    return "%ERROR%";
                }

            }

            // First file in the right panel only
            if (bcontent.Contains("%RF1%"))
            {
                string PTH = "";

                if (RPgrid.GetListOfSelectedFiles().Count > 0)
                {
                    // the left grid has some files selected

                    List<AFileEntry> thelista = RPgrid.GetListOfSelectedFiles();

                    PTH = QuoteIfNeeded(MakePathEnvSafe(RPpath.Text) + thelista[0].Name);

                    string ret = bcontent.Replace("%RF1%", PTH);

                    return ret;

                }
                else
                {
                    // neither grid has a folder selected
                    // do nothing

                    ProgressWindow PW = new ProgressWindow("Error", "You have to have a file selected in the right panel");

                    PW.ShowDialog(this);

                    //PW.Close();

                    return "%ERROR%";
                }

            }
            
            // List of Files selected in the right panel
            if (bcontent.Contains("%RPAF%"))
            {
                string PTH = "";

                if (RPgrid.GetListOfSelectedFiles().Count > 0)
                {
                    // the left grid has some files selected

                    foreach (AFileEntry af in RPgrid.GetListOfSelectedFiles())
                    {
                        PTH += QuoteIfNeeded(MakePathEnvSafe(RPpath.Text) + af.Name) + ",";
                    }

                    string ret = bcontent.Replace("%RPAF%", PTH);

                    return ret;

                }
                else
                {
                    // neither grid has a folder selected
                    // do nothing

                    ProgressWindow PW = new ProgressWindow("Error", "You have to have at least one file selected in the right panel");

                    PW.ShowDialog(this);

                    //PW.Close();

                    return "%ERROR%";
                }

            }
            
            // List of files selected in the left panel
            if (bcontent.Contains("%LPAF%"))
            {
                string PTH = "";

                if (LPgrid.GetListOfSelectedFiles().Count > 0)
                {
                    // the left grid has some files selected

                    foreach (AFileEntry af in LPgrid.GetListOfSelectedFiles())
                    {
                        PTH += QuoteIfNeeded(MakePathEnvSafe(LPpath.Text) + af.Name) + ",";
                    }

                    string ret = bcontent.Replace("%LPAF%", PTH);

                    return ret;

                }
                else
                {
                    // neither grid has a folder selected
                    // do nothing

                    ProgressWindow PW = new ProgressWindow("Error", "You have to have at least one file selected in the left panel");

                    PW.ShowDialog(this);

                    //PW.Close();

                    return "%ERROR%";
                }

            }
            
            // Path to the files shown in the left panel
            if (bcontent.Contains("%LPATH%"))
            {
                string ret = bcontent.Replace("%LPATH%", QuoteIfNeeded(MakePathEnvSafe(LPpath.Text)));

                return ret;
            }

            // Path to the files shown in the right panel
            if (bcontent.Contains("%RPATH%"))
            {
                string ret = bcontent.Replace("%RPATH%", QuoteIfNeeded(MakePathEnvSafe(RPpath.Text)));

                return ret;
            }   
            
            
            
            return bcontent;
        }

        private void SwapButton_Click(object? sender, RoutedEventArgs e)
        {
            Button B = (Button)sender;
            ToolTip.SetIsOpen(B,false);
            
            var p1 = LPpath.Text;
            var p2 = RPpath.Text;

            LPpath.Text = p2; 
            RPpath.Text = p1;

            RefreshLPGrid();
            RefreshRPGrid();
        }

        private void RPgrid_GridItemClick(object? sender, GridHoverItem e)
        {
            // If there are any selected items in the Left Grid Deselect them

            //LPgrid.SelectedItems.Clear();
            //LPgrid.ReRender();
        }

        private void LPgrid_GridItemClick(object? sender, GridHoverItem e)
        {
            // If there are any selected items in the Right Grid Deselect them

            //RPgrid.SelectedItems.Clear();
            //RPgrid.ReRender();
        }

        private async void MoveRightButton_Click(object? sender, RoutedEventArgs e)
        {
            Button B = (Button)sender;
            ToolTip.SetIsOpen(B, false);

            await RunPanelTransferAsync(LPgrid.SelectedItems, LPpath.Text, RPpath.Text, move: true);
        }

        /// <summary>
        /// Builds a transfer batch from the selected entries and runs it through a modal
        /// <see cref="TransferProgressWindow"/> (off-thread, with live progress and Cancel),
        /// then refreshes both panels and surfaces any error.
        /// </summary>
        private async Task RunPanelTransferAsync(IList<object> selected, string sourcePathRaw, string targetPathRaw, bool move)
        {
            if (selected == null || selected.Count == 0) return;

            string spath = AppendSeparator(sourcePathRaw.Replace(@"\\", @"\"));
            string tpath = AppendSeparator(targetPathRaw.Replace(@"\\", @"\"));

            var items = new List<TransferItem>();
            foreach (var obj in selected)
            {
                if (obj is not AFileEntry item) continue;
                string source = spath + item.Name;
                string targetPath = tpath + item.Name;
                items.Add(new TransferItem(source, tpath, targetPath, item.Typ));
            }
            if (items.Count == 0) return;

            var win = new TransferProgressWindow(move ? "Moving" : "Copying", items, move);
            await win.ShowDialog(this);

            RefreshLPGrid();
            RefreshRPGrid();

            if (win.Error != null)
                await new MessageBox($"Transfer failed: {win.Error.Message}").ShowDialog(this);
        }

        private static string AppendSeparator(string p)
        {
            if (Environment.OSVersion.Platform == PlatformID.Win32NT)
                return p.EndsWith(@"\") ? p : p + @"\";
            return p.EndsWith("/") ? p : p + "/";
        }

        private string MakePathEnvSafe(string path)
        {
            string result = path.Replace(@"\\", @"\");

            if (Environment.OSVersion.Platform == PlatformID.Win32NT)
            {
                result = result.Replace(@"/", @"\");
                
                if (!result.EndsWith(@"\"))
                {
                    result += @"\\";

                }
                               
            }
            else if (Environment.OSVersion.Platform == PlatformID.Unix || Environment.OSVersion.Platform == PlatformID.MacOSX)
            {
                if (!result.EndsWith(@"/"))
                {
                    result += @"/";

                }
                
            }

            return result;

        }

        private async void MoveLeftButton_Click(object? sender, RoutedEventArgs e)
        {
            Button B = (Button)sender;
            ToolTip.SetIsOpen(B, false);

            await RunPanelTransferAsync(RPgrid.SelectedItems, RPpath.Text, LPpath.Text, move: true);
        }

        private async void CopyRightButton_Click(object? sender, RoutedEventArgs e)
        {
            Button B = (Button)sender;
            ToolTip.SetIsOpen(B, false);

            await RunPanelTransferAsync(LPgrid.SelectedItems, LPpath.Text, RPpath.Text, move: false);
        }

        private async void CopyLeftButton_Click(object? sender, RoutedEventArgs e)
        {
            Button B = (Button)sender;
            ToolTip.SetIsOpen(B, false);

            await RunPanelTransferAsync(RPgrid.SelectedItems, RPpath.Text, LPpath.Text, move: false);
        }

        private Bitmap LoadImage(string base64)
        {
            byte[] bytes = Convert.FromBase64String(base64);

            using (var memoryStream = new MemoryStream(bytes))
            {
                return Bitmap.DecodeToWidth(memoryStream, 32);
            }
        }

        private void LPBackButton_Click(object? sender, RoutedEventArgs e)
        {
            if (_lpHistory.Count > 0)
            {
                LPpath.Text = _lpHistory.Pop();
            }
            else
            {
                // Fallback: navigate up the directory tree
                var parent = Path.GetDirectoryName(LPpath.Text);
                if (!string.IsNullOrEmpty(parent))
                    LPpath.Text = parent;
            }

            LPfilter.Text = "";
            if (ChkShowHidden != null) FileUtility.PopulateFilePanel(LPgrid, LPpath.Text, ChkShowHidden.IsChecked.Value);
            RefreshLPGridPostActions();
        }

        private void RPBackButton_Click(object? sender, RoutedEventArgs e)
        {
            if (_rpHistory.Count > 0)
            {
                RPpath.Text = _rpHistory.Pop();
            }
            else
            {
                // Fallback: navigate up the directory tree
                var parent = Path.GetDirectoryName(RPpath.Text);
                if (!string.IsNullOrEmpty(parent))
                    RPpath.Text = parent;
            }

            RPfilter.Text = "";
            if (ChkShowHidden != null) FileUtility.PopulateFilePanel(RPgrid, RPpath.Text, ChkShowHidden.IsChecked.Value);
            RefreshRPGridPostActions();
        }

        private void RPgrid_GridItemDoubleClick(object? sender, GridHoverItem e)
        {
            var it = e.ItemUnderMouse as AFileEntry;

            // Kill the tooltip if any
            if (sender != null)
                ToolTip.SetIsOpen((TaiDataGrid)sender,false);
            
            if (Environment.OSVersion.Platform == PlatformID.Win32NT)
            {
                if (it.Typ)
                {
                    _rpHistory.Push(RPpath.Text);
                    RPpath.Text = JoinChildPath(RPpath.Text, it.Name);
                    if (ChkShowHidden != null) FileUtility.PopulateFilePanel(RPgrid, RPpath.Text, ChkShowHidden.IsChecked.Value,RbSortName.IsChecked.Value);
                }
                else
                {
                    // its an actual file so can we execute it?

                    if (FileExtensionIsExecutable(it.Name.ToUpper()))
                    {
                        // we can execute it

                        string thingtoexecute = (RPpath.Text + "\\" + it.Name).Replace(@"\\", @"\");

                        Process.Start(new ProcessStartInfo()
                        {
                            FileName = thingtoexecute,
                            UseShellExecute = true,
                        });

                        //Process.Start((RPpath.Text + "\\" + it.Name).Replace(@"\\", @"\"));
                    }

                }
            }
            else if (Environment.OSVersion.Platform == PlatformID.Unix || Environment.OSVersion.Platform == PlatformID.MacOSX)
            {
                if (it.Typ)
                {
                    _rpHistory.Push(RPpath.Text);

                    RPpath.Text = JoinChildPath(RPpath.Text, it.Name);
                    if (ChkShowHidden != null)
                        FileUtility.PopulateFilePanel(RPgrid, RPpath.Text, ChkShowHidden.IsChecked.Value);
                }
                else
                {
                    string thingtoexecute = (RPpath.Text + "/" + it.Name);

                    if (RuntimeInformation.IsOSPlatform(OSPlatform.OSX))
                    {
                        // macOS — use 'open' which delegates to Launch Services
                        OpenFileWithDefaultApp(thingtoexecute);
                    }
                    else if (FileExtensionIsExecutable(it.Name.ToUpper()) || IsExecutableOnUnixNet6(thingtoexecute))
                    {
                        // Linux — try xdg-open for known types, fall back to direct execution
                        OpenFileWithDefaultApp(thingtoexecute);
                    }
                }
            }

            if (it.Typ)
            {
                RPfilter.Text = "";
                RefreshRPGridPostActions();
            }
        }

        private void LPgrid_GridItemDoubleClick(object? sender, GridHoverItem e)
        {
            var it = e.ItemUnderMouse as AFileEntry;

            // Kill the tooltip if any
            if (sender != null)
                ToolTip.SetIsOpen((TaiDataGrid)sender,false);
            
            if (Environment.OSVersion.Platform == PlatformID.Win32NT)
            {
                if (it.Typ)
                {
                    _lpHistory.Push(LPpath.Text);
                    LPpath.Text = JoinChildPath(LPpath.Text, it.Name);
                    if (ChkShowHidden != null)
                        FileUtility.PopulateFilePanel(LPgrid, LPpath.Text, ChkShowHidden.IsChecked.Value,RbSortName.IsChecked.Value);
                }
                else
                {
                    // its an actual file so can we execute it?

                    if (FileExtensionIsExecutable(it.Name.ToUpper()))
                    {
                        // we can execute it

                        string thingtoexecute = (LPpath.Text + "\\" + it.Name).Replace(@"\\", @"\");

                        Process.Start(new ProcessStartInfo()
                        {
                            FileName = thingtoexecute,
                            UseShellExecute = true,
                        });

                        //Process.Start((LPpath.Text + "\\" + it.Name).Replace(@"\\", @"\"));
                    }

                }
            }
            else if (Environment.OSVersion.Platform == PlatformID.Unix || Environment.OSVersion.Platform == PlatformID.MacOSX)
            {
                if (it.Typ)
                {
                    _lpHistory.Push(LPpath.Text);

                    LPpath.Text = JoinChildPath(LPpath.Text, it.Name);
                    if (ChkShowHidden != null) FileUtility.PopulateFilePanel(LPgrid, LPpath.Text, ChkShowHidden.IsChecked.Value);
                }
                else
                {
                    string thingtoexecute = (LPpath.Text + "/" + it.Name);

                    if (RuntimeInformation.IsOSPlatform(OSPlatform.OSX))
                    {
                        // macOS — use 'open' which delegates to Launch Services
                        OpenFileWithDefaultApp(thingtoexecute);
                    }
                    else if (FileExtensionIsExecutable(it.Name.ToUpper()) || IsExecutableOnUnixNet6(thingtoexecute))
                    {
                        // Linux — try xdg-open for known types, fall back to direct execution
                        OpenFileWithDefaultApp(thingtoexecute);
                    }
                }
            }

            if (it.Typ)
            {
                LPfilter.Text = "";
                RefreshLPGridPostActions();
            }
        }

        private void OpenFileWithDefaultApp(string filePath)
        {
            if (RuntimeInformation.IsOSPlatform(OSPlatform.OSX))
            {
                Process.Start(new ProcessStartInfo
                {
                    FileName = "open",
                    Arguments = "\"" + filePath + "\"",
                    UseShellExecute = false,
                });
            }
            else
            {
                // Linux — use xdg-open
                Process.Start(new ProcessStartInfo
                {
                    FileName = "xdg-open",
                    Arguments = "\"" + filePath + "\"",
                    UseShellExecute = false,
                });
            }
        }

        private bool FileExtensionIsImage(string v)
        {
            bool result = false;

            foreach (string s in ImageStuff)
            {
                if (v.ToUpper().EndsWith(s))
                {
                    result = true;
                    break;
                }
            }

            return result;
        }

        private bool FileExtensionIsExecutable(string v)
        {
            bool result = false;

            if (PlatformID.Win32NT == Environment.OSVersion.Platform)
            {
                foreach (string s in ExecutableStuff)
                {
                    if (v.ToUpper().EndsWith(s))
                    {
                        result = true;
                        break;
                    }
                }
            }
            else if (PlatformID.Unix == Environment.OSVersion.Platform ||
                     PlatformID.MacOSX == Environment.OSVersion.Platform)
            {
                bool IsExecutableOnUnixNet6(string path)
                {
                    return IsExecutableOnUnixNet6(v);
                }
            }

            return result;
        }
        
        private bool IsExecutableOnUnixNet6(string path)
        {
            if (!File.Exists(path)) return false;
            var mode = File.GetUnixFileMode(path);
            // Check any of the execute bits
            return  mode.HasFlag(UnixFileMode.UserExecute)
                     || mode.HasFlag(UnixFileMode.GroupExecute)
                     || mode.HasFlag(UnixFileMode.OtherExecute);
        }

        private void MainWindowGridContainer_SizeChanged(object? sender, SizeChangedEventArgs e)
        {

            double centerWidth = 150;
            double nwidth = (e.NewSize.Width - centerWidth - 24) / 2;

            double nheight = ((e.NewSize.Height) - 30 - 26 - 24) * .7;

            LPgrid.SetGridSize((int)nwidth - 8, (int)nheight - 16);
            RPgrid.SetGridSize((int)nwidth - 8, (int)nheight - 16 );

            // here we should also set the sizes for other elements of the UI
            // like the center buttons between the file grids


        }

        private void RefreshLPGrid ()
        {
            if (LPpath.Text != null)
                if (ChkShowHidden != null)
                    FileUtility.PopulateFilePanel(LPgrid, LPpath.Text, ChkShowHidden.IsChecked.Value,RbSortName.IsChecked.Value);
            CaptureUnfilteredItems(LPgrid, ref _lpUnfilteredItems);
            ApplyFilter(LPgrid, LPfilter.Text, _lpUnfilteredItems);
            UpdateStatusBar();
        }

        private void RefreshRPGrid()
        {
            if (RPpath.Text != null)
                if (ChkShowHidden != null)
                    FileUtility.PopulateFilePanel(RPgrid, RPpath.Text, ChkShowHidden.IsChecked.Value,RbSortName.IsChecked.Value);
            CaptureUnfilteredItems(RPgrid, ref _rpUnfilteredItems);
            ApplyFilter(RPgrid, RPfilter.Text, _rpUnfilteredItems);
            UpdateStatusBar();
        }

        #region Breadcrumb Path Bar

        private void UpdateBreadcrumbs(string path, ItemsControl breadcrumbs, string side)
        {
            if (string.IsNullOrEmpty(path)) return;

            if (CloudPath.IsCloudUri(path))
            {
                UpdateCloudBreadcrumbs(path, breadcrumbs, side);
                return;
            }

            var items = new List<Control>();

            var separator = Path.DirectorySeparatorChar;
            var segments = path.Split(separator, StringSplitOptions.RemoveEmptyEntries);

            // Add root segment
            var isWindows = RuntimeInformation.IsOSPlatform(OSPlatform.Windows);

            // Handle root-only path (e.g., "/" on Linux or "C:\" on Windows)
            if (segments.Length == 0)
            {
                var rootOnlyButton = CreateBreadcrumbButton("/", separator.ToString(), side);
                items.Add(rootOnlyButton);
                breadcrumbs.ItemsSource = items;
                return;
            }

            var rootPath = isWindows
                ? segments[0] + separator
                : separator.ToString();

            var rootButton = CreateBreadcrumbButton(
                isWindows ? segments[0] : "/",
                rootPath, side);
            items.Add(rootButton);

            // Build cumulative path for each segment
            var cumulativePath = rootPath;
            var startIndex = isWindows ? 1 : 0;

            for (int i = startIndex; i < segments.Length; i++)
            {
                // Add separator label
                var sepLabel = new TextBlock
                {
                    Text = " \u203a ",
                    VerticalAlignment = Avalonia.Layout.VerticalAlignment.Center,
                    Foreground = new SolidColorBrush(Colors.Gray),
                    FontSize = 13
                };
                items.Add(sepLabel);

                // Build cumulative path
                cumulativePath = cumulativePath.TrimEnd(separator) + separator + segments[i];

                var segButton = CreateBreadcrumbButton(segments[i], cumulativePath, side);
                items.Add(segButton);
            }

            breadcrumbs.ItemsSource = items;
        }

        private void UpdateCloudBreadcrumbs(string path, ItemsControl breadcrumbs, string side)
        {
            var items = new List<Control>();
            var cp = CloudPath.Parse(path);

            var rootPath = new CloudPath(cp.Remote, "").FullUri;
            items.Add(CreateBreadcrumbButton($"cloud://{cp.Remote}", rootPath, side));

            if (string.IsNullOrEmpty(cp.Path))
            {
                breadcrumbs.ItemsSource = items;
                return;
            }

            var segments = cp.Path.Split('/', StringSplitOptions.RemoveEmptyEntries);
            var cumulative = "";
            foreach (var seg in segments)
            {
                items.Add(new TextBlock
                {
                    Text = " \u203a ",
                    VerticalAlignment = Avalonia.Layout.VerticalAlignment.Center,
                    Foreground = new SolidColorBrush(Colors.Gray),
                    FontSize = 13,
                });

                cumulative = string.IsNullOrEmpty(cumulative) ? seg : $"{cumulative}/{seg}";
                var fullUri = new CloudPath(cp.Remote, cumulative).FullUri;
                items.Add(CreateBreadcrumbButton(seg, fullUri, side));
            }

            breadcrumbs.ItemsSource = items;
        }

        private Button CreateBreadcrumbButton(string label, string fullPath, string side)
        {
            var btn = new Button
            {
                Content = label,
                Tag = new string[] { fullPath, side },
                Padding = new Avalonia.Thickness(4, 1),
                Margin = new Avalonia.Thickness(0),
                MinWidth = 0,
                MinHeight = 0,
                FontSize = 12,
                Background = Brushes.Transparent,
                BorderThickness = new Avalonia.Thickness(0),
                Cursor = new Avalonia.Input.Cursor(Avalonia.Input.StandardCursorType.Hand),
                VerticalAlignment = Avalonia.Layout.VerticalAlignment.Center
            };
            btn.Click += BreadcrumbSegment_Click;
            return btn;
        }

        private void BreadcrumbSegment_Click(object? sender, RoutedEventArgs e)
        {
            if (sender is not Button btn || btn.Tag is not string[] info) return;

            var targetPath = info[0];
            var side = info[1];

            if (side == "LP")
            {
                if (!string.IsNullOrEmpty(LPpath.Text))
                    _lpHistory.Push(LPpath.Text);
                LPpath.Text = targetPath;
                LPfilter.Text = "";
                if (ChkShowHidden != null)
                    FileUtility.PopulateFilePanel(LPgrid, LPpath.Text, ChkShowHidden.IsChecked.Value, RbSortName.IsChecked.Value);
                RefreshLPGridPostActions();
            }
            else
            {
                if (!string.IsNullOrEmpty(RPpath.Text))
                    _rpHistory.Push(RPpath.Text);
                RPpath.Text = targetPath;
                RPfilter.Text = "";
                if (ChkShowHidden != null)
                    FileUtility.PopulateFilePanel(RPgrid, RPpath.Text, ChkShowHidden.IsChecked.Value, RbSortName.IsChecked.Value);
                RefreshRPGridPostActions();
            }
        }

        private void EnterPathEditMode(string side)
        {
            if (side == "LP")
            {
                _lpPathBeforeEdit = LPpath.Text ?? "";
                LPbreadcrumbBorder.IsVisible = false;
                LPpath.IsVisible = true;
                LPpath.Focus();
                LPpath.SelectAll();
            }
            else
            {
                _rpPathBeforeEdit = RPpath.Text ?? "";
                RPbreadcrumbBorder.IsVisible = false;
                RPpath.IsVisible = true;
                RPpath.Focus();
                RPpath.SelectAll();
            }
        }

        private void ExitPathEditMode(string side)
        {
            if (side == "LP")
            {
                LPpath.IsVisible = false;
                LPbreadcrumbBorder.IsVisible = true;
                UpdateBreadcrumbs(LPpath.Text, LPbreadcrumbs, "LP");
            }
            else
            {
                RPpath.IsVisible = false;
                RPbreadcrumbBorder.IsVisible = true;
                UpdateBreadcrumbs(RPpath.Text, RPbreadcrumbs, "RP");
            }
        }

        #endregion

        /// <summary>
        /// Shows a flyout listing the configured rclone cloud remotes; selecting one navigates the
        /// given panel to cloud://&lt;remote&gt;/ so the user doesn't have to type the URI by hand.
        /// </summary>
        private async Task ShowCloudRemotesAsync(Button anchor, string side)
        {
            if (!RcloneService.IsInstalled())
            {
                await new MessageBox(
                    "rclone is not installed. Use the %RCLONECONFIG% button to add a cloud remote first.")
                    .ShowDialog(this);
                return;
            }

            List<string> remotes;
            try
            {
                remotes = await RcloneRemoteManager.ListRemotesAsync();
            }
            catch (Exception ex)
            {
                await new MessageBox($"Could not list cloud remotes: {ex.Message}").ShowDialog(this);
                return;
            }

            if (remotes.Count == 0)
            {
                await new MessageBox(
                    "No cloud remotes are configured yet. Use the %RCLONECONFIG% button to add one.")
                    .ShowDialog(this);
                return;
            }

            var items = new List<MenuItem>();
            foreach (var name in remotes)
            {
                var remote = name; // capture per-iteration for the closure
                var mi = new MenuItem { Header = $"{CloudPath.Scheme}{remote}/" };
                mi.Click += (_, _) => NavigatePanelToCloud(side, remote);
                items.Add(mi);
            }

            var flyout = new MenuFlyout { ItemsSource = items };
            flyout.ShowAt(anchor);
        }

        /// <summary>Navigates a panel to the root of a cloud remote, mirroring a typed-path Enter.</summary>
        private void NavigatePanelToCloud(string side, string remote)
        {
            string path = $"{CloudPath.Scheme}{remote}/";
            bool showHidden = ChkShowHidden?.IsChecked ?? false;

            if (side == "LP")
            {
                if (!string.IsNullOrEmpty(LPpath.Text)) _lpHistory.Push(LPpath.Text);
                LPpath.Text = path;
                LPfilter.Text = "";
                FileUtility.PopulateFilePanel(LPgrid, path, showHidden);
                RefreshLPGridPostActions();
                ExitPathEditMode("LP");
            }
            else
            {
                if (!string.IsNullOrEmpty(RPpath.Text)) _rpHistory.Push(RPpath.Text);
                RPpath.Text = path;
                RPfilter.Text = "";
                FileUtility.PopulateFilePanel(RPgrid, path, showHidden);
                RefreshRPGridPostActions();
                ExitPathEditMode("RP");
            }
        }

        private void RefreshLPGridPostActions()
        {
            CaptureUnfilteredItems(LPgrid, ref _lpUnfilteredItems);
            ApplyFilter(LPgrid, LPfilter.Text, _lpUnfilteredItems);
            UpdateStatusBar();
            UpdateBreadcrumbs(LPpath.Text, LPbreadcrumbs, "LP");
        }

        private void RefreshRPGridPostActions()
        {
            CaptureUnfilteredItems(RPgrid, ref _rpUnfilteredItems);
            ApplyFilter(RPgrid, RPfilter.Text, _rpUnfilteredItems);
            UpdateStatusBar();
            UpdateBreadcrumbs(RPpath.Text, RPbreadcrumbs, "RP");
        }

        /// <summary>
        /// Fires when a panel has finished (re)populating — including the asynchronous completion of
        /// a remote/cloud listing. Captures the *real* item list as the unfiltered baseline and
        /// re-applies the active filter, so the snapshot is never the transient "Loading…" row that
        /// a synchronous post-action would otherwise grab from an async populate.
        /// </summary>
        private void OnPanelPopulated(TaiDataGrid grid)
        {
            if (ReferenceEquals(grid, LPgrid))
            {
                CaptureUnfilteredItems(LPgrid, ref _lpUnfilteredItems);
                ApplyFilter(LPgrid, LPfilter.Text, _lpUnfilteredItems);
            }
            else if (ReferenceEquals(grid, RPgrid))
            {
                CaptureUnfilteredItems(RPgrid, ref _rpUnfilteredItems);
                ApplyFilter(RPgrid, RPfilter.Text, _rpUnfilteredItems);
            }
            UpdateStatusBar();
        }

        private void CaptureUnfilteredItems(TaiDataGrid grid, ref List<object> store)
        {
            store = new List<object>(grid.Items);
        }

        private void ApplyFilter(TaiDataGrid grid, string filterText, List<object> unfilteredItems)
        {
            if (string.IsNullOrWhiteSpace(filterText))
            {
                if (grid.Items.Count != unfilteredItems.Count)
                {
                    grid.Items = new List<object>(unfilteredItems);
                    grid.SuspendRendering = false;
                }
                return;
            }

            var filtered = unfilteredItems.Where(item =>
            {
                if (item is AFileEntry af)
                    return af.Name.Contains(filterText, StringComparison.OrdinalIgnoreCase);
                return true;
            }).ToList();

            grid.SuspendRendering = true;
            grid.Items = filtered;
            grid.SuspendRendering = false;
        }

        private void LPfilter_KeyUp(object? sender, KeyEventArgs e)
        {
            ApplyFilter(LPgrid, LPfilter.Text, _lpUnfilteredItems);
            UpdateStatusBar();
        }

        private void RPfilter_KeyUp(object? sender, KeyEventArgs e)
        {
            ApplyFilter(RPgrid, RPfilter.Text, _rpUnfilteredItems);
            UpdateStatusBar();
        }

        private void LPfilterClear_Click(object? sender, RoutedEventArgs e)
        {
            LPfilter.Text = "";
            ApplyFilter(LPgrid, "", _lpUnfilteredItems);
            UpdateStatusBar();
        }

        private void RPfilterClear_Click(object? sender, RoutedEventArgs e)
        {
            RPfilter.Text = "";
            ApplyFilter(RPgrid, "", _rpUnfilteredItems);
            UpdateStatusBar();
        }

        private void UpdateStatusBar()
        {
            LeftStatusText.Text = BuildStatusText(LPgrid, LPpath.Text);
            RightStatusText.Text = BuildStatusText(RPgrid, RPpath.Text);
        }

        private string BuildStatusText(TaiDataGrid grid, string path)
        {
            int totalItems = grid.Items.Count;
            int folders = 0;
            int files = 0;
            long selectedBytes = 0;
            int selectedCount = 0;

            foreach (var item in grid.Items)
            {
                if (item is AFileEntry af)
                {
                    if (af.Typ) folders++;
                    else files++;
                }
            }

            foreach (var item in grid.SelectedItems)
            {
                if (item is AFileEntry af && !af.Typ)
                {
                    selectedCount++;
                    selectedBytes += ParseFileSize(af.FileSize);
                }
            }

            string freeSpace = "";
            try
            {
                if (!string.IsNullOrEmpty(path) && ProviderRegistry.For(path).DirectoryExists(path))
                {
                    var root = Path.GetPathRoot(path);
                    if (!string.IsNullOrEmpty(root))
                    {
                        var di = new DriveInfo(root);
                        freeSpace = FormatBytes(di.AvailableFreeSpace) + " free";
                    }
                }
            }
            catch { }

            string selInfo = selectedCount > 0
                ? $" | Sel: {selectedCount} ({FormatBytes(selectedBytes)})"
                : "";

            return $"{folders} folders, {files} files{selInfo}" +
                   (freeSpace != "" ? $" | {freeSpace}" : "");
        }

        private long ParseFileSize(string sizeStr)
        {
            if (string.IsNullOrEmpty(sizeStr)) return 0;
            sizeStr = sizeStr.Trim();
            if (sizeStr.EndsWith("Gb", StringComparison.OrdinalIgnoreCase))
            {
                if (double.TryParse(sizeStr[..^2], out var v)) return (long)(v * 1024 * 1024 * 1024);
            }
            else if (sizeStr.EndsWith("Mb", StringComparison.OrdinalIgnoreCase))
            {
                if (double.TryParse(sizeStr[..^2], out var v)) return (long)(v * 1024 * 1024);
            }
            else if (sizeStr.EndsWith("Kb", StringComparison.OrdinalIgnoreCase))
            {
                if (double.TryParse(sizeStr[..^2], out var v)) return (long)(v * 1024);
            }
            else if (sizeStr.EndsWith("b", StringComparison.OrdinalIgnoreCase))
            {
                if (double.TryParse(sizeStr[..^1], out var v)) return (long)v;
            }
            return 0;
        }

        private string FormatBytes(long bytes)
        {
            string[] units = { "B", "KB", "MB", "GB", "TB" };
            double val = bytes;
            int unit = 0;
            while (val >= 1024 && unit < units.Length - 1)
            {
                val /= 1024;
                unit++;
            }
            return $"{val:0.##} {units[unit]}";
        }

        private string FindAssetsDirectory()
        {
            // 1. Current working directory
            string path = Path.Combine(Environment.CurrentDirectory, "Assets");
            if (Directory.Exists(path)) return path;

            // 2. Executable's directory
            path = Path.Combine(AppContext.BaseDirectory, "Assets");
            if (Directory.Exists(path)) return path;

            return null;
        }

        private string FindConfigurationFile()
        {
            const string configName = "Configuration.xml";

            // 1. Current working directory
            string path = Path.Combine(Environment.CurrentDirectory, configName);
            if (File.Exists(path)) return path;

            // 2. Directory where the executable lives
            string exeDir = AppContext.BaseDirectory;
            path = Path.Combine(exeDir, configName);
            if (File.Exists(path)) return path;

            // 3. Platform-specific config locations
            if (RuntimeInformation.IsOSPlatform(OSPlatform.OSX))
            {
                path = Path.Combine(
                    Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
                    "Library", "Application Support", "dori", configName);
                if (File.Exists(path)) return path;
            }
            else if (Environment.OSVersion.Platform == PlatformID.Unix)
            {
                path = Path.Combine(
                    Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
                    ".config", "dori", configName);
                if (File.Exists(path)) return path;
            }
            else if (Environment.OSVersion.Platform == PlatformID.Win32NT)
            {
                path = Path.Combine(
                    Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
                    "dori", configName);
                if (File.Exists(path)) return path;
            }

            return null;
        }

        private string GetRootDirectoryPath()
        {
            string rootDirectoryPath = "";

            if (Environment.OSVersion.Platform == PlatformID.Win32NT)
            {
                //rootDirectoryPath = Directory.GetDirectoryRoot(Environment.SystemDirectory);
                rootDirectoryPath = Environment.GetFolderPath(Environment.SpecialFolder.MyComputer);
            }
            else if (Environment.OSVersion.Platform == PlatformID.Unix || Environment.OSVersion.Platform == PlatformID.MacOSX)
            {
                //rootDirectoryPath = Directory.GetDirectoryRoot(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile));
                rootDirectoryPath = @"/";
            }

            return rootDirectoryPath;
        }

        private string GetHomeDirectoryPath()
        {
            string rootDirectoryPath = "";

            if (Environment.OSVersion.Platform == PlatformID.Win32NT)
            {
                //rootDirectoryPath = Directory.GetDirectoryRoot(Environment.SystemDirectory);
                rootDirectoryPath = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
            }
            else if (Environment.OSVersion.Platform == PlatformID.Unix || Environment.OSVersion.Platform == PlatformID.MacOSX)
            {
                //rootDirectoryPath = Directory.GetDirectoryRoot(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile));
                rootDirectoryPath = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
            }

            return rootDirectoryPath;
        }
        
        private string GetDesktopDirectoryPath()
        {
            string rootDirectoryPath = "";

            if (Environment.OSVersion.Platform == PlatformID.Win32NT)
            {
                //rootDirectoryPath = Directory.GetDirectoryRoot(Environment.SystemDirectory);
                rootDirectoryPath = Environment.GetFolderPath(Environment.SpecialFolder.Desktop);
            }
            else if (Environment.OSVersion.Platform == PlatformID.Unix || Environment.OSVersion.Platform == PlatformID.MacOSX)
            {
                //rootDirectoryPath = Directory.GetDirectoryRoot(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile));
                rootDirectoryPath = Environment.GetFolderPath(Environment.SpecialFolder.Desktop);
            }

            return rootDirectoryPath;
        }
        
        private string GetDocumentsDirectoryPath()
        {
            string rootDirectoryPath = "";

            if (Environment.OSVersion.Platform == PlatformID.Win32NT)
            {
                //rootDirectoryPath = Directory.GetDirectoryRoot(Environment.SystemDirectory);
                rootDirectoryPath = Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments);
            }
            else if (Environment.OSVersion.Platform == PlatformID.Unix || Environment.OSVersion.Platform == PlatformID.MacOSX)
            {
                //rootDirectoryPath = Directory.GetDirectoryRoot(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile));
                rootDirectoryPath = Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments);

                string Home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
                
                // If home = rootDir See if there is a documents folder in there
                if (Home == rootDirectoryPath)
                {
                    if (Directory.Exists(Home + "/Documents"))
                    {
                        rootDirectoryPath = rootDirectoryPath + "/Documents";
                    }
                    else
                    {
                        if (Directory.Exists(Home + "/documents"))
                        {
                            rootDirectoryPath = rootDirectoryPath + "/documents";
                        }
                        else
                        {
                            if (Directory.Exists(Home + "/DOCUMENTS"))
                            {
                                rootDirectoryPath = rootDirectoryPath + "/DOCUMENTS";
                            }
                            
                        }
                    }
                }
            }

            return rootDirectoryPath;
        }
        
        private string GetPicturesDirectoryPath()
        {
            string rootDirectoryPath = "";

            if (Environment.OSVersion.Platform == PlatformID.Win32NT)
            {
                //rootDirectoryPath = Directory.GetDirectoryRoot(Environment.SystemDirectory);
                rootDirectoryPath = Environment.GetFolderPath(Environment.SpecialFolder.MyPictures);
            }
            else if (Environment.OSVersion.Platform == PlatformID.Unix || Environment.OSVersion.Platform == PlatformID.MacOSX)
            {
                //rootDirectoryPath = Directory.GetDirectoryRoot(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile));
                rootDirectoryPath = Environment.GetFolderPath(Environment.SpecialFolder.MyPictures);
            }

            return rootDirectoryPath;
        }

        public void ApplyButtonSettingsFromXml(string xmlFilePath, Window window)
        {
            try
            {
                // Clear The Button Entries out first

                TheButtons.Clear();
                TheDriveButtons.Clear();

                // Load XML file
                XDocument xmlDoc = XDocument.Load(xmlFilePath);
                
                // Find the <Extensions> element
                XElement extensionsElement = xmlDoc.Descendants("Extensions").FirstOrDefault();

                if (extensionsElement != null)
                {
                    ExecutableStuff = new List<string>();
                    string pattern = "[\n\r\t\b\f\\\"\'\x0B]";
                    // Get the string value inside the <Extensions> element
                    string extensionsString = Regex.Replace(extensionsElement.Value, pattern, "");
                    
                    // Split the string into an array if needed
                    string[] extensionsArray = extensionsString.Split(',');

                    // Print or use the string or array as needed
                    Console.WriteLine("Extensions String: " + extensionsString);
                    //Console.WriteLine("Extensions Array: ");

                    //string pattern = "[\n\r\t\b\f\\\"\'\x0B]";
                    //string cleanedString = Regex.Replace(input, pattern, "");

                    foreach (string extension in extensionsArray)
                    {
                        ExecutableStuff.Add(extension);
                    }
                }

                // find the <Extensions> element in the <Images> element
                XElement imagesElement = xmlDoc.Descendants("ImageExtensions").FirstOrDefault();
                
                XElement UseIntegratedImageViewerElement = xmlDoc.Descendants("UseIntegratedImageViewer").FirstOrDefault();
                
                if (imagesElement != null)
                {
                    ImageStuff = new List<string>();
                    string pattern = "[\n\r\t\b\f\\\"\'\x0B]";
                    // Get the string value inside the <Extensions> element
                    string extensionsString = Regex.Replace(imagesElement.Value, pattern, "");

                    // Split the string into an array if needed
                    string[] extensionsArray = extensionsString.Split(',');

                    // Print or use the string or array as needed
                    Console.WriteLine("Extensions String: " + extensionsString);
                    //Console.WriteLine("Extensions Array: ");

                    //string pattern = "[\n\r\t\b\f\\\"\'\x0B]";
                    //string cleanedString = Regex.Replace(input, pattern, "");

                    foreach (string extension in extensionsArray)
                    {
                        ImageStuff.Add(extension);
                    }
                }
                
                if (UseIntegratedImageViewerElement != null)
                {
                    UseIntegratedImageViewer = bool.Parse(UseIntegratedImageViewerElement.Value);
                }
                else
                {
                    UseIntegratedImageViewer = false;
                }
                
                // Find the <FontSize> element inside <LeftGrid>
                XElement leftGridFontSizeElement = xmlDoc.Descendants("LeftGrid")
                    .Elements("FontSize")
                    .FirstOrDefault();
                
                XElement leftGridHeaderFontSizeElement = xmlDoc.Descendants("LeftGrid")
                    .Elements("HeaderFontSize")
                    .FirstOrDefault();

                XElement leftGridTitleElement = xmlDoc.Descendants("LeftGrid")
                    .Elements("Title")
                    .FirstOrDefault();

                XElement leftGridTitleFontSizeElement = xmlDoc.Descendants("LeftGrid")
                   .Elements("TitleFontSize")
                   .FirstOrDefault();
                
                XElement leftGridStartPathElement = xmlDoc.Descendants("LeftGrid")
                    .Elements("StartPath")
                    .FirstOrDefault();

                if (leftGridFontSizeElement != null)
                {
                    // Get the font size value from <LeftGrid>
                    LPgrid.GridFontSize = int.Parse(leftGridFontSizeElement.Value);
                }
                else
                {
                    LPgrid.GridFontSize = 12;
                }
                
                if (leftGridHeaderFontSizeElement != null)
                {
                    // Get the font size value from <LeftGrid>
                    LPgrid.GridHeaderFontSize = int.Parse(leftGridHeaderFontSizeElement.Value);
                }
                else
                {
                    LPgrid.GridHeaderFontSize = 14;
                }

                if (leftGridTitleElement != null)
                {
                    // Get the font size value from <RightGrid>
                    LPgrid.GridTitle = leftGridTitleElement.Value;
                }
                else
                {
                    RPgrid.GridTitle = "Right Panel";
                }

                if (leftGridTitleFontSizeElement != null)
                {
                    // Get the font size value from <RightGrid>
                    LPgrid.GridTitleFontSize = int.Parse(leftGridTitleFontSizeElement.Value);
                }
                else
                {
                    LPgrid.GridTitleFontSize = 16;
                }
                
                if (leftGridStartPathElement != null)
                {
                    // Get the font size value from <RightGrid>
                    LPpath.Text = MakePathEnvSafe(leftGridStartPathElement.Value);
                    StartLeftPath = LPpath.Text;
                }
                else
                {
                    LPpath.Text = GetRootDirectoryPath();
                }

                // Find the <FontSize> element inside <RightGrid>
                XElement rightGridFontSizeElement = xmlDoc.Descendants("RightGrid")
                    .Elements("FontSize")
                    .FirstOrDefault();
                
                XElement rightGridHeaderFontSizeElement = xmlDoc.Descendants("RightGrid")
                    .Elements("HeaderFontSize")
                    .FirstOrDefault();

                XElement rightGridTitleElement = xmlDoc.Descendants("RightGrid")
                    .Elements("Title")
                    .FirstOrDefault();

                XElement rightGridTitleFontSizeElement = xmlDoc.Descendants("RightGrid")
                   .Elements("TitleFontSize")
                   .FirstOrDefault();
                
                XElement rightGridStartPathElement = xmlDoc.Descendants("RightGrid")
                    .Elements("StartPath")
                    .FirstOrDefault();

                if (rightGridFontSizeElement != null)
                {
                    // Get the font size value from <RightGrid>
                    RPgrid.GridFontSize = int.Parse(rightGridFontSizeElement.Value);
                }
                else
                {
                    RPgrid.GridFontSize = 12;
                    
                }
                
                if (rightGridHeaderFontSizeElement != null)
                {
                    // Get the font size value from <RightGrid>
                    RPgrid.GridHeaderFontSize = int.Parse(rightGridHeaderFontSizeElement.Value);
                }
                else
                {
                    RPgrid.GridHeaderFontSize = 14;
                }

                if (rightGridTitleElement != null)
                {
                    // Get the font size value from <RightGrid>
                    RPgrid.GridTitle = rightGridTitleElement.Value;
                }
                else
                {
                    RPgrid.GridTitle = "Right Panel";
                }

                if (rightGridTitleFontSizeElement != null)
                {
                    // Get the font size value from <RightGrid>
                    RPgrid.GridTitleFontSize = int.Parse(rightGridTitleFontSizeElement.Value);
                }
                else
                {
                    RPgrid.GridTitleFontSize = 16;
                }
                
                if (rightGridStartPathElement != null)
                {
                    // Get the font size value from <RightGrid>
                    RPpath.Text = MakePathEnvSafe(rightGridStartPathElement.Value);
                    StartRightPath = RPpath.Text;
                }
                else
                {
                    RPpath.Text = GetRootDirectoryPath();
                }
                
                // Query XML for button settings
                var buttonSettingsList = from btn in xmlDoc.Descendants("Button")
                                         select new ButtonSettings
                                         {
                                             Name = (string)btn.Element("Name"),
                                             Content = (string)btn.Element("Content"),
                                             Background = (string)btn.Element("Background"),
                                             Foreground = (string)btn.Element("Foreground"),
                                             HorizontalAlignment = (string)btn.Element("HorizontalAlignment"),
                                             VerticalAlignment = (string)btn.Element("VerticalAlignment"),
                                             Margin = (string)btn.Element("Margin"),
                                             Action = (string)btn.Element("Action"),
                                             Args = (string)btn.Element("Args"),
                                             ShellExecute = (string)btn.Element("ShellExecute"),
                                             ShowWindow = (string)btn.Element("ShowWindow"),
                                             ToolTip = (string)btn.Element("ToolTip")
                                         };
                
                //Console.WriteLine(Toml.FromModel(buttonSettingsList));

                TheButtonSettings = buttonSettingsList.ToList();
                // Find the grid where the buttons exist
                var grid = (Grid)window.FindControl<Control>("ButtonGrid");
                
                // Apply settings to each button
                foreach (var buttonSettings in buttonSettingsList)
                {
                    buttonSettings.Name = buttonSettings.Name.Replace("LP", "Lp");
                    
                    // Find the button by its name
                    Button button = (Button)grid.FindControl<Control>(buttonSettings.Name);
                    
                    // Check if the button control exists
                    if (button != null)
                    {
                        // Save the buttonSettings object to the button's Tag property
                        button.Tag = buttonSettings;
                        
                        // Check if the Content property is not null or empty
                        if (!string.IsNullOrEmpty(buttonSettings.Content))
                        {
                            // Set the button's Content property
                            button.Content = buttonSettings.Content;
                        }

                        // Check if the Background property is not null or empty
                        if (!string.IsNullOrEmpty(buttonSettings.Background))
                        {
                            // Parse the color from the string
                            var color = Color.Parse(buttonSettings.Background);
                            // Set the button's Background property
                            button.Background = new SolidColorBrush(color);
                        }

                        // Check if the Foreground property is not null or empty
                        if (!string.IsNullOrEmpty(buttonSettings.Foreground))
                        {
                            // Parse the color from the string
                            var color = Color.Parse(buttonSettings.Foreground);
                            // Set the button's Foreground property
                            button.Foreground = new SolidColorBrush(color);
                              
                            //button.VerticalContentAlignment = Avalonia.Layout.VerticalAlignment.Stretch;
                        }

                        // Check if the HorizontalAlignment property is not null or empty
                        if (!string.IsNullOrEmpty(buttonSettings.HorizontalAlignment))
                        {
                            // Depending on the value of the property, set the button's HorizontalAlignment
                            switch (buttonSettings.HorizontalAlignment.ToUpper())
                            {
                                case "LEFT":
                                    button.HorizontalContentAlignment = Avalonia.Layout.HorizontalAlignment.Left;
                                    break;
                                case "CENTER":
                                    button.HorizontalContentAlignment = Avalonia.Layout.HorizontalAlignment.Center;
                                    break;
                                case "RIGHT":
                                    button.HorizontalContentAlignment = Avalonia.Layout.HorizontalAlignment.Right;
                                    break;
                                default:
                                    break;
                            }
                        }

                        // Check if the VerticalAlignment property is not null or empty
                        if (!string.IsNullOrEmpty(buttonSettings.VerticalAlignment))
                        {
                            // Depending on the value of the property, set the button's VerticalAlignment
                            switch (buttonSettings.VerticalAlignment.ToUpper())
                            {
                                case "TOP":
                                    button.VerticalContentAlignment = Avalonia.Layout.VerticalAlignment.Top;
                                    break;
                                case "CENTER":
                                    button.VerticalContentAlignment = Avalonia.Layout.VerticalAlignment.Center;
                                    break;
                                case "BOTTOM":
                                    button.VerticalContentAlignment = Avalonia.Layout.VerticalAlignment.Bottom;
                                    break;
                                default:
                                    break;
                            }
                        }
                    
                        // Check if the Margin property is not null or empty
                        if (!string.IsNullOrEmpty(buttonSettings.Margin))
                        {
                            // Parse the margin from the string
                            var margin = Avalonia.Thickness.Parse(buttonSettings.Margin);
                            // Set the button's Margin property
                            button.Margin = margin;
                        }
                        
                        // Check if the Action property is not null or empty
                        if (!string.IsNullOrEmpty(buttonSettings.Action))
                        {
                            // Get the defined action for this button
                            var action = buttonSettings.Action;
                            // Get the actual buttons name so it can be sought
                            // on the handler for the button click
                            var buttonName = buttonSettings.Name;
                                                        
                            var buttonArgs = buttonSettings.Args;

                            // Create a new button entry
                            var buttonEntry = new ButtonEntry(buttonName, action, buttonArgs);

                            if (!string.IsNullOrEmpty(buttonSettings.ShellExecute))
                            {
                                buttonEntry.ShellExecute = bool.Parse(buttonSettings.ShellExecute);
                            }

                            if (!string.IsNullOrEmpty(buttonSettings.ShowWindow))
                            {
                                buttonEntry.ShowWindow = bool.Parse(buttonSettings.ShowWindow);
                            }

                            buttonEntry.ToolTip = buttonSettings.ToolTip;

                            // Add the button entry to the list of buttons
                            TheButtons.Add(buttonEntry);
                            
                        }
                    }
                }
                
                var driveSettingsList = from btn in xmlDoc.Descendants("DrivePreset")
                                         select new DriveButtonEntry()
                                         {
                                             Order = (string)btn.Element("Order"),
                                             Name = (string)btn.Element("Name"),
                                             Path = (string)btn.Element("Path"),
                                             Content = (string)btn.Element("Content"),
                                             Background = (string)btn.Element("Background"),
                                             Foreground = (string)btn.Element("Foreground"),
                                             ToolTip = (string)btn.Element("ToolTip")
                                         };

                foreach (var thing in driveSettingsList)
                {
                    if (thing.Content == null)
                    {
                        thing.Content = thing.Name;
                    }
                    
                    switch (thing.Order)
                    {
                        case "1":
                            Button b1 = (Button)window.FindControl<Control>("DrivePreset1A");
                            Button b2 = (Button)window.FindControl<Control>("DrivePreset1B");
                            if (thing.Content != null)
                            {
                                b1.Content = "<-" + thing.Content.ToString();
                                b2.Content = thing.Content.ToString() + "->";
                            }

                            b1.Tag = thing;
                            b2.Tag = thing;

                            break;
                        case "2":
                            Button b3 = (Button)window.FindControl<Control>("DrivePreset2A");
                            Button b4 = (Button)window.FindControl<Control>("DrivePreset2B");
                            if (thing.Content != null)
                            {
                                b3.Content = "<-" + thing.Content.ToString();
                                b4.Content = thing.Content.ToString() + "->";
                            }

                            b3.Tag = thing;
                            b4.Tag = thing;

                            break;
                        case "3":
                            Button b5 = (Button)window.FindControl<Control>("DrivePreset3A");
                            Button b6 = (Button)window.FindControl<Control>("DrivePreset3B");
                            if (thing.Content != null)
                            {
                                b5.Content = "<-" + thing.Content.ToString();
                                b6.Content = thing.Content.ToString() + "->";
                            }

                            b5.Tag = thing;
                            b6.Tag = thing;

                            break;
                        case "4":
                            Button b7 = (Button)window.FindControl<Control>("DrivePreset4A");
                            Button b8 = (Button)window.FindControl<Control>("DrivePreset4B");
                            if (thing.Content != null)
                            {
                                b7.Content = "<-" + thing.Content.ToString();
                                b8.Content = thing.Content.ToString() + "->";
                            }

                            b7.Tag = thing;
                            b8.Tag = thing;

                            break;
                        case "5":
                            Button b9 = (Button)window.FindControl<Control>("DrivePreset5A");
                            Button b10 = (Button)window.FindControl<Control>("DrivePreset5B");
                            if (thing.Content != null)
                            {
                                b9.Content = "<-" + thing.Content.ToString();
                                b10.Content = thing.Content.ToString() + "->";
                            }

                            b9.Tag = thing;
                            b10.Tag = thing;

                            break;
                    }
                }
                
                
            }
            catch (Exception ex)
            {
                Console.WriteLine("Unexpected error: " + ex.Message);
            }
        }

        public void DoButtonRefresh()
        {
            if (File.Exists(Environment.CurrentDirectory + "/Configuration.xml"))
            {
                ClearLowerButtons();
                ApplyButtonSettingsFromXml(Environment.CurrentDirectory + "/Configuration.xml", this);
            }
            else
            {
                // Look in the alternate places here based on OS

            }
        }

        private void ShowRcloneDiagnostics()
        {
            new RcloneDiagnosticsDialog().ShowDialog(this);
        }

        private async void ShowRcloneConfig()
        {
            if (!RcloneService.IsInstalled())
            {
                await new MessageBox(
                    "rclone is not installed. Open the rclone Diagnostics dialog (%RCLONEDIAG%) to install it first.")
                    .ShowDialog(this);
                return;
            }
            await new RcloneAddRemoteDialog().ShowDialog(this);
        }

        private static string JoinChildPath(string parent, string childName)
        {
            if (CloudPath.IsCloudUri(parent))
                return CloudPath.Parse(parent).Join(childName).FullUri;

            var sep = Environment.OSVersion.Platform == PlatformID.Win32NT ? "\\" : "/";
            if (parent.EndsWith(sep)) return parent + childName;
            return parent + sep + childName;
        }

    }
    
    public class DriveButtonEntry
    {
        public DriveButtonEntry()
        {
            Order = "";
            Name = "";
            Path = "";
            Content = "";
            Background = "";
            Foreground = "";
            ToolTip = "";
        }
        public string Order { get; set; }
        public string Name { get; set; }
        public string Path { get; set; }
        public string Content { get; set; }
        public string Background { get; set; }
        public string Foreground { get; set; }
        public string ToolTip { get; set; }
    }
    
    public class ButtonEntry
    {
        public string Bname { get; set; }
        public string Bcontent { get; set; }
        public string Bargs { get; set; }
        public bool ShellExecute { get; set; }
        public bool ShowWindow { get; set; }
        public string ToolTip { get; set; }
        
        public ButtonEntry(string name, string content, string bargs)
        {
            Bname = name;
            Bcontent = content;
            Bargs = bargs;
            ShellExecute = true;
            ShowWindow = false;
            ToolTip = "";
        }

        public ButtonEntry(string name, string content, string bargs,string sexecute)
        {
            Bname = name;
            Bcontent = content;
            Bargs = bargs;
            if (sexecute.ToUpper() == "FALSE")
            {
                ShellExecute = false;
            }
            else
            {
                ShellExecute = true;
            }
            //ShellExecute = true;

            ShowWindow = false;
            ToolTip = "";
        }

        public ButtonEntry(string name, string content, string bargs, string sexecute, string swin)
        {
            Bname = name;
            Bcontent = content;
            Bargs = bargs;
            if (sexecute.ToUpper() == "FALSE")
            {
                ShellExecute = false;
            }
            else
            {
                ShellExecute = true;
            }
            //ShellExecute = true;
            if (swin.ToUpper() == "TRUE")
            {
                ShowWindow = true;
            }
            else
            {
                ShowWindow = false;
            }
            //ShowWindow = false;
            ToolTip = "";
        }

        public ButtonEntry(string name, string content, string bargs, string sexecute, string swin,string tooltip)
        {
            Bname = name;
            Bcontent = content;
            Bargs = bargs;
            if (sexecute.ToUpper() == "FALSE")
            {
                ShellExecute = false;
            }
            else
            {
                ShellExecute = true;
            }
            //ShellExecute = true;
            if (swin.ToUpper() == "TRUE")
            {
                ShowWindow = true;
            }
            else
            {
                ShowWindow = false;
            }
            //ShowWindow = false;
            ToolTip = tooltip;
        }
    }

    public class ButtonSettings
    {
        public ButtonSettings()
        {
            Name = "";
            Content = "";
            Background = "";
            Foreground = "";
            HorizontalAlignment = "";
            VerticalAlignment = "";
            Margin = "";
            Action = "";
            Args = "";
            ShellExecute = "";
            ShowWindow = "";
            ToolTip = "";
        }
        
        public string Name { get; set; }
        public string Content { get; set; }
        public string Background { get; set; }
        public string Foreground { get; set; }
        public string HorizontalAlignment { get; set; }
        public string VerticalAlignment { get; set; }
        public string Margin { get; set; }
        public string Action { get; set; }
        public string Args { get; set; }
        public string ShellExecute { get; set;}
        public string ShowWindow { get; set; }
        public string ToolTip { get; set; }

    }

    
}