using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Media.Imaging;
//using Microsoft.CodeAnalysis.CSharp.Syntax;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Diagnostics;
using System.IO;
using System.Linq;

namespace DirOpusReImagined
{
    public partial class MainWindow : Window
    {

        private Avalonia.Rect LRB = new Avalonia.Rect();
        private Avalonia.Rect RRB = new Avalonia.Rect();
        private Avalonia.Size OrigSize = new Avalonia.Size();

        public MainWindow()
        {
            InitializeComponent();

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
        }

        private void MoveRightButton_Click(object? sender, RoutedEventArgs e)
        {
            throw new NotImplementedException();
        }

        private void MoveLeftButton_Click(object? sender, RoutedEventArgs e)
        {
            throw new NotImplementedException();
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
                        
                        FileUtility.CopyDirectoryToFolder(FullPath, tpath);

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

                        FileUtility.CopyDirectoryToFolder(FullPath, tpath);

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

            var Directories = System.IO.Directory.EnumerateDirectories(PATHNAME);

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

                
                    var ds = di.GetDirectories().GetUpperBound(0);
                    var fs = di.GetFiles().GetUpperBound(0);

                    FileList.Add(new AFileEntry(di.Name, 0, true, ds, fs));
                }
                catch (UnauthorizedAccessException)
                {
                    
                    try
                    {                    
                        FileList.Add(new AFileEntry(di.Name, 0, true, 0, 0));
                    }
                    catch
                    {

                    }
                }
                //var ds = di.GetDirectories().GetUpperBound(0);
                //var fs = di.GetFiles().GetUpperBound(0);

                //FileList.Add(new AFileEntry(di.Name, 0, true,ds,fs));
            }

            var files = System.IO.Directory.EnumerateFiles(PATHNAME);

            foreach (string file in files)
            {
                try
                {
                    FileInfo fi = new FileInfo(file);
                    FileList.Add(new AFileEntry(fi.Name, (int)fi.Length, false));
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

    }

    public class AFileEntry
    {
        public bool Typ { get; set; }
        public string Name { get; set; }
        public string FileSize { get; set; }
        public string Directrories { get; set; }   
        public string Files { get; set; }  

        public AFileEntry(string name, int filesize, bool isdirectory)
        {
            Name = name;
            
            if (filesize > 0)
            {
                FileSize = ConvertNumberToReadableString( filesize );
            }
            else
            {
                FileSize = "";
            }   
            
            //FileSize = filesize;
            Typ = isdirectory;
            
            Directrories = "";
            Files = "";
        }

        public AFileEntry(string name, int filesize, bool isdirectory, int directories, int files)
        {
            Name = name;
            if (filesize > 0)
            {
                FileSize = ConvertNumberToReadableString(filesize);
            }
            else
            {
                FileSize = "";
            }

            //FileSize = filesize;
            Typ = isdirectory;
            if(directories > 0)
            {
                  Directrories = directories.ToString();
            }
            else
            {
                Directrories = "";
            }
            
            //Directrories = directories;
            
            if (files > 0)
            {
                  Files = files.ToString();
            }
            else
            {
                Files = "";         
            }
            
            //Files = files;
        }

        public string ConvertNumberToReadableString(long number)
        {
            const int scale = 1024;

            string[] orders = new string[] { "b", "Kb", "Mb", "Gb" };

            if (number < scale)
                return number.ToString() + "b";

            int order = 0;
            while (number >= scale)
            {
                order++;
                number /= scale;
            }

            double result = number;
            return string.Format("{0:0.##}{1}", result, orders[order]);
        }
    }
}