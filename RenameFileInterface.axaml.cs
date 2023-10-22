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
    
    public RenameFileInterface(TaiDataGrid Thegrid, string ThePath)
    {
        InitializeComponent();
        OKButton.Click += OKButton_Click;
        CANCELButton.Click += CANCELButton_Click;
        
        this.theGrid = Thegrid;
        this.thePath = FileUtility.MakePathENVSafe(ThePath);
        
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
                    if (!af.Typ)
                    {
                        // we got a file apparently
                        // lets get the full path to it
                        i += 1;
                        string oldpath = Path.Combine(thePath, af.Name);
                        
                        // now lets get the new name
                        string newname = "";
                        string pfx = "" + this.PrefixTextBox.Text;
                        string sfx = "" + this.SuffixTextBox.Text;
                        string bsn = "" + this.BasenameTextBox.Text;
                        
                        pfx = pfx.Replace("%ORD%", i.ToString());
                        pfx = pfx.Replace("%NAME%", af.Name);
                        sfx = sfx.Replace("%ORD%", i.ToString());
                        sfx = sfx.Replace("%NAME%", af.Name);
                        bsn = bsn.Replace("%ORD%", i.ToString());
                        bsn = bsn.Replace("%NAME%", af.Name);
                        
                        string newpath = Path.Combine(thePath, pfx + bsn + sfx);
                        
                        FileUtility.RenameFile(oldpath, newpath);
                        
                        

                    }
                }
                
                FileUtility.PopulateFilePanel(theGrid,thePath);
                
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