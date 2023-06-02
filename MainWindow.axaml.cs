using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Media;
using Avalonia.Media.Imaging;
//using Microsoft.CodeAnalysis.CSharp.Syntax;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Xml.Linq;
using YamlDotNet.Serialization;
using static System.Net.WebRequestMethods;
using Bitmap = Avalonia.Media.Imaging.Bitmap;
using Color = Avalonia.Media.Color;
using Image = Avalonia.Controls.Image;

namespace DirOpusReImagined
{
    public partial class MainWindow : Window
    {

        private Avalonia.Rect LRB = new Avalonia.Rect();
        private Avalonia.Rect RRB = new Avalonia.Rect();
        private Avalonia.Size OrigSize = new Avalonia.Size();

        private List<ButtonEntry> TheButtons = new List<ButtonEntry>();

        public MainWindow()
        {
            InitializeComponent();

            // Apply The Settings if possible

            ApplyButtonSettingsFromXml("Configuration.xml", this);

            MainWindowGridContainer.SizeChanged += MainWindowGridContainer_SizeChanged;

            Bitmap B1 = LoadImage(ImageStrings.BackButton);

            Image I1 = new Image();
            Image I2 = new Image();
            I1.Source = B1;
            I1.Width = RPBackButton.Width + 8;
            I1.Height = RPBackButton.Height+ 8;

            I2.Source = B1;
            I2.Width = LPBackButton.Width + 8;
            I2.Height = LPBackButton.Height + 8;

            SwapButton.Click += SwapButton_Click;

            CopyLeftButton.Click += CopyLeftButton_Click;
            CopyRightButton.Click += CopyRightButton_Click;

            MoveLeftButton.Click += MoveLeftButton_Click;  
            MoveRightButton.Click += MoveRightButton_Click;

            RPBackButton.Content = I1;
            LPBackButton.Content = I2;

            RPBackButton.Click += RPBackButton_Click;
            LPBackButton.Click += LPBackButton_Click;

            LPgrid.GridFontSize = 16;
            RPgrid.GridFontSize = 16;
            LPgrid.GridHeaderFontSize = 16;
            RPgrid.GridHeaderFontSize = 16;

            LPgrid.GridTitle = "Left Panel";
            RPgrid.GridTitle = "Right Panel";

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

            LPpath.Text = GetRootDirectoryPath();
            RPpath.Text = GetRootDirectoryPath();

            PopulateFilePanel(LPgrid,LPpath.Text);
            PopulateFilePanel(RPgrid, RPpath.Text);

            // wire up button click events for the lower panel buttons

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

                    Process.Start(new ProcessStartInfo()
                    {
                        FileName = item.Bcontent,
                        Arguments = newaction,
                        UseShellExecute = false,
                        CreateNoWindow = true
                    });

                }
            }


        }

        private string ParseTheArgs(string bcontent)
        {
            // this will attermpt to parse the bcontent string and
            // return the action to be performed

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

                    return bcontent;
                }
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

            LPgrid.SelectedItems.Clear();
            LPgrid.ReRender();
        }

        private void LPgrid_GridItemClick(object? sender, GridHoverItem e)
        {
            // If there are any selected items in the Right Grid Deselect them

            RPgrid.SelectedItems.Clear();
            RPgrid.ReRender();
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
                if (!result.EndsWith(@"\"))
                {
                    result += @"\";

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
            
            PopulateFilePanel(LPgrid, LPpath.Text);

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

            PopulateFilePanel(RPgrid, RPpath.Text);
        }

        private void RPgrid_GridItemDoubleClick(object? sender, GridHoverItem e)
        {
            var it = e.ItemUnderMouse as AFileEntry;

            if (Environment.OSVersion.Platform == PlatformID.Win32NT)
            {
                if (it.Typ)
                {
                    RPpath.Text = (RPpath.Text + "\\" + it.Name).Replace(@"\\", @"\");
                    PopulateFilePanel(RPgrid, RPpath.Text);
                }
                else
                {
                    // its an actual file so can we execute it?

                    if (it.Name.ToUpper().EndsWith(".EXE") ||
                        it.Name.ToUpper().EndsWith(".JPG") ||
                        it.Name.ToUpper().EndsWith(".PNG") ||
                        it.Name.ToUpper().EndsWith(".GIF") ||
                        it.Name.ToUpper().EndsWith(".BMP") ||
                        it.Name.ToUpper().EndsWith(".ICO") ||
                        it.Name.ToUpper().EndsWith(".TXT") ||
                        it.Name.ToUpper().EndsWith(".PDF") ||
                        it.Name.ToUpper().EndsWith(".DOC") ||
                        it.Name.ToUpper().EndsWith(".DOCX") ||
                        it.Name.ToUpper().EndsWith(".XLS") ||
                        it.Name.ToUpper().EndsWith(".XLSX") ||
                        it.Name.ToUpper().EndsWith(".PPT") ||
                        it.Name.ToUpper().EndsWith(".PPTX") ||
                        it.Name.ToUpper().EndsWith(".MP3") ||
                        it.Name.ToUpper().EndsWith(".MP4") ||
                        it.Name.ToUpper().EndsWith(".WAV") ||
                        it.Name.ToUpper().EndsWith(".AVI") ||
                        it.Name.ToUpper().EndsWith(".WMV") ||
                        it.Name.ToUpper().EndsWith(".WMA") ||
                        it.Name.ToUpper().EndsWith(".MPEG") ||
                        it.Name.ToUpper().EndsWith(".MPEG4") ||
                        it.Name.ToUpper().EndsWith(".MKV") ||
                        it.Name.ToUpper().EndsWith(".MOV") ||
                        it.Name.ToUpper().EndsWith(".FLV") ||
                        it.Name.ToUpper().EndsWith(".ZIP") ||
                        it.Name.ToUpper().EndsWith(".RAR") ||
                        it.Name.ToUpper().EndsWith(".7Z") ||
                        it.Name.ToUpper().EndsWith(".GZ") ||
                        it.Name.ToUpper().EndsWith(".TAR") ||
                        it.Name.ToUpper().EndsWith(".ISO") ||
                        it.Name.ToUpper().EndsWith(".IMG"))
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
                    RPpath.Text = (RPpath.Text + "/" + it.Name).Replace(@"//", @"/");
                    PopulateFilePanel(RPgrid, RPpath.Text);
                }
                else
                {
                    // its an actual file so can we execute it?

                    if (it.Name.ToUpper().EndsWith(".EXE") ||
                        it.Name.ToUpper().EndsWith(".JPG") ||
                        it.Name.ToUpper().EndsWith(".PNG") ||
                        it.Name.ToUpper().EndsWith(".GIF") ||
                        it.Name.ToUpper().EndsWith(".BMP") ||
                        it.Name.ToUpper().EndsWith(".ICO") ||
                        it.Name.ToUpper().EndsWith(".TXT") ||
                        it.Name.ToUpper().EndsWith(".PDF") ||
                        it.Name.ToUpper().EndsWith(".DOC") ||
                        it.Name.ToUpper().EndsWith(".DOCX") ||
                        it.Name.ToUpper().EndsWith(".XLS") ||
                        it.Name.ToUpper().EndsWith(".XLSX") ||
                        it.Name.ToUpper().EndsWith(".PPT") ||
                        it.Name.ToUpper().EndsWith(".PPTX") ||
                        it.Name.ToUpper().EndsWith(".MP3") ||
                        it.Name.ToUpper().EndsWith(".MP4") ||
                        it.Name.ToUpper().EndsWith(".WAV") ||
                        it.Name.ToUpper().EndsWith(".AVI") ||
                        it.Name.ToUpper().EndsWith(".WMV") ||
                        it.Name.ToUpper().EndsWith(".WMA") ||
                        it.Name.ToUpper().EndsWith(".MPEG") ||
                        it.Name.ToUpper().EndsWith(".MPEG4") ||
                        it.Name.ToUpper().EndsWith(".MKV") ||
                        it.Name.ToUpper().EndsWith(".MOV") ||
                        it.Name.ToUpper().EndsWith(".FLV") ||
                        it.Name.ToUpper().EndsWith(".ZIP") ||
                        it.Name.ToUpper().EndsWith(".RAR") ||
                        it.Name.ToUpper().EndsWith(".7Z") ||
                        it.Name.ToUpper().EndsWith(".GZ") ||
                        it.Name.ToUpper().EndsWith(".TAR") ||
                        it.Name.ToUpper().EndsWith(".ISO") ||
                        it.Name.ToUpper().EndsWith(".IMG"))
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
                    LPpath.Text = (LPpath.Text + "\\" + it.Name).Replace(@"\\", @"\");
                    PopulateFilePanel(LPgrid, LPpath.Text);
                }
                else
                {
                    // its an actual file so can we execute it?

                    if (it.Name.ToUpper().EndsWith(".EXE") ||
                        it.Name.ToUpper().EndsWith(".JPG") ||
                        it.Name.ToUpper().EndsWith(".PNG") ||
                        it.Name.ToUpper().EndsWith(".GIF") ||
                        it.Name.ToUpper().EndsWith(".BMP") ||
                        it.Name.ToUpper().EndsWith(".ICO") ||
                        it.Name.ToUpper().EndsWith(".TXT") ||
                        it.Name.ToUpper().EndsWith(".PDF") ||
                        it.Name.ToUpper().EndsWith(".DOC") ||
                        it.Name.ToUpper().EndsWith(".DOCX") ||
                        it.Name.ToUpper().EndsWith(".XLS") ||
                        it.Name.ToUpper().EndsWith(".XLSX") ||
                        it.Name.ToUpper().EndsWith(".PPT") ||
                        it.Name.ToUpper().EndsWith(".PPTX") ||
                        it.Name.ToUpper().EndsWith(".MP3") ||
                        it.Name.ToUpper().EndsWith(".MP4") ||
                        it.Name.ToUpper().EndsWith(".WAV") ||
                        it.Name.ToUpper().EndsWith(".AVI") ||
                        it.Name.ToUpper().EndsWith(".WMV") ||
                        it.Name.ToUpper().EndsWith(".WMA") ||
                        it.Name.ToUpper().EndsWith(".MPEG") ||
                        it.Name.ToUpper().EndsWith(".MPEG4") ||
                        it.Name.ToUpper().EndsWith(".MKV") ||
                        it.Name.ToUpper().EndsWith(".MOV") ||
                        it.Name.ToUpper().EndsWith(".FLV") ||
                        it.Name.ToUpper().EndsWith(".ZIP") ||
                        it.Name.ToUpper().EndsWith(".RAR") ||
                        it.Name.ToUpper().EndsWith(".7Z") ||
                        it.Name.ToUpper().EndsWith(".GZ") ||
                        it.Name.ToUpper().EndsWith(".TAR") ||
                        it.Name.ToUpper().EndsWith(".ISO") ||
                        it.Name.ToUpper().EndsWith(".IMG"))
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
                    LPpath.Text = (LPpath.Text + "/" + it.Name).Replace(@"//", @"/");
                    PopulateFilePanel(LPgrid, LPpath.Text);
                }
                else
                {
                    if (it.Name.ToUpper().EndsWith(".EXE") ||
                        it.Name.ToUpper().EndsWith(".JPG") ||
                        it.Name.ToUpper().EndsWith(".PNG") ||
                        it.Name.ToUpper().EndsWith(".GIF") ||
                        it.Name.ToUpper().EndsWith(".BMP") ||
                        it.Name.ToUpper().EndsWith(".ICO") ||
                        it.Name.ToUpper().EndsWith(".TXT") ||
                        it.Name.ToUpper().EndsWith(".PDF") ||
                        it.Name.ToUpper().EndsWith(".DOC") ||
                        it.Name.ToUpper().EndsWith(".DOCX") ||
                        it.Name.ToUpper().EndsWith(".XLS") ||
                        it.Name.ToUpper().EndsWith(".XLSX") ||
                        it.Name.ToUpper().EndsWith(".PPT") ||
                        it.Name.ToUpper().EndsWith(".PPTX") ||
                        it.Name.ToUpper().EndsWith(".MP3") ||
                        it.Name.ToUpper().EndsWith(".MP4") ||
                        it.Name.ToUpper().EndsWith(".WAV") ||
                        it.Name.ToUpper().EndsWith(".AVI") ||
                        it.Name.ToUpper().EndsWith(".WMV") ||
                        it.Name.ToUpper().EndsWith(".WMA") ||
                        it.Name.ToUpper().EndsWith(".MPEG") ||
                        it.Name.ToUpper().EndsWith(".MPEG4") ||
                        it.Name.ToUpper().EndsWith(".MKV") ||
                        it.Name.ToUpper().EndsWith(".MOV") ||
                        it.Name.ToUpper().EndsWith(".FLV") ||
                        it.Name.ToUpper().EndsWith(".ZIP") ||
                        it.Name.ToUpper().EndsWith(".RAR") ||
                        it.Name.ToUpper().EndsWith(".7Z") ||
                        it.Name.ToUpper().EndsWith(".GZ") ||
                        it.Name.ToUpper().EndsWith(".TAR") ||
                        it.Name.ToUpper().EndsWith(".ISO") ||
                        it.Name.ToUpper().EndsWith(".IMG"))
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

        private void MainWindowGridContainer_SizeChanged(object? sender, SizeChangedEventArgs e)
        {

            double nwidth = e.NewSize.Width * .45;

            double nheight = ((e.NewSize.Height) - 30) * .7;

            LPgrid.SetGridSize((int)nwidth - 8, (int)nheight - 16);
            RPgrid.SetGridSize((int)nwidth - 8, (int)nheight - 16 );
        }

        private void RefreshLPGrid ()
        {
              PopulateFilePanel(LPgrid, LPpath.Text);
        }

        private void RefreshRPGrid()
        {
            PopulateFilePanel(RPgrid, RPpath.Text);
        }   

        private void PopulateFilePanel(TAIDataGrid ThePanel, string PATHNAME)
        {
            //LPgrid.PopulateGrid(PATHNAME);

            //var Directories = System.IO.Directory.EnumerateDirectories(PATHNAME);

            // using linq to sort the directories by name alphabetically
            var Directories = System.IO.Directory.EnumerateDirectories(PATHNAME)
                .OrderBy(path => path, StringComparer.OrdinalIgnoreCase)
                .ToList();

            ThePanel.SuspendRendering = true;

            ThePanel.Items.Clear();
            List<Object> FileList = new List<Object>();

            foreach (string dir in Directories)
            {
                DirectoryInfo di = new DirectoryInfo(dir);
                try
                {

                    if (di.Attributes.HasFlag(FileAttributes.System))
                    {
                        continue;
                    }

                    string flags = GetAbbreviatedAttributes(di.Attributes);
                                        
                    var ds = di.GetDirectories().GetUpperBound(0) + 1;
                    var fs = di.GetFiles().GetUpperBound(0) + 1;

                    FileList.Add(new AFileEntry(di.Name, 0, true, ds, fs,flags));
                }
                catch (UnauthorizedAccessException)
                {
                    
                    try
                    {                    
                        FileList.Add(new AFileEntry(di.Name, 0, true, 0, 0,""));
                    }
                    catch
                    {

                    }
                }
                //var ds = di.GetDirectories().GetUpperBound(0);
                //var fs = di.GetFiles().GetUpperBound(0);

                //FileList.Add(new AFileEntry(di.Name, 0, true,ds,fs));
            }

            // Using Linq to sort the files alphabetically
            var files = System.IO.Directory.EnumerateFiles(PATHNAME)
                .OrderBy(path => path, StringComparer.OrdinalIgnoreCase)
                .ToList(); ;

            foreach (string file in files)
            {
                try
                {
                    FileInfo fi = new FileInfo(file);

                    FileAttributes fa = System.IO.File.GetAttributes(fi.FullName);

                    string flags = GetAbbreviatedAttributes(fa);

                    FileList.Add(new AFileEntry(fi.Name, (int)fi.Length, false,flags));
                }
                catch
                {
                    
                }
            }

            ThePanel.Items = FileList.OfType<object>().ToList(); 

            ThePanel.SuspendRendering = false;
        }

        private string GetRootDirectoryPath()
        {
            string rootDirectoryPath = "";

            if (Environment.OSVersion.Platform == PlatformID.Win32NT)
            {
                rootDirectoryPath = Directory.GetDirectoryRoot(Environment.SystemDirectory);
            }
            else if (Environment.OSVersion.Platform == PlatformID.Unix || Environment.OSVersion.Platform == PlatformID.MacOSX)
            {
                rootDirectoryPath = Directory.GetDirectoryRoot("/");
                
            }

            return rootDirectoryPath;
        }

        private string GetAbbreviatedAttributes(FileAttributes attributes)
        {
            string abbreviatedAttributes = string.Empty;

            if ((attributes & FileAttributes.ReadOnly) != 0)
                abbreviatedAttributes += "RO ";
            else
                abbreviatedAttributes += "RW ";
            if ((attributes & FileAttributes.Hidden) != 0)
                abbreviatedAttributes += "H ";
            else
                abbreviatedAttributes += "V ";
            if ((attributes & FileAttributes.System) != 0)
                abbreviatedAttributes += "S ";
            else
            {
                abbreviatedAttributes += "  ";
            }
            if ((attributes & FileAttributes.Directory) != 0)
                abbreviatedAttributes += "D ";
            else
            {
                abbreviatedAttributes += "  ";
            }
            if ((attributes & FileAttributes.Archive) != 0)
                abbreviatedAttributes += "A ";
            else
            {
                abbreviatedAttributes += "  ";
            }
            if ((attributes & FileAttributes.Device) != 0)
                abbreviatedAttributes += "DEV ";
            else
            {
                abbreviatedAttributes += "    ";
            }
            if ((attributes & FileAttributes.Normal) != 0)
                abbreviatedAttributes += "N ";
            else
            {
                abbreviatedAttributes += "  ";
            }
            if ((attributes & FileAttributes.Temporary) != 0)
                abbreviatedAttributes += "T ";
            else
            {
                abbreviatedAttributes += "  ";
            }
            if ((attributes & FileAttributes.SparseFile) != 0)
                abbreviatedAttributes += "SF ";
            else
            {
                abbreviatedAttributes += "   ";
            }
            if ((attributes & FileAttributes.ReparsePoint) != 0)
                abbreviatedAttributes += "RP ";
            else
            {
                abbreviatedAttributes += "   ";
            }
            if ((attributes & FileAttributes.Compressed) != 0)
                abbreviatedAttributes += "C ";
            else
            {
                abbreviatedAttributes += "  ";
            }
            if ((attributes & FileAttributes.Offline) != 0)
                abbreviatedAttributes += "O ";
            else
            {
                abbreviatedAttributes += "  ";
            }
            if ((attributes & FileAttributes.NotContentIndexed) != 0)
                abbreviatedAttributes += "NCI ";
            else
            {
                
                abbreviatedAttributes += "  ";
            }
            if ((attributes & FileAttributes.Encrypted) != 0)
                abbreviatedAttributes += "E ";
            else
            { abbreviatedAttributes += "  "; }
            if ((attributes & FileAttributes.IntegrityStream) != 0)
                abbreviatedAttributes += "IS ";
            else
            { abbreviatedAttributes += "  "; }
            if ((attributes & FileAttributes.NoScrubData) != 0)
                abbreviatedAttributes += "NSD ";
            else
            {
                
                abbreviatedAttributes += "   ";
            }

            return abbreviatedAttributes.Trim();
        }
                
        public void ApplyButtonSettingsFromXml(string xmlFilePath, Window window)
        {
            try
            {
                // Clear The Button Entries out first

                TheButtons.Clear();

                // Load XML file
                XDocument xmlDoc = XDocument.Load(xmlFilePath);

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
                                             Args = (string)btn.Element("Args")
                                         };

                // Apply settings to each button
                foreach (var buttonSettings in buttonSettingsList)
                {
                    // Find the grid where the buttons exist
                    var grid = (Grid)window.FindControl<Control>("ButtonGrid");
                    
                    // Find the button by its name
                    var button = (Button)grid.FindControl<Control>(buttonSettings.Name);

                    // Check if the button control exists
                    if (button != null)
                    {
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

                            // Add the button entry to the list of buttons
                            TheButtons.Add(buttonEntry);
                            
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine("Unexpected error: " + ex.Message);
            }
        }
    }

    public class ButtonEntry
    {
        public string Bname { get; set; }
        public string Bcontent { get; set; }
        public string Bargs { get; set; }

        public ButtonEntry(string name, string content, string bargs)
        {
            Bname = name;
            Bcontent = content;
            Bargs = bargs;
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

    }

    
}