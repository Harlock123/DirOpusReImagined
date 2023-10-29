using System.IO;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Media;

namespace DirOpusReImagined;

public partial class RenameFileInterface : Window
{
    
    bool FrmCanceled;
    private TaiDataGrid theGrid;
    private string thePath = "";
    
    private TaiDataGrid theOtherGrid;
    private string theOtherPath = "";

    public string NewName
    {
        get { return this.BasenameTextBox.Text; }
    }

    public string newprefix
    {
        get {return this.PrefixTextBox.Text; }
    }

    public string newsuffix
    {
        get {return this.SuffixTextBox.Text; }
    }

    public bool Canceled { get { return FrmCanceled; } }
    
    public RenameFileInterface()
    {
        InitializeComponent();
        OKButton.Click += OKButton_Click;
        CANCELButton.Click += CANCELButton_Click;
        
    }
    
    public RenameFileInterface(TaiDataGrid Thegrid, string ThePath,TaiDataGrid TheOtherGrid, string TheOtherPath)
    {
        InitializeComponent();
        OKButton.Click += OKButton_Click;
        CANCELButton.Click += CANCELButton_Click;
        
        this.theGrid = Thegrid;
        this.thePath = FileUtility.MakePathENVSafe(ThePath);
        
        this.theOtherGrid = TheOtherGrid;
        this.theOtherPath = FileUtility.MakePathENVSafe(TheOtherPath);
        
    }
    

    private void OKButton_Click(object? sender, RoutedEventArgs e)
    {
        FrmCanceled = false;
        // Here we want to Iterate through the selected items in theGrid and rename them
        // We want to use thePath as the path to the files
        // We want to use theGrid.SelectedItems to get the selected items
        
        // first lets make sure we have a grid to work with 

        if (theGrid != null)
        {
            // yup we got one
            // now lets make sure we have some selected items
            if (theGrid.SelectedItems.Count > 0)
            {
                
                int i = 0;
                
                foreach (AFileEntry af in theGrid.SelectedItems)
                {
                    i += 1;
                    if (!af.Typ)
                    {
                        // we got a file apparently
                        // lets get the full path to it
                        //i += 1;
                        string oldpath = Path.Combine(thePath, af.Name);
                        
                        string oldfilename = FileUtility.FileNameMinusExtension(oldpath);
                        string oldfileext = FileUtility.FilenameExtension(oldpath); 
                        
                        // now lets get the new name
                        string newname = "";
                        string pfx = "" + this.PrefixTextBox.Text;
                        string sfx = "" + this.SuffixTextBox.Text;
                        string bsn = "" + this.BasenameTextBox.Text;

                        string ord = i.ToString();
                        if (chkPad.IsChecked == true)
                        {
                            ord = ord.PadLeft(4, '0');
                        }
                        
                        pfx = pfx.Replace("%ORD%", ord);
                        pfx = pfx.Replace("%NAME%", oldfilename);
                        sfx = sfx.Replace("%ORD%", ord);
                        sfx = sfx.Replace("%NAME%", oldfilename);
                        bsn = bsn.Replace("%ORD%", ord);
                        bsn = bsn.Replace("%NAME%", oldfilename);
                        
                        if (sfx == "")
                        {
                            // Dont add the extension if its already on the resulting name
                            if (!(pfx + bsn).EndsWith(oldfileext))
                                sfx = oldfileext;
                        }
                        
                        string newpath = Path.Combine(thePath, pfx + bsn + sfx);
                        
                        FileUtility.RenameFile(oldpath, newpath);
                    
                    }
                    else
                    {
                        // we are moving a directory
                        string oldpath = Path.Combine(thePath, af.Name);
                        
                        string oldfilename = FileUtility.FileNameMinusExtension(oldpath);
                        string oldfileext = FileUtility.FilenameExtension(oldpath); 
                        
                        // now lets get the new name
                        string newname = "";
                        string pfx = "" + this.PrefixTextBox.Text;
                        string sfx = "" + this.SuffixTextBox.Text;
                        string bsn = "" + this.BasenameTextBox.Text;

                        string ord = i.ToString();
                        if (chkPad.IsChecked == true)
                        {
                            ord = ord.PadLeft(4, '0');
                        }
                        
                        pfx = pfx.Replace("%ORD%", ord);
                        pfx = pfx.Replace("%NAME%", oldfilename);
                        sfx = sfx.Replace("%ORD%", ord);
                        sfx = sfx.Replace("%NAME%", oldfilename);
                        bsn = bsn.Replace("%ORD%", ord);
                        bsn = bsn.Replace("%NAME%", oldfilename);
                        
                        if (sfx == "")
                        {
                            // Dont add the extension if its already on the resulting name
                            //if (!(pfx + bsn).EndsWith(oldfileext))
                            //    sfx = oldfileext;
                        }
                        
                        string newpath = Path.Combine(thePath, pfx + bsn);
                        
                        FileUtility.RenameDirectory(oldpath, newpath);

                    }
                }
                
                FileUtility.PopulateFilePanel(theGrid,thePath);
                if (thePath == theOtherPath)
                    FileUtility.PopulateFilePanel(theOtherGrid,theOtherPath);
                
                
            }
        }
        
        Close();
        
    }

    private void CANCELButton_Click(object? sender, RoutedEventArgs e)
    {
        FrmCanceled = true;
        Close();
        
    }
}