using Avalonia.Controls;
using Avalonia.Interactivity;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
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


            PopulateFilePanel(LPgrid,LPpath.Text);
            PopulateFilePanel(RPgrid, RPpath.Text);
        }

        private void LPBackButton_Click(object? sender, RoutedEventArgs e)
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
            
            PopulateFilePanel(LPgrid, LPpath.Text);

        }

        private void RPBackButton_Click(object? sender, RoutedEventArgs e)
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

            PopulateFilePanel(RPgrid, RPpath.Text);
        }

        private void RPgrid_GridItemDoubleClick(object? sender, GridHoverItem e)
        {
            var it = e.ItemUnderMouse as AFileEntry;
            if (it.IsDirectory)
            {
                RPpath.Text = (RPpath.Text + "\\" + it.Name).Replace(@"\\",@"\");
                PopulateFilePanel(RPgrid, RPpath.Text);
            }
        }

        private void LPgrid_GridItemDoubleClick(object? sender, GridHoverItem e)
        {
            var it = e.ItemUnderMouse as AFileEntry;
            if (it.IsDirectory)
            {
                LPpath.Text = (LPpath.Text + "\\" + it.Name).Replace(@"\\",@"\");
                PopulateFilePanel(LPgrid, LPpath.Text);
            }

        }

        private void MainWindowGridContainer_SizeChanged(object? sender, SizeChangedEventArgs e)
        {

            double nwidth = e.NewSize.Width * .45;

            double nheight = ((e.NewSize.Height) - 30) * .7;

            LPgrid.SetGridSize((int)nwidth - 8, (int)nheight - 16);
            RPgrid.SetGridSize((int)nwidth - 8, (int)nheight - 16 );
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

                if (di.Attributes.HasFlag(FileAttributes.System))
                {
                    continue;
                }

                try
                {
                    var ds = di.GetDirectories().GetUpperBound(0);
                    var fs = di.GetFiles().GetUpperBound(0);

                    FileList.Add(new AFileEntry(di.Name, 0, true, ds, fs));
                }
                catch (UnauthorizedAccessException)
                {
                    continue;
                }
                //var ds = di.GetDirectories().GetUpperBound(0);
                //var fs = di.GetFiles().GetUpperBound(0);

                //FileList.Add(new AFileEntry(di.Name, 0, true,ds,fs));
            }

            var files = System.IO.Directory.EnumerateFiles(PATHNAME);

            foreach (string file in files)
            {
                FileInfo fi = new FileInfo(file);
                FileList.Add(new AFileEntry(fi.Name, (int)fi.Length, false));
            }

            ThePanel.Items = FileList.OfType<object>().ToList(); 

            ThePanel.SuspendRendering = false;
        }
    }

    public class AFileEntry
    {
        public string Name { get; set; }
        public string FileSize { get; set; }
        public bool IsDirectory { get; set; } 
        public string Directrories { get; set; }   
        public string Files { get; set; }  

        public AFileEntry(string name, int filesize, bool isdirectory)
        {
            Name = name;
            
            if (filesize > 0)
            {
                FileSize = filesize.ToString();
            }
            else
            {
                FileSize = "";
            }   
            
            //FileSize = filesize;
            IsDirectory = isdirectory;
            
            Directrories = "";
            Files = "";
        }

        public AFileEntry(string name, int filesize, bool isdirectory, int directories, int files)
        {
            Name = name;
            if (filesize > 0)
            {
                FileSize = filesize.ToString();
            }
            else
            {
                FileSize = "";
            }

            //FileSize = filesize;
            IsDirectory = isdirectory;
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
    }
}