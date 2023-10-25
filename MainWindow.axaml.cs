using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Media;
//using Microsoft.CodeAnalysis.CSharp.Syntax;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using System.Xml.Linq;
using Avalonia.Input;
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
        
        private string StartRightPath = "";
        private string StartLeftPath = "";

        private string oldpath = "";

        private bool UseIntegratedImageViewer = true;
        
        private string LastButtonPopupName = "";

        private PopUp pop;
        
        #endregion
        
        public MainWindow()
        {
            InitializeComponent();

            // Apply The Settings if possible
            // First Look where the app is running from for Configuration.xml
            // Otherwise
            // If the system is Linux/Unix Look for Config in ~/.config/dori/Configuration.xml
            // If the system is MacOS Look in ~/Library/Application Support/dori/Configuration.xml
            // If the system is Windowz Look in %APPDATA%\dori\Configuration.xml
            // If the file is not found then use the default settings
           
            // See if Configuration.xml exists in the current directory
            // if it does then use it
            if (File.Exists(Environment.CurrentDirectory + "/Configuration.xml"))
            {
                ApplyButtonSettingsFromXml(Environment.CurrentDirectory + "/Configuration.xml", this);
            }
            else
            {
                // Look in the alternate places here based on OS
                
            }
            
            MainWindowGridContainer.SizeChanged += MainWindowGridContainer_SizeChanged;

            //Bitmap B1 = LoadImage(ImageStrings.BackButton);
            
            Bitmap B2 = new Bitmap(@"Assets/BackFolder.png");
            Bitmap B3 = new Bitmap(@"Assets/Drives.png");
            Bitmap B4 = new Bitmap(@"Assets/LeftArrow.png");
            Bitmap B5 = new Bitmap(@"Assets/RightArrow.png");
            Bitmap B6 = new Bitmap(@"Assets/LeftRightArrows.png");

            Image I1 = new Image();
            Image I2 = new Image();
            Image I3 = new Image();
            Image I4 = new Image();
            Image I5 = new Image();
            Image I6 = new Image();
            Image I7 = new Image();
            
            I1.Source = B2;
            I1.Width = RPBackButton.Width + 8;
            I1.Height = RPBackButton.Height+ 8;

            I2.Source = B2;
            I2.Width = LPBackButton.Width + 8;
            I2.Height = LPBackButton.Height + 8;
            
            I3.Source = B3;
            I3.Width = 28;
            I3.Height = 28;
            
            I4.Source = B3;
            I4.Width = 28;
            I4.Height = 28;
            
            I5.Source = B4;
            I5.Width = 18;
            I5.Height = 18;
            
            I6.Source = B5;
            I6.Width = 18;
            I6.Height= 18;
            
            I7.Source = B6;
            I7.Width = 18;
            I7.Height = 18;
            
            
            RPBackButton.Content = I1;
            LPBackButton.Content = I2;
            
            SwapButton.Content = I7;
            LeftToRightButton.Content = I5;
            RightToLeftButton.Content = I6;
            
            LPDriveButton.Content = I3;
            RPDriveButton.Content = I4;
            
            RPgrid.TruncateColumns.Add(1); // truncate the NAME column if its more than 30 characters
            LPgrid.TruncateColumns.Add(1); // truncate the NAME column if its more than 30 characters

            LPgrid.GridItemDoubleClick += LPgrid_GridItemDoubleClick;
            RPgrid.GridItemDoubleClick += RPgrid_GridItemDoubleClick;

            LPgrid.GridItemClick += LPgrid_GridItemClick;
            RPgrid.GridItemClick += RPgrid_GridItemClick;

            LPgrid.JustifyColumns.Add(2);
            LPgrid.JustifyColumns.Add(3);
            LPgrid.JustifyColumns.Add(4);
            RPgrid.JustifyColumns.Add(2);
            RPgrid.JustifyColumns.Add(3);
            RPgrid.JustifyColumns.Add(4);
            
            LPpath.KeyUp += LPpath_KeyUp;
            RPpath.KeyUp += RPpath_KeyUp;
            
            FileUtility.PopulateFilePanel(LPgrid, LPpath.Text);
            FileUtility.PopulateFilePanel(RPgrid, RPpath.Text);
            
            //PopulateFilePanel(LPgrid,LPpath.Text);
            //PopulateFilePanel(RPgrid, RPpath.Text);

            // wire up button click events for the lower panel buttons

            WireUpButtonHandlers();
            
            Title = Title + " " + Assembly.GetExecutingAssembly().GetName().Version.ToString(); 
        }

        private void RenameLeftButton_Click(object? sender, RoutedEventArgs e)
        {
            
            bool fileselected = false;

            if (LPgrid.SelectedItems.Count > 0)
            {
                foreach (AFileEntry af in LPgrid.SelectedItems)
                {
                    if (!af.Typ)
                    {
                        fileselected = true;
                        break;
                    }
                }
            }

            if (!fileselected)
            {
                MessageBox MB = new MessageBox("You have to have a file selected in the Left panel");
                MB.ShowDialog(this);
                return;
            }

            RenameFileInterface fi = new RenameFileInterface(LPgrid, LPpath.Text);
            fi.Width = 600;
            fi.Height = 180;
            fi.Show(this);
            
        }

        private void RenameRightButton_Click(object? sender, RoutedEventArgs e)
        {
            bool fileselected = false;

            if (RPgrid.SelectedItems.Count > 0)
            {
                foreach (AFileEntry af in RPgrid.SelectedItems)
                {
                    if (!af.Typ)
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

            RenameFileInterface fi = new RenameFileInterface(RPgrid, RPpath.Text);
            fi.Width = 600;
            fi.Height = 180;
            fi.Show(this);
            
        }
        
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

            RPBackButton.Click += RPBackButton_Click;
            LPBackButton.Click += LPBackButton_Click;
            
            RenameRightButton.Click += RenameRightButton_Click;
            RenameLeftButton.Click += RenameLeftButton_Click;
            
            DeleteLeftButton.Click += DeleteLeftButton_Click;
            DeleteRightButton.Click += DeleteRightButton_Click;

            LPButton1.Click += Handle_Lower_Panel_Button_Clicks;
            LPButton2.Click += Handle_Lower_Panel_Button_Clicks;
            LPButton3.Click += Handle_Lower_Panel_Button_Clicks;
            LPButton4.Click += Handle_Lower_Panel_Button_Clicks;
            LPButton5.Click += Handle_Lower_Panel_Button_Clicks;
            LPButton6.Click += Handle_Lower_Panel_Button_Clicks;
            LPButton7.Click += Handle_Lower_Panel_Button_Clicks;
            LPButton8.Click += Handle_Lower_Panel_Button_Clicks;
            LPButton9.Click += Handle_Lower_Panel_Button_Clicks;
            LPButton10.Click += Handle_Lower_Panel_Button_Clicks;
            LPButton11.Click += Handle_Lower_Panel_Button_Clicks;
            LPButton12.Click += Handle_Lower_Panel_Button_Clicks;
            LPButton13.Click += Handle_Lower_Panel_Button_Clicks;
            LPButton14.Click += Handle_Lower_Panel_Button_Clicks;
            LPButton15.Click += Handle_Lower_Panel_Button_Clicks;
            LPButton16.Click += Handle_Lower_Panel_Button_Clicks;
            LPButton17.Click += Handle_Lower_Panel_Button_Clicks;
            LPButton18.Click += Handle_Lower_Panel_Button_Clicks;
            LPButton19.Click += Handle_Lower_Panel_Button_Clicks;
            LPButton20.Click += Handle_Lower_Panel_Button_Clicks;
            LPButton21.Click += Handle_Lower_Panel_Button_Clicks;
            LPButton22.Click += Handle_Lower_Panel_Button_Clicks;
            LPButton23.Click += Handle_Lower_Panel_Button_Clicks;
            LPButton24.Click += Handle_Lower_Panel_Button_Clicks;
            LPButton25.Click += Handle_Lower_Panel_Button_Clicks;
            LPButton26.Click += Handle_Lower_Panel_Button_Clicks;
            LPButton27.Click += Handle_Lower_Panel_Button_Clicks;
            LPButton28.Click += Handle_Lower_Panel_Button_Clicks;
            LPButton29.Click += Handle_Lower_Panel_Button_Clicks;
            LPButton30.Click += Handle_Lower_Panel_Button_Clicks;
            LPButton31.Click += Handle_Lower_Panel_Button_Clicks;
            LPButton32.Click += Handle_Lower_Panel_Button_Clicks;
            LPButton33.Click += Handle_Lower_Panel_Button_Clicks;
            LPButton34.Click += Handle_Lower_Panel_Button_Clicks;
            LPButton35.Click += Handle_Lower_Panel_Button_Clicks;
            LPButton36.Click += Handle_Lower_Panel_Button_Clicks;
            
            DrivePreset1a.Click += Handle_Drive_Button_Clicks;
            DrivePreset1b.Click += Handle_Drive_Button_Clicks;
            DrivePreset2a.Click += Handle_Drive_Button_Clicks;
            DrivePreset2b.Click += Handle_Drive_Button_Clicks;
            DrivePreset3a.Click += Handle_Drive_Button_Clicks;
            DrivePreset3b.Click += Handle_Drive_Button_Clicks;
            DrivePreset4a.Click += Handle_Drive_Button_Clicks;
            DrivePreset4b.Click += Handle_Drive_Button_Clicks;
            DrivePreset5a.Click += Handle_Drive_Button_Clicks;
            DrivePreset5b.Click += Handle_Drive_Button_Clicks;
            
            

            #endregion

            #region Pointer Enter/Leave Handlers

            LPButton1.PointerEntered += Handle_Lower_Panel_Button_PointerEntered;
            LPButton2.PointerEntered += Handle_Lower_Panel_Button_PointerEntered;
            LPButton3.PointerEntered += Handle_Lower_Panel_Button_PointerEntered;
            LPButton4.PointerEntered += Handle_Lower_Panel_Button_PointerEntered;
            LPButton5.PointerEntered += Handle_Lower_Panel_Button_PointerEntered;
            LPButton6.PointerEntered += Handle_Lower_Panel_Button_PointerEntered;
            LPButton7.PointerEntered += Handle_Lower_Panel_Button_PointerEntered;
            LPButton8.PointerEntered += Handle_Lower_Panel_Button_PointerEntered;
            LPButton9.PointerEntered += Handle_Lower_Panel_Button_PointerEntered;
            LPButton10.PointerEntered += Handle_Lower_Panel_Button_PointerEntered;
            LPButton11.PointerEntered += Handle_Lower_Panel_Button_PointerEntered;
            LPButton12.PointerEntered += Handle_Lower_Panel_Button_PointerEntered;
            LPButton13.PointerEntered += Handle_Lower_Panel_Button_PointerEntered;
            LPButton14.PointerEntered += Handle_Lower_Panel_Button_PointerEntered;
            LPButton15.PointerEntered += Handle_Lower_Panel_Button_PointerEntered;
            LPButton16.PointerEntered += Handle_Lower_Panel_Button_PointerEntered;
            LPButton17.PointerEntered += Handle_Lower_Panel_Button_PointerEntered;
            LPButton18.PointerEntered += Handle_Lower_Panel_Button_PointerEntered;
            LPButton19.PointerEntered += Handle_Lower_Panel_Button_PointerEntered;
            LPButton20.PointerEntered += Handle_Lower_Panel_Button_PointerEntered;
            LPButton21.PointerEntered += Handle_Lower_Panel_Button_PointerEntered;
            LPButton22.PointerEntered += Handle_Lower_Panel_Button_PointerEntered;
            LPButton23.PointerEntered += Handle_Lower_Panel_Button_PointerEntered;
            LPButton24.PointerEntered += Handle_Lower_Panel_Button_PointerEntered;
            LPButton25.PointerEntered += Handle_Lower_Panel_Button_PointerEntered;
            LPButton26.PointerEntered += Handle_Lower_Panel_Button_PointerEntered;
            LPButton27.PointerEntered += Handle_Lower_Panel_Button_PointerEntered;
            LPButton28.PointerEntered += Handle_Lower_Panel_Button_PointerEntered;
            LPButton29.PointerEntered += Handle_Lower_Panel_Button_PointerEntered;
            LPButton30.PointerEntered += Handle_Lower_Panel_Button_PointerEntered;
            LPButton31.PointerEntered += Handle_Lower_Panel_Button_PointerEntered;
            LPButton32.PointerEntered += Handle_Lower_Panel_Button_PointerEntered;
            LPButton33.PointerEntered += Handle_Lower_Panel_Button_PointerEntered;
            LPButton34.PointerEntered += Handle_Lower_Panel_Button_PointerEntered;
            LPButton35.PointerEntered += Handle_Lower_Panel_Button_PointerEntered;
            LPButton36.PointerEntered += Handle_Lower_Panel_Button_PointerEntered;

            LPButton1.PointerExited += Handle_Lower_Panel_Button_PointerLeave;
            LPButton2.PointerExited += Handle_Lower_Panel_Button_PointerLeave;
            LPButton3.PointerExited += Handle_Lower_Panel_Button_PointerLeave;
            LPButton4.PointerExited += Handle_Lower_Panel_Button_PointerLeave;
            LPButton5.PointerExited += Handle_Lower_Panel_Button_PointerLeave;
            LPButton6.PointerExited += Handle_Lower_Panel_Button_PointerLeave;
            LPButton7.PointerExited += Handle_Lower_Panel_Button_PointerLeave;
            LPButton8.PointerExited += Handle_Lower_Panel_Button_PointerLeave;
            LPButton9.PointerExited += Handle_Lower_Panel_Button_PointerLeave;
            LPButton10.PointerExited += Handle_Lower_Panel_Button_PointerLeave;
            LPButton11.PointerExited += Handle_Lower_Panel_Button_PointerLeave;
            LPButton12.PointerExited += Handle_Lower_Panel_Button_PointerLeave;
            LPButton13.PointerExited += Handle_Lower_Panel_Button_PointerLeave;
            LPButton14.PointerExited += Handle_Lower_Panel_Button_PointerLeave;
            LPButton15.PointerExited += Handle_Lower_Panel_Button_PointerLeave;
            LPButton16.PointerExited += Handle_Lower_Panel_Button_PointerLeave;
            LPButton17.PointerExited += Handle_Lower_Panel_Button_PointerLeave;
            LPButton18.PointerExited += Handle_Lower_Panel_Button_PointerLeave;
            LPButton19.PointerExited += Handle_Lower_Panel_Button_PointerLeave;
            LPButton20.PointerExited += Handle_Lower_Panel_Button_PointerLeave;
            LPButton21.PointerExited += Handle_Lower_Panel_Button_PointerLeave;
            LPButton22.PointerExited += Handle_Lower_Panel_Button_PointerLeave;
            LPButton23.PointerExited += Handle_Lower_Panel_Button_PointerLeave;
            LPButton24.PointerExited += Handle_Lower_Panel_Button_PointerLeave;
            LPButton25.PointerExited += Handle_Lower_Panel_Button_PointerLeave;
            LPButton26.PointerExited += Handle_Lower_Panel_Button_PointerLeave;
            LPButton27.PointerExited += Handle_Lower_Panel_Button_PointerLeave;
            LPButton28.PointerExited += Handle_Lower_Panel_Button_PointerLeave;
            LPButton29.PointerExited += Handle_Lower_Panel_Button_PointerLeave;
            LPButton30.PointerExited += Handle_Lower_Panel_Button_PointerLeave;
            LPButton31.PointerExited += Handle_Lower_Panel_Button_PointerLeave;
            LPButton32.PointerExited += Handle_Lower_Panel_Button_PointerLeave;
            LPButton33.PointerExited += Handle_Lower_Panel_Button_PointerLeave;
            LPButton34.PointerExited += Handle_Lower_Panel_Button_PointerLeave;
            LPButton35.PointerExited += Handle_Lower_Panel_Button_PointerLeave;
            LPButton36.PointerExited += Handle_Lower_Panel_Button_PointerLeave;

            #endregion
        }

        private void DeleteRightButton_Click(object? sender, RoutedEventArgs e)
        {
            if (RPgrid.SelectedItems.Count == 0)
            {
                MessageBox MB = new MessageBox("You have to have a file selected in the Right panel");
                MB.ShowDialog(this);
                return;
            }
            
            // Here vwe want to iterate over the selected items and delete them

            DeleteFilesDialog df = new DeleteFilesDialog(RPgrid.SelectedItems);

            df.ShowDialog(this);



        }

        private void DeleteLeftButton_Click(object? sender, RoutedEventArgs e)
        {
            if (LPgrid.SelectedItems.Count == 0)
            {
                MessageBox MB = new MessageBox("You have to have a file selected in the Left panel");
                MB.ShowDialog(this);
                return;
            }
            
            // Here vwe want to iterate over the selected items and delete them

            DeleteFilesDialog df = new DeleteFilesDialog(LPgrid.SelectedItems);

            df.ShowDialog(this);
            
        }

        private void AllLeftButton_Click(object? sender, RoutedEventArgs e)
        {
            LPgrid.SelectAllFilesOnly();
        }

        private void AllRightButton_Click(object? sender, RoutedEventArgs e)
        {
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

            switch (dbe.Path.ToUpper())
            {
                case "$HOME":
                    if (nm.EndsWith("a")) // Left
                    {
                        LPpath.Text = GetHomeDirectoryPath();
                        FileUtility.PopulateFilePanel(LPgrid, LPpath.Text);
                    }
                    else // Right
                    {
                        RPpath.Text = GetHomeDirectoryPath();
                        FileUtility.PopulateFilePanel(RPgrid, RPpath.Text);
                    }

                    break;
                case "$ROOT":
                    if (nm.EndsWith("a")) // Left
                    {
                        LPpath.Text = GetRootDirectoryPath();
                        FileUtility.PopulateFilePanel(LPgrid, LPpath.Text);
                    }
                    else // Right
                    {
                        RPpath.Text = GetRootDirectoryPath();
                        FileUtility.PopulateFilePanel(RPgrid, RPpath.Text);
                    }

                    break;
                case "$DESKTOP":
                    if (nm.EndsWith("a")) // Left
                    {
                        LPpath.Text = GetDesktopDirectoryPath();
                        FileUtility.PopulateFilePanel(LPgrid, LPpath.Text);
                    }
                    else // Right
                    {
                        RPpath.Text = GetDesktopDirectoryPath();
                        FileUtility.PopulateFilePanel(RPgrid, RPpath.Text);
                    }

                    break;
                case "$DOCUMENTS":
                    if (nm.EndsWith("a")) // Left
                    {
                        LPpath.Text = GetDocumentsDirectoryPath();
                        FileUtility.PopulateFilePanel(LPgrid, LPpath.Text);
                    }
                    else // Right
                    {
                        RPpath.Text = GetDocumentsDirectoryPath();
                        FileUtility.PopulateFilePanel(RPgrid, RPpath.Text);
                    }

                    break;
                case "$PICTURES":
                    if (nm.EndsWith("a")) // Left
                    {
                        LPpath.Text = GetPicturesDirectoryPath();
                        FileUtility.PopulateFilePanel(LPgrid, LPpath.Text);
                    }
                    else // Right
                    {
                        RPpath.Text = GetPicturesDirectoryPath();
                        FileUtility.PopulateFilePanel(RPgrid, RPpath.Text);
                    }

                    break;
                case "$DOWNLOADS":
                    if (nm.EndsWith("a")) // Left
                    {
                        LPpath.Text = GetPicturesDirectoryPath();
                        FileUtility.PopulateFilePanel(LPgrid, LPpath.Text);
                    }
                    else // Right
                    {
                        RPpath.Text = GetPicturesDirectoryPath();
                        FileUtility.PopulateFilePanel(RPgrid, RPpath.Text);
                    }

                    break;
                default:
                    if (nm.EndsWith("a")) // Left
                    {
                        LPpath.Text = dbe.Path;
                        FileUtility.PopulateFilePanel(LPgrid, LPpath.Text);
                    }
                    else // Right
                    {
                        RPpath.Text = dbe.Path;
                        FileUtility.PopulateFilePanel(RPgrid, RPpath.Text);
                    }

                    break;
            }


        }

        private void Handle_Lower_Panel_Button_PointerLeave(object? sender, PointerEventArgs e)
        {
            if (pop != null && pop.IsVisible )
            {
                pop.Close();
                pop = null;
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
                if (item.Bname.ToUpper() == nm.ToUpper() && item.ToolTip!=null && LastButtonPopupName != B.Name)
                {
                    // We have a winner - do the action

                    LastButtonPopupName = B.Name;
                                        
                    pop = new PopUp();
                    pop.Title = B.Content.ToString();

                    pop.SetText(item.ToolTip);

                    var p = VisualRoot.PointToScreen(e.GetPosition(this));

                    pop.Position = new Avalonia.PixelPoint((int)p.X+ 30, (int)p.Y + 30);

                    pop.Width = 240;
                    pop.Height = 100;

                    pop.Show(); 

                    //Avalonia.Threading.DispatcherTimer.Run(() =>
                    //{
                    //    pop.Close();
                    //    return false;
                    //}, TimeSpan.FromSeconds(3)); // adjust the delay as needed

                }
            }
        }

        private void RightToLeftButton_Click(object? sender, RoutedEventArgs e)
        {
            LPpath.Text = RPpath.Text;
            FileUtility.PopulateFilePanel(LPgrid,LPpath.Text);
        }

        private void LeftToRightButton_Click(object? sender, RoutedEventArgs e)
        {
            RPpath.Text = LPpath.Text;
            FileUtility.PopulateFilePanel(RPgrid, RPpath.Text);
            
        }

        private void RPpath_KeyUp(object? sender, KeyEventArgs e)
        {
            if (e.Key == Key.Enter)
            {
                FileUtility.PopulateFilePanel(RPgrid, RPpath.Text);
            }
        }

        private void LPpath_KeyUp(object? sender, KeyEventArgs e)
        {
            if (e.Key == Key.Enter)
            {
                FileUtility.PopulateFilePanel(LPgrid, LPpath.Text);
            }
        }

        private void ClearRightButton_Click(object? sender, RoutedEventArgs e)
        {
            RPgrid.SelectedItems.Clear();
            RPgrid.ReRender();
        }

        private void ClearLeftButton_Click(object? sender, RoutedEventArgs e)
        {
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

            string nm = B.Name;

            foreach (ButtonEntry item in TheButtons)
            {
                if (item.Bname.ToUpper() == nm.ToUpper())
                {
                    // We have a winner - do the action

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
                                // with Process.Waitforexit() in between

                                string[] args = newaction.Split(',', StringSplitOptions.RemoveEmptyEntries);

                                foreach (string arg in args)
                                {
                                    Process.Start(new ProcessStartInfo()
                                    {
                                        FileName = item.Bcontent,
                                        Arguments = arg,
                                        UseShellExecute = item.ShellExecute,
                                        CreateNoWindow = item.ShowWindow
                                    }).WaitForExit();
                                }
                            }
                            else
                            {
                                Process.Start(new ProcessStartInfo()
                                {
                                    FileName = item.Bcontent,
                                    Arguments = newaction,
                                    UseShellExecute = item.ShellExecute,
                                    CreateNoWindow = item.ShowWindow
                                });
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

                    PTH = MakePathEnvSafe(LPpath.Text) + LPgrid.GetFirstSelectedFolder();

                    string ret = bcontent.Replace("%FD%", PTH);

                    return ret;

                }
                else if (RPgrid.GetFirstSelectedFolder() != "")
                {
                    // the right grid has a folder selected

                    PTH = MakePathEnvSafe(RPpath.Text) + RPgrid.GetFirstSelectedFolder();

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
                        PTH += MakePathEnvSafe(LPpath.Text) + af.Name + " ";
                    }

                    string ret = bcontent.Replace("%AF%", PTH);

                    return ret;

                }
                else if (RPgrid.GetListOfSelectedFiles().Count > 0)
                {
                    // the left grid has some files selected

                    foreach (AFileEntry af in RPgrid.GetListOfSelectedFiles())
                    {
                        PTH += MakePathEnvSafe(RPpath.Text) + af.Name + " ";
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
                        PTH += MakePathEnvSafe(LPpath.Text) + af.Name + ",";
                    }

                    string ret = bcontent.Replace("%LAF%", PTH);

                    return ret;

                }
                else if (RPgrid.GetListOfSelectedFiles().Count > 0)
                {
                    // the left grid has some files selected

                    foreach (AFileEntry af in RPgrid.GetListOfSelectedFiles())
                    {
                        PTH += MakePathEnvSafe(RPpath.Text) + af.Name + ",";
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

                    PTH = MakePathEnvSafe(LPpath.Text) + thelista[0].Name + " " + MakePathEnvSafe(RPpath.Text) + thelistb[0].Name;

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

                    PTH = MakePathEnvSafe(LPpath.Text) + thelista[0].Name;

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

                    PTH = MakePathEnvSafe(RPpath.Text) + thelista[0].Name;

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
                        PTH += MakePathEnvSafe(RPpath.Text) + af.Name + ",";
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
                        PTH += MakePathEnvSafe(LPpath.Text) + af.Name + ",";
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
            
            if (bcontent.Contains("%LPATH%"))
            {
                string ret = bcontent.Replace("%LPATH%", MakePathEnvSafe(LPpath.Text));

                return ret;
            }
            
            if (bcontent.Contains("%RPATH%"))
            {
                string ret = bcontent.Replace("%RPATH%", MakePathEnvSafe(RPpath.Text));

                return ret;
            }   
            
            
            
            return bcontent;
        }

        private void SwapButton_Click(object? sender, RoutedEventArgs e)
        {
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

        private void MoveRightButton_Click(object? sender, RoutedEventArgs e)
        {
            if (LPgrid.SelectedItems.Count > 0)
            {
                string spath = LPpath.Text.Replace(@"\\", @"\");
                string tpath = RPpath.Text.Replace(@"\\", @"\");

                if (Environment.OSVersion.Platform == PlatformID.Win32NT)
                {
                    if (!spath.EndsWith(@"\"))
                    {
                        spath += @"\";

                    }

                    if (!tpath.EndsWith(@"\"))
                    {
                        tpath += @"\";

                    }
                }
                else if (Environment.OSVersion.Platform == PlatformID.Unix || Environment.OSVersion.Platform == PlatformID.MacOSX)
                {
                    if (!spath.EndsWith(@"/"))
                    {
                        spath += @"/";

                    }
                    if (!tpath.EndsWith(@"/"))
                    {
                        tpath += @"/";

                    }
                }

                List<object> Sellist = LPgrid.SelectedItems;

                foreach (AFileEntry item in Sellist)
                {
                    if (item.Typ)
                    {
                        string FullPath = spath + item.Name;

                        string NewPath = tpath + item.Name;

                        //Directory.CreateDirectory(NewPath);

                        FileUtility.MoveDirectory(FullPath, NewPath);

                        RefreshRPGrid();
                        
                    }
                    else
                    {
                        string FullPath = spath + item.Name;

                        FileUtility.MoveFile(FullPath, tpath);

                        RefreshRPGrid();

                    }
                    //Console.WriteLine(item);
                }
            }
            else
            {
                //FileUtility.CopyDirectoryToFolder(LPpath.Text, RPpath.Text);
            }

            RefreshLPGrid();
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

        private void MoveLeftButton_Click(object? sender, RoutedEventArgs e)
        {
            if (RPgrid.SelectedItems.Count > 0)
            {
                string spath = RPpath.Text.Replace(@"\\", @"\");
                string tpath = LPpath.Text.Replace(@"\\", @"\");

                if (Environment.OSVersion.Platform == PlatformID.Win32NT)
                {
                    if (!spath.EndsWith(@"\"))
                    {
                        spath += @"\";

                    }

                    if (!tpath.EndsWith(@"\"))
                    {
                        tpath += @"\";

                    }
                }
                else if (Environment.OSVersion.Platform == PlatformID.Unix || Environment.OSVersion.Platform == PlatformID.MacOSX)
                {
                    if (!spath.EndsWith(@"/"))
                    {
                        spath += @"/";

                    }
                    if (!tpath.EndsWith(@"/"))
                    {
                        tpath += @"/";

                    }
                }

                List<object> Sellist = RPgrid.SelectedItems;

                foreach (AFileEntry item in Sellist)
                {
                    if (item.Typ)
                    {
                        string FullPath = spath + item.Name;

                        string NewPath = tpath + item.Name;

                        //Directory.CreateDirectory(NewPath);

                        FileUtility.MoveDirectory(FullPath, NewPath);

                        RefreshLPGrid();

                    }
                    else
                    {
                        string FullPath = spath + item.Name;

                        FileUtility.MoveFile(FullPath, tpath);

                        RefreshLPGrid();

                    }
                    //Console.WriteLine(item);
                }
            }
            else
            {
                //FileUtility.CopyDirectoryToFolder(LPpath.Text, RPpath.Text);
            }

            RefreshRPGrid();
        }

        private void CopyRightButton_Click(object? sender, RoutedEventArgs e)
        {
            if (LPgrid.SelectedItems.Count > 0)
            {
                string spath = LPpath.Text.Replace(@"\\",@"\");
                string tpath = RPpath.Text.Replace(@"\\", @"\");

                if (Environment.OSVersion.Platform == PlatformID.Win32NT)
                {
                    if (!spath.EndsWith(@"\"))
                    {
                        spath += @"\";

                    }

                    if (!tpath.EndsWith(@"\"))
                    {
                        tpath += @"\";

                    }
                }
                else if (Environment.OSVersion.Platform == PlatformID.Unix || Environment.OSVersion.Platform == PlatformID.MacOSX)
                {
                    if (!spath.EndsWith(@"/"))
                    {
                        spath += @"/";

                    }
                    if (!tpath.EndsWith(@"/"))
                    {
                        tpath += @"/";

                    }
                }

                List<object> Sellist = LPgrid.SelectedItems;

                foreach (AFileEntry item in Sellist)
                {
                    if (item.Typ)
                    {
                        string FullPath = spath + item.Name;
                        
                        string NewPath = tpath + item.Name;

                        Directory.CreateDirectory(NewPath);

                        FileUtility.CopyDirectoryToFolder(FullPath, NewPath);

                        RefreshRPGrid();
                    }
                    else
                    {
                        string FullPath = spath + item.Name;

                        FileUtility.CopyFileToFolder(FullPath, tpath);

                        RefreshRPGrid();

                    }
                    //Console.WriteLine(item);
                }
            }
            else
            {
                //FileUtility.CopyDirectoryToFolder(LPpath.Text, RPpath.Text);
            }
        }

        private void CopyLeftButton_Click(object? sender, RoutedEventArgs e)
        {
            if (RPgrid.SelectedItems.Count > 0)
            {
                string spath = RPpath.Text.Replace(@"\\", @"\");
                string tpath = LPpath.Text.Replace(@"\\", @"\");

                if (Environment.OSVersion.Platform == PlatformID.Win32NT)
                {
                    if (!spath.EndsWith(@"\"))
                    {
                        spath += @"\";

                    }

                    if (!tpath.EndsWith(@"\"))
                    {
                        tpath += @"\";

                    }
                }
                else if (Environment.OSVersion.Platform == PlatformID.Unix || Environment.OSVersion.Platform == PlatformID.MacOSX)
                {
                    if (!spath.EndsWith(@"/"))
                    {
                        spath += @"/";

                    }
                    if (!tpath.EndsWith(@"/"))
                    {
                        tpath += @"/";

                    }
                }

                List<object> Sellist = RPgrid.SelectedItems;

                foreach (AFileEntry item in Sellist)
                {
                    if (item.Typ)
                    {
                        string FullPath = spath + item.Name;

                        string NewPath = tpath + item.Name;

                        Directory.CreateDirectory(NewPath);

                        FileUtility.CopyDirectoryToFolder(FullPath, NewPath);

                        RefreshLPGrid();
                    }
                    else
                    {
                        string FullPath = spath + item.Name;

                        FileUtility.CopyFileToFolder(FullPath, tpath);

                        RefreshLPGrid();

                    }
                    //Console.WriteLine(item);
                }
            }
            else
            {
                //FileUtility.CopyDirectoryToFolder(LPpath.Text, RPpath.Text);
            }
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
            if (Environment.OSVersion.Platform == PlatformID.Win32NT)
            {
                string[] arr = LPpath.Text.Split(@"\");

                string newpath = "";

                for (int i = 0; i < arr.Length - 1; i++)
                {
                    newpath += arr[i] + @"\";
                }

                if (newpath.EndsWith(@"\") && newpath.Length > 3)
                {
                    newpath = newpath.Substring(0, newpath.Length - 1);
                }

                LPpath.Text = newpath;
            }
            else if (Environment.OSVersion.Platform == PlatformID.Unix || Environment.OSVersion.Platform == PlatformID.MacOSX)
            {
                string[] arr = LPpath.Text.Split(@"/");

                string newpath = "";

                for (int i = 0; i < arr.Length - 1; i++)
                {
                    newpath += arr[i] + @"/";
                }

                if (newpath.EndsWith(@"/") && newpath.Length > 1)
                {
                    newpath = newpath.Substring(0, newpath.Length - 1);
                }
                LPpath.Text = newpath;

            }

            //string[] arr = LPpath.Text.Split(@"\");

            //string newpath = "";

            //for (int i = 0; i < arr.Length - 1; i++)
            //{
            //    newpath += arr[i] + @"\";
            //}

            //if (newpath.EndsWith(@"\") && newpath.Length > 3)
            //{
            //    newpath = newpath.Substring(0, newpath.Length - 1);
            //}

            //LPpath.Text = newpath;
            
            FileUtility.PopulateFilePanel(LPgrid, LPpath.Text);

        }

        private void RPBackButton_Click(object? sender, RoutedEventArgs e)
        {
            if (Environment.OSVersion.Platform == PlatformID.Win32NT)
            {
                string[] arr = RPpath.Text.Split(@"\");

                string newpath = "";

                for (int i = 0; i < arr.Length - 1; i++)
                {
                    newpath += arr[i] + @"\";
                }

                if (newpath.EndsWith(@"\") && newpath.Length > 3)
                {
                    newpath = newpath.Substring(0, newpath.Length - 1);
                }

                RPpath.Text = newpath;
            }
            else if (Environment.OSVersion.Platform == PlatformID.Unix || Environment.OSVersion.Platform == PlatformID.MacOSX)
            {
                string[] arr = RPpath.Text.Split(@"/");

                string newpath = "";

                for (int i = 0; i < arr.Length - 1; i++)
                {
                    newpath += arr[i] + @"/";
                }

                if (newpath.EndsWith(@"/") && newpath.Length > 1)
                {
                    newpath = newpath.Substring(0, newpath.Length - 1);
                }

                RPpath.Text = newpath;
            }

            FileUtility.PopulateFilePanel(RPgrid, RPpath.Text);
        }

        private void RPgrid_GridItemDoubleClick(object? sender, GridHoverItem e)
        {
            var it = e.ItemUnderMouse as AFileEntry;

            if (Environment.OSVersion.Platform == PlatformID.Win32NT)
            {
                if (it.Typ)
                {
                    oldpath = RPpath.Text;
                    RPpath.Text = (RPpath.Text + "\\" + it.Name).Replace(@"\\", @"\");
                    FileUtility.PopulateFilePanel(RPgrid, RPpath.Text);
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
                    oldpath = RPpath.Text;
                    
                    RPpath.Text = (RPpath.Text + "/" + it.Name).Replace(@"//", @"/");
                    FileUtility.PopulateFilePanel(RPgrid, RPpath.Text);
                }
                else
                {
                    // its an actual file so can we execute it?

                    if (FileExtensionIsExecutable(it.Name.ToUpper()))
                    {
                        // we can execute it

                        string thingtoexecute = (RPpath.Text + "/" + it.Name);

                        Process.Start(new ProcessStartInfo()
                        {
                            FileName = thingtoexecute,
                            UseShellExecute = true,
                        });

                        //Process.Start(RPpath.Text + "/" + it.Name);
                    }

                }

            }
                        
        }
                
        private void LPgrid_GridItemDoubleClick(object? sender, GridHoverItem e)
        {
            var it = e.ItemUnderMouse as AFileEntry;
            if (Environment.OSVersion.Platform == PlatformID.Win32NT)
            {
                if (it.Typ)
                {
                    // Its A folder so gets go into it
                    oldpath = LPpath.Text;
                    LPpath.Text = (LPpath.Text + "\\" + it.Name).Replace(@"\\", @"\");
                    FileUtility.PopulateFilePanel(LPgrid, LPpath.Text);
                }
                else
                {
                    // its an actual file so can we execute it?

                    if (FileExtensionIsImage(it.Name.ToUpper()) && UseIntegratedImageViewer)
                    {
                        string thingtoexecute = (LPpath.Text + "\\" + it.Name).Replace(@"\\", @"\");

                        ImageViewer iv = new ImageViewer(thingtoexecute);

                        iv.ShowDialog(this);

                    }
                    else if (FileExtensionIsExecutable(it.Name.ToUpper()))
                    {
                        // we can execute it

                        string thingtoexecute = (LPpath.Text + "\\" + it.Name).Replace(@"\\", @"\");

                        Process.Start(new ProcessStartInfo()
                        {
                            FileName = thingtoexecute,
                            UseShellExecute = true,
                        });


                        //Process.Start((LPpath.Text + "\\" + it.Name).Replace(@"\\",@"\"));
                    }
                    
                }
            }
            else if (Environment.OSVersion.Platform == PlatformID.Unix || Environment.OSVersion.Platform == PlatformID.MacOSX)
            {
                if (it.Typ)
                {
                    oldpath = LPpath.Text;
                    LPpath.Text = (LPpath.Text + "/" + it.Name).Replace(@"//", @"/");
                    FileUtility.PopulateFilePanel(LPgrid, LPpath.Text);
                }
                else
                {
                    if (FileExtensionIsImage(it.Name.ToUpper()) && UseIntegratedImageViewer)
                    {
                        string thingtoexecute = (LPpath.Text + "//" + it.Name).Replace(@"//", @"/");
                        
                        ImageViewer iv = new ImageViewer(thingtoexecute);

                        iv.ShowDialog(this);

                    }
                    else 
                    if (FileExtensionIsExecutable(it.Name.ToUpper()))
                    {
                        // we can execute it

                        string thingtoexecute = (LPpath.Text + "/" + it.Name);

                        Process.Start(new ProcessStartInfo()
                        {
                            FileName = thingtoexecute,
                            UseShellExecute = true,
                        });

                        //Process.Start(LPpath.Text + "/" + it.Name);
                    }

                }

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

            foreach (string s in ExecutableStuff)
            {
                if (v.ToUpper().EndsWith(s))
                {
                    result = true;
                    break;
                }
            }

            return result;
        }

        private void MainWindowGridContainer_SizeChanged(object? sender, SizeChangedEventArgs e)
        {

            double nwidth = e.NewSize.Width * .45;

            double nheight = ((e.NewSize.Height) - 30) * .7;

            LPgrid.SetGridSize((int)nwidth - 8, (int)nheight - 16);
            RPgrid.SetGridSize((int)nwidth - 8, (int)nheight - 16 );
        }

        private void RefreshLPGrid ()
        {
            FileUtility.PopulateFilePanel(LPgrid, LPpath.Text);
        }

        private void RefreshRPGrid()
        {
            FileUtility.PopulateFilePanel(RPgrid, RPpath.Text);
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
                                             ShellExecute = (string)btn.Element("Shell"),
                                             ShowWindow = (string)btn.Element("Window"),
                                             ToolTip = (string)btn.Element("ToolTip")
                                         };

                
                // Apply settings to each button
                foreach (var buttonSettings in buttonSettingsList)
                {
                    // Find the grid where the buttons exist
                    var grid = (Grid)window.FindControl<Control>("ButtonGrid");
                    
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
                            Button b1 = (Button)window.FindControl<Control>("DrivePreset1a");
                            Button b2 = (Button)window.FindControl<Control>("DrivePreset1b");
                            if (thing.Content != null)
                            {
                                b1.Content = "<-" + thing.Content.ToString();
                                b2.Content = thing.Content.ToString() + "->";
                            }

                            b1.Tag = thing;
                            b2.Tag = thing;

                            break;
                        case "2":
                            Button b3 = (Button)window.FindControl<Control>("DrivePreset2a");
                            Button b4 = (Button)window.FindControl<Control>("DrivePreset2b");
                            if (thing.Content != null)
                            {
                                b3.Content = "<-" + thing.Content.ToString();
                                b4.Content = thing.Content.ToString() + "->";
                            }

                            b3.Tag = thing;
                            b4.Tag = thing;

                            break;
                        case "3":
                            Button b5 = (Button)window.FindControl<Control>("DrivePreset3a");
                            Button b6 = (Button)window.FindControl<Control>("DrivePreset3b");
                            if (thing.Content != null)
                            {
                                b5.Content = "<-" + thing.Content.ToString();
                                b6.Content = thing.Content.ToString() + "->";
                            }

                            b5.Tag = thing;
                            b6.Tag = thing;

                            break;
                        case "4":
                            Button b7 = (Button)window.FindControl<Control>("DrivePreset4a");
                            Button b8 = (Button)window.FindControl<Control>("DrivePreset4b");
                            if (thing.Content != null)
                            {
                                b7.Content = "<-" + thing.Content.ToString();
                                b8.Content = thing.Content.ToString() + "->";
                            }

                            b7.Tag = thing;
                            b8.Tag = thing;

                            break;
                        case "5":
                            Button b9 = (Button)window.FindControl<Control>("DrivePreset5a");
                            Button b10 = (Button)window.FindControl<Control>("DrivePreset5b");
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
        
    }
    
    public class DriveButtonEntry
    {
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