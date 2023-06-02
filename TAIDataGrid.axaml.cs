using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Controls.Shapes;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Metadata;
using Avalonia.Threading;
using System;

using System.Collections.Generic;
using System.Drawing.Text;
using System.Globalization;
using System.IO;
using System.Linq.Expressions;
using System.Reflection;
using System.Threading.Tasks;

namespace DirOpusReImagined
{
    public partial class TAIDataGrid : UserControl
    {
        #region Private Variables

        private string gridTitle = "The Grid Control for Avalonia";
        private int gridTitleFontSize = 20;
        private int gridTitleHeight = 10;
        private int theLineLabelSize = 10;
        private int theDataLabelSize = 10;
        private IBrush gridBackground = Avalonia.Media.Brushes.Cornsilk;
        private IBrush gridCellOutline = Avalonia.Media.Brushes.Black;
        private IBrush gridCellContentBrush = Avalonia.Media.Brushes.Black;
        private IBrush gridCellBrush = Avalonia.Media.Brushes.Wheat;

        private IBrush gridCellHighlightBrush = Avalonia.Media.Brushes.LightBlue;
        private IBrush gridSelectedItemBrush = Avalonia.Media.Brushes.AliceBlue;
        private IBrush gridCellHighlightContentBrush = Avalonia.Media.Brushes.Black;

        private IBrush gridTitleBrush = Avalonia.Media.Brushes.White;
        private IBrush gridHeaderBrush = Avalonia.Media.Brushes.DarkBlue;
        private IBrush gridTitleBackground = Avalonia.Media.Brushes.Blue;
        private IBrush gridHeaderBackground = Avalonia.Media.Brushes.Cyan;
        private Typeface gridTitleTypeface = new Typeface("Arial", FontStyle.Normal, FontWeight.Bold);
        private Typeface gridHeaderTypeface = new Typeface("Arial", FontStyle.Normal, FontWeight.Normal);
        private Typeface gridTypeface = new Typeface("Arial", FontStyle.Normal, FontWeight.Normal);
        private int gridheaderFontSize = 14;
        private int gridFontSize = 12;

        private bool showCrossHairs = true;
        private IBrush crossHairBrush = Avalonia.Media.Brushes.Red;

        private int gridWidth = 800;
        private int gridHeight = 300;

        private int GridXShift = 0;
        private int GridYShift = 0;

        private Point LastPosition = new Point();

        private int CurMouseX = 0;
        private int CurMouseY = 0;
        private int curMouseRow = 0;
        private int curMouseCol = 0;
        private bool MouseInControl = false;
        private int scrollMultiplier = 3;
        private bool suspendRendering = false;
        private bool autosizeCellsToContents = true;

        private bool populateWithTestData = false;

        private int gridHeaderAndTitleHeight = 0;


        private List<object> items = new List<object>();
        private List<object> selecteditems = new List<object>();

        private int[] colWidths;
        private int[] rowHeights;

        private int gridRows = 0;
        private int gridCols = 0;

        private object ItemUnderMouse = null;

        private bool InDesignMode = false;

        public GridHoverItem TheItemUnderTheMouse = new GridHoverItem();

        private DispatcherTimer _doubleClickTimer;
        private int _clickCounter;

        private Bitmap CheckMark = null;
        private Bitmap RedX = null;
        private Bitmap Folder = null;
        private Bitmap File = null;

        private List<int> justifyColumns = new List<int>();

        #endregion

        #region Constructor
        public TAIDataGrid()
        {
            InitializeComponent();

            InDesignMode = Design.IsDesignMode;

            this.HorizontalAlignment = Avalonia.Layout.HorizontalAlignment.Stretch;

            TheHorizontalScrollBar.Scroll += TheHorizontalScrollBar_scroll;
            TheVerticleScrollBar.Scroll += TheVerticleScrollBar_Scroll;

            this.PointerWheelChanged += OnPointerWheelChanged;

            this.PointerMoved += OnPointerMoved;
            this.PointerEntered += OnPointerEntered;
            this.PointerExited += OnPointerExited;
            this.PointerPressed += OnPointerPressed;
            this.PointerReleased += OnPointerReleased;

            _doubleClickTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(500) };
            _doubleClickTimer.Tick += DoubleClickTimer_Tick;

            this.CheckMark = LoadImage(ImageStrings.CheckMark);
            this.RedX = LoadImage(ImageStrings.RedX);
            this.Folder = LoadImage(ImageStrings.Folder);
            this.File = LoadImage(ImageStrings.Afile);

            this.items.Clear();


            if (populateWithTestData || this.InDesignMode)
                PopulateTESTData();

            this.ReRender();
        }

        #endregion

        #region Properties

        // this is the title of the grid
        public string GridTitle
        {
            get { return gridTitle; }
            set
            {
                gridTitle = value;
                this.ReRender();
            }
        }

        // this is the font size for the grid title
        public int GridTitleFontSize
        {
            get { return gridTitleFontSize; }
            set
            {
                gridTitleFontSize = value;
                this.ReRender();
            }
        }

        // this is the font size for the grid headers (column names)
        public int GridHeaderFontSize
        {
            get { return gridheaderFontSize; }
            set
            {
                gridheaderFontSize = value;
                this.ReRender();
            }
        }

        // this is the font size for the grid contents
        public int GridFontSize
        {
            get { return gridFontSize; }
            set
            {
                gridFontSize = value;
                this.ReRender();
            }
        }

        // this is the height of the grid title in pixels
        public int GridTitleHeight
        {
            get { return gridTitleHeight; }
            set
            {
                gridTitleHeight = value;
                this.ReRender();
            }
        }

        // this is the brush used to render the Grid Background
        public IBrush GridBackground
        {
            get { return gridBackground; }
            set
            {
                gridBackground = value;
                this.ReRender();
            }
        }

        // this is the brush that will be used to render the grid title font
        public IBrush GridTitleBrush
        {
            get { return gridTitleBrush; }
            set
            {
                gridTitleBrush = value;
                this.ReRender();
            }
        }

        // this is the brush that will be used to render the grids title background
        public IBrush GridTitleBackground
        {
            get { return gridTitleBackground; }
            set
            {
                gridTitleBackground = value;
                this.ReRender();
            }
        }

        // this is the brush that will be used to render the grids headers (column names)
        public IBrush GridHeaderBrush
        {
            get { return gridHeaderBrush; }
            set
            {
                gridHeaderBrush = value;
                this.ReRender();
            }
        }

        // this is the brush that will be used to fill the grid header
        public IBrush GridHeaderBackground
        {
            get { return gridHeaderBackground; }
            set
            {
                gridHeaderBackground = value;
                this.ReRender();
            }
        }

        // this is the brush that will be used to outline the grid cells
        public IBrush GridCellOutline
        {
            get { return gridCellOutline; }
            set
            {
                gridCellOutline = value;
                this.ReRender();
            }
        }

        // this is the brush that will be used to fill the grid cells
        public IBrush GridCellBrush
        {
            get { return gridCellBrush; }
            set
            {
                gridCellBrush = value;
                this.ReRender();
            }
        }

        // Hovering over a cell will highlight its background with this brush
        public IBrush GridCellHighlightBrush
        {
            get { return gridCellHighlightBrush; }
            set
            {
                GridCellHighlightBrush = value;
                this.ReRender();
            }
        }

        // Hovering over a cell will highlight its content with this brush
        public IBrush GridCellHighlightContentBrush
        {
            get { return gridCellHighlightContentBrush; }
            set
            {
                gridCellHighlightContentBrush = value;
                this.ReRender();
            }
        }

        // SelectedItems in the grid will be highlighted with this brush
        public IBrush GridSelectedItemBrush
        {
            get { return gridSelectedItemBrush; }
            set
            {
                gridSelectedItemBrush = value;
                this.ReRender();
            }
        }

        // this is the font definition for the grid title
        public Typeface GridTitleTypeface
        {
            get { return gridTitleTypeface; }
            set
            {
                gridTitleTypeface = value;
                this.ReRender();
            }
        }

        // this is the font definition for the grid contents
        public Typeface GridTypeface
        {
            get { return gridTypeface; }
            set
            {
                gridTypeface = value;
                this.ReRender();
            }
        }

        // this is the font definition for the grid header
        public Typeface GridHeaderTypeface
        {
            get { return gridHeaderTypeface; }
            set
            {
                gridHeaderTypeface = value;
                this.ReRender();
            }
        }

        // This is the data that will be displayed in the grid
        public List<object> Items
        {
            get { return items; }
            set
            {
                SuspendRendering = true;

                items = value;
                selecteditems.Clear();
                this.TheItemUnderTheMouse = new GridHoverItem();
                this.GridXShift = 0;
                this.GridYShift = 0;

                SuspendRendering = false;

                this.ReRender();
            }
        }

        // This is the data that will be displayed in the grid as selected rows
        public List<object> SelectedItems 
        {   
            get { return selecteditems; }
            set
            {
                selecteditems = value;
                this.ReRender();
            }
        } 

        // Flag to enable or disable rendering the grid
        public bool SuspendRendering
        {
            get { return suspendRendering; }
            set
            {
                suspendRendering = value;
                this.ReRender();
            }
        }

        // Flag to enable or disable autosizing the cells to the contents
        public bool AutosizeCellsToContents
        {
            get { return autosizeCellsToContents; }
            set
            {
                autosizeCellsToContents = value;
                this.ReRender();
            }
        }

        // The width of the grid in Pixels
        public int GridWidth
        {
            get { return gridWidth; }
            set
            {
                gridWidth = value;
                this.ReRender();
            }
        }

        // The height of the grid in Pixels
        public int GridHeight
        {
            get { return gridHeight; }
            set
            {
                gridHeight = value;
                this.ReRender();
            }
        }

        // Boolean to Show or Hide Crosshairs on hovering over a cell
        public bool ShowCrossHairs
        {
            get { return showCrossHairs; }
            set
            {
                showCrossHairs = value;
                this.ReRender();
            }
        }

        // Boolean to Show or Hide A set of self contained test objects in the grid
        public bool PopulateWithTestData
        {
            get { return populateWithTestData; }
            set
            {
                populateWithTestData = value;

                if (populateWithTestData)
                {
                    PopulateTESTData();

                }
                else
                {
                    this.Items.Clear();
                }

                this.ReRender();
            }
        }

        // An accelerator for Mouse Wheel scrolling Operations
        public int ScrollMultiplier
        {
            get { return scrollMultiplier; }
            set
            {
                scrollMultiplier = value;
                this.ReRender();
            }
        }

        // The current row under the mouse
        public int CurMouseRow
        {
            get { return curMouseRow; }
            set
            {
                curMouseRow = value;
                //this.ReRender();
            }
        }

        // The current column under the mouse   
        public int CurMouseCol
        {
            get { return curMouseCol; }
            set
            {
                curMouseCol = value;
                //this.ReRender();
            }
        }

        public List<int> JustifyColumns
        {
            get { return justifyColumns; }
            set
            {
                justifyColumns = value;
                this.ReRender();
            }
        }

        #endregion

        #region Public Methods

        public void ReRender()
        {
            try
            {

                if (TheCanvas != null && !this.suspendRendering)
                {
                    // the canvas exists so lets render the grid

                    // clear the canvas
                    TheCanvas.Children.Clear();

                    this.gridHeaderAndTitleHeight = 0;

                    #region Render Title

                    if (this.GridTitle != String.Empty)
                    {
                        var formattedText =
                            new FormattedText(this.GridTitle, CultureInfo.CurrentCulture,
                            FlowDirection.LeftToRight, this.GridTitleTypeface, this.GridTitleFontSize,
                            this.GridTitleBrush);

                        Rectangle rr = new Rectangle();
                        rr.Width = TheCanvas.Width;
                        //rr.Height = formattedText.Height;

                        if (this.GridTitleHeight < formattedText.Height)
                        {
                            rr.Height = formattedText.Height;
                            this.gridTitleHeight = (int)formattedText.Height;
                            this.gridHeaderAndTitleHeight = this.gridTitleHeight;
                        }
                        else
                        {
                            rr.Height = this.GridTitleHeight;

                            this.gridTitleHeight = (int)formattedText.Height;

                            this.gridHeaderAndTitleHeight = this.gridTitleHeight;
                        }

                        //this.gridHeaderAndTitleHeight = this.gridTitleHeight;

                        rr.Fill = this.GridTitleBackground;
                        Canvas.SetLeft(rr, 0);
                        Canvas.SetTop(rr, 0);
                        TheCanvas.Children.Add(rr);

                        TextBlock ttb = new TextBlock();
                        ttb.Text = this.GridTitle;
                        ttb.FontSize = this.GridTitleFontSize;
                        ttb.Foreground = this.GridTitleBrush;
                        ttb.FontFamily = this.GridTitleTypeface.FontFamily;
                        ttb.FontWeight = this.GridTitleTypeface.Weight;
                        ttb.FontStyle = this.GridTitleTypeface.Style;
                        Canvas.SetLeft(ttb, 0);
                        Canvas.SetTop(ttb, 0);
                        TheCanvas.Children.Add(ttb);

                    }

                    #endregion

                    #region FigureOut Cell Sizes

                    this.rowHeights = new int[this.items.Count];
                    this.gridRows = this.items.Count;
                    int row = 0;

                    if (this.items.Count > 0)
                    {
                        List<PropertyInfoModel> schema = GetObjectSchema(this.items[0]);

                        this.colWidths = new int[schema.Count];
                        this.gridCols = schema.Count;
                        int idx = 0;
                        //int tempval = 0;

                        foreach (PropertyInfoModel property in schema)
                        {
                            var formattedText =
                                new FormattedText(property.Name, CultureInfo.CurrentCulture,
                                                           FlowDirection.LeftToRight,
                                                           this.GridHeaderTypeface,
                                                           this.GridHeaderFontSize,
                                                           this.GridTitleBrush);

                            colWidths[idx] = (int)formattedText.Width + 10;

                            idx++;
                        }



                        // we now have column widths, lets see if we need to autosize the cells

                        if (this.AutosizeCellsToContents)
                        {
                            // we need to autosize the cells

                            var rowidx = 0;

                            foreach (object item in this.items)
                            {
                                idx = 0;

                                int tempval = 0;


                                foreach (PropertyInfo property in item.GetType().GetProperties())
                                {
                                    //property.GetValue(item)

                                    var formattedText =
                                        new FormattedText(property.GetValue(item)?.ToString() + "",
                                                CultureInfo.CurrentCulture, FlowDirection.LeftToRight,
                                                this.gridTypeface, this.gridFontSize,
                                                this.gridCellBrush);
                                    if (colWidths[idx] < formattedText.Width + 10)
                                    {
                                        colWidths[idx] = (int)formattedText.Width + 10;
                                    }

                                    if ((int)formattedText.Height + 2 > tempval)
                                    {
                                        tempval = (int)formattedText.Height + 2;
                                    }

                                    idx++;
                                }

                                this.rowHeights[rowidx] = tempval;
                                tempval = 0;
                                rowidx++;

                            }
                        }
                    }


                    #endregion

                    #region Render Grid Header

                    if (this.items.Count > 0)
                    {
                        List<PropertyInfoModel> schema = GetObjectSchema(this.items[0]);
                        int left = 0;
                        int top = this.gridTitleHeight;
                        int tempheight = 0;


                        int idx = 0;

                        foreach (PropertyInfoModel property in schema)
                        {
                            var formattedText =
                                new FormattedText(property.Name, CultureInfo.CurrentCulture,
                                                           FlowDirection.LeftToRight,
                                                           this.GridHeaderTypeface,
                                                           this.GridHeaderFontSize,
                                                           this.GridTitleBrush);

                            // see if the height of the text is greater than the current height
                            // gathered for the header physical height on the grid
                            if (tempheight == 0)
                            {
                                tempheight = (int)formattedText.Height;
                            }
                            else
                            {
                                if (tempheight < formattedText.Height)
                                {
                                    tempheight = (int)formattedText.Height;
                                }
                            }

                            Rectangle rr1 = new Rectangle();
                            rr1.Width = this.colWidths[idx];

                            //this.colWidths[idx] = (int)rr1.Width;

                            rr1.Height = (int)formattedText.Height + 2;
                            rr1.Fill = this.GridHeaderBackground;
                            //rr1.StrokeThickness = 1;
                            Canvas.SetLeft(rr1, left - this.GridXShift);
                            Canvas.SetTop(rr1, top);

                            TheCanvas.Children.Add(rr1);

                            Rectangle rr = new Rectangle();
                            rr.Width = this.colWidths[idx];//(int)formattedText.Width + 10;
                            rr.Height = (int)formattedText.Height + 2;
                            rr.Stroke = this.gridCellOutline;
                            rr.StrokeThickness = 1;
                            Canvas.SetLeft(rr, left - this.GridXShift);
                            Canvas.SetTop(rr, top);

                            TheCanvas.Children.Add(rr);

                            TextBlock ttb = new TextBlock();
                            ttb.Text = property.Name;
                            ttb.FontSize = this.GridHeaderFontSize;
                            ttb.Foreground = this.GridHeaderBrush;
                            ttb.FontFamily = this.GridHeaderTypeface.FontFamily;
                            ttb.FontWeight = this.GridHeaderTypeface.Weight;
                            ttb.FontStyle = this.GridHeaderTypeface.Style;
                            Canvas.SetLeft(ttb, left + 2 - this.GridXShift);
                            Canvas.SetTop(ttb, top + 1);
                            TheCanvas.Children.Add(ttb);
                            left += this.colWidths[idx];

                            idx++;
                        }

                        this.gridHeaderAndTitleHeight += tempheight;
                    }

                    #endregion

                    // coming out of here we should have our grids Title and Header Row rendered
                    // we should also know at what Y coordinate to start rendering the data rows
                    // as it will be the gridHeaderAndTitleHeight

                    #region Render Grid Data Rows

                    if (this.items.Count > 0)
                    {
                        int left = 0;
                        int top = this.gridHeaderAndTitleHeight;

                        int idx = 0;
                        int rowidx = 0;

                        foreach (object item in this.items)
                        {

                            IBrush tbb = this.gridCellBrush;
                            IBrush tcb = this.gridCellContentBrush;

                            if (this.selecteditems.Contains(item))
                            {
                                tbb = this.gridSelectedItemBrush;
                                tcb = this.gridCellHighlightContentBrush;
                            }

                            if (rowidx == this.TheItemUnderTheMouse.rowID && this.MouseInControl)
                            {
                                tbb = this.gridCellHighlightBrush;
                                tcb = this.gridCellHighlightContentBrush;
                            }

                            idx = 0;
                            left = 0;

                            if (rowidx < this.GridYShift)
                            {
                                rowidx++;
                            }
                            else
                            {
                                string lastCellItemText = "";
                                foreach (PropertyInfo property in item.GetType().GetProperties())
                                {
                                    //property.GetValue(item)

                                    var formattedText =
                                        new FormattedText(property.GetValue(item)?.ToString() + "",
                                                CultureInfo.CurrentCulture, FlowDirection.LeftToRight,
                                                this.gridTypeface, this.gridFontSize,
                                                this.gridCellBrush);

                                    Rectangle rr = new Rectangle();
                                    rr.Width = this.colWidths[idx];
                                    rr.Height = this.rowHeights[rowidx];

                                    rr.Fill = tbb;

                                    // Do our Grid Clipping Here
                                    if ((left - this.GridXShift + rr.Width > 0) &&
                                        (top <= TheCanvas.Height) &&
                                        (left - this.GridXShift <= TheCanvas.Width))
                                    {

                                        Canvas.SetLeft(rr, left - this.GridXShift);
                                        Canvas.SetTop(rr, top);

                                        TheCanvas.Children.Add(rr);


                                        Rectangle rr1 = new Rectangle();
                                        rr1.Width = this.colWidths[idx];
                                        rr1.Height = this.rowHeights[rowidx];
                                        rr1.Stroke = this.gridCellOutline;
                                        rr1.StrokeThickness = 1;
                                        Canvas.SetLeft(rr1, left - this.GridXShift);
                                        Canvas.SetTop(rr1, top);

                                        TheCanvas.Children.Add(rr1);

                                        string TheText = (property.GetValue(item)?.ToString() + "").ToUpper().Trim();

                                        if (TheText == "TRUE" ||
                                            TheText == "FALSE" ||
                                            TheText == "YES" ||
                                            TheText == "NO")
                                        {
                                            // Create an image control.
                                            var image = new Image();

                                            if (TheText == "TRUE" || TheText == "YES")
                                            {
                                                image.Source = this.Folder;
                                                image.Width = this.rowHeights[rowidx] - 2;
                                                image.Height = this.rowHeights[rowidx] - 2;
                                            }
                                            else
                                            {
                                                image.Source = this.File;
                                                image.Width = this.rowHeights[rowidx] - 2;
                                                image.Height = this.rowHeights[rowidx] - 2;
                                            }

                                            // Set the position of the image on the canvas.

                                            var leftoffset = (this.colWidths[idx] - this.rowHeights[rowidx]) / 2;
                                            Canvas.SetLeft(image, left + leftoffset - this.GridXShift);
                                            Canvas.SetTop(image, top + 1);

                                            // Add the image to the canvas.
                                            TheCanvas.Children.Add(image);
                                        }
                                        else
                                        {

                                            TextBlock ttb = new TextBlock();
                                            ttb.Text = property.GetValue(item)?.ToString() + "";
                                            ttb.FontSize = this.gridFontSize;
                                            ttb.Foreground = tcb;
                                            ttb.FontFamily = this.gridTypeface.FontFamily;
                                            ttb.FontWeight = this.gridTypeface.Weight;
                                            ttb.FontStyle = this.gridTypeface.Style;

                                            // Here we look for idx in JustifyColumns
                                            // if its there we might pad the strings placement
                                            // to the right to give it a right justification

                                            int leftoffset = 2;

                                            if (JustifyColumns.Contains(idx))
                                            {
                                                var theTextFormatted =
                                                new FormattedText(property.GetValue(item)?.ToString() + "",
                                                CultureInfo.CurrentCulture, FlowDirection.LeftToRight,
                                                this.gridTypeface, this.gridFontSize,
                                                this.gridCellBrush);

                                                leftoffset = this.colWidths[idx] - (int)theTextFormatted.Width - 2;
                                            }


                                            Canvas.SetLeft(ttb, left + leftoffset - this.GridXShift);
                                            Canvas.SetTop(ttb, top + 1);
                                            TheCanvas.Children.Add(ttb);
                                            lastCellItemText = ttb.Text;
                                        }
                                    }

                                    left += this.colWidths[idx];

                                    idx++;
                                }

                                top += this.rowHeights[rowidx];
                                rowidx++;
                            }

                        }
                    }

                    #endregion

                    // Setup the Verticle Scrollbar
                    // Here we will only scroll whole rows by the setting
                    // the max value of the scrollbar to be the number of rows total in the grid

                    this.TheVerticleScrollBar.Minimum = 0;
                    this.TheVerticleScrollBar.Maximum = this.items.Count;

                    //if (this.showCrossHairs)
                    //{
                    //    RenderCrossHairs();
                    //}

                    // Figure out the ROW we are on

                    int offsety = 0;

                    for (int i = this.GridYShift; i < this.gridRows; i++)
                    {
                        offsety += this.rowHeights[i];

                        if (this.CurMouseY - this.gridHeaderAndTitleHeight < offsety)
                        {
                            this.TheItemUnderTheMouse.rowID = i;
                            break;
                        }
                    }

                    // Figure out the COLUMN we are on

                    offsety = 0;
                    for (int i = 0; i < this.gridCols; i++)
                    {
                        offsety += this.colWidths[i];


                        if (this.LastPosition.X + this.GridXShift < offsety)
                        {
                            this.TheItemUnderTheMouse.colID = i;
                            if (this.items.Count > 0)
                                this.TheItemUnderTheMouse.ItemUnderMouse = this.items[this.TheItemUnderTheMouse.rowID];
                            break;
                        }

                    }

                    // figure out the value of whats being hovered over
                    this.TheItemUnderTheMouse.cellContent = "";

                    if (this.Items.Count > 0)
                    {

                        object theitem = this.TheItemUnderTheMouse.ItemUnderMouse;//this.items[this.TheItemUnderTheMouse.rowID];
                        int idx = 0;
                        foreach (PropertyInfo property in this.Items[0].GetType().GetProperties())
                        {
                            if (idx == this.TheItemUnderTheMouse.colID)
                            {
                                this.TheItemUnderTheMouse.cellContent = property.GetValue(this.Items[this.TheItemUnderTheMouse.rowID])?.ToString() + "";
                                break;
                            }
                            idx++;
                        }
                    }


                    GridHover?.Invoke(this, this.TheItemUnderTheMouse);
                }
                

            }
            catch (Exception ex)
            {
                //Debug.WriteLine(ex.Message);
            }
        }

        public void TestPopulate()
        {
            PopulateTESTData();
        }

        public void ClearTestPopulate()
        {
            TheCanvas.Children.Clear();
            this.items.Clear();
            this.ReRender();
        }

        public void SetGridSize(int width, int height)
        {
            TheCanvas.Width = width;
            TheCanvas.Height = height;
            this.Width = width + 12;
            this.Height = height + 12;
            this.ReRender();
        }

        public string GetFirstSelectedFolder()
        {
            string result = "";

            foreach(AFileEntry item in SelectedItems)
            {
                //var item = i.ItemUnderMouse as AFileEntry;

                if ( item.Typ)
                {
                    result = item.Name;
                    break;
                }
            }

            return result;
        }

        #endregion

        #region Private methods

        private void PopulateTESTData()
        {
            TheCanvas.Children.Clear();
            this.items.Clear();

            for (int i = 0; i < 20; i++)
            {
                TestStuff tt = new TestStuff();

                tt.Name = "Lonnie Watson";
                tt.Status = "Active";
                tt.AssignedTo = "SlatriBartFast";
                tt.Priority = "High";
                //tt.DueDate = DateTime.Now.AddDays(5).ToShortDateString();
                tt.Description = "This is a test of the emergency broadcast system\n.This is only a test.";


                tt.Id = i;
                tt.DueDate = DateTime.Now.AddDays(i).ToShortDateString();
                tt.AssignedDate = DateTime.Now.AddDays(i).ToShortDateString();
                tt.Name = "Lonnie Watson " + i.ToString();

                tt.CompletedAssignedBy = "Name Of Person " + i.ToString();

                this.items.Add(tt);
            }
        }

        private List<PropertyInfoModel> GetObjectSchema(object obj)
        {
            List<PropertyInfoModel> schema = new List<PropertyInfoModel>();
            Type type = obj.GetType();
            PropertyInfo[] properties = type.GetProperties();

            foreach (PropertyInfo property in properties)
            {
                schema.Add(new PropertyInfoModel { Name = property.Name, Type = property.PropertyType });
            }

            return schema;
        }

        private void RenderCrossHairs()
        {
            if (this.CurMouseY >= TheCanvas.Height || this.CurMouseX >= TheCanvas.Width)
                return;

            Line l1 = new Line();
            l1.Stroke = this.crossHairBrush;
            l1.StrokeThickness = 1;
            l1.StartPoint = new Point(0, this.CurMouseY);
            l1.EndPoint = new Point(TheCanvas.Width, this.CurMouseY);

            TheCanvas.Children.Add(l1);

            Line l2 = new Line();
            l2.Stroke = this.crossHairBrush;
            l2.StrokeThickness = 1;
            l2.StartPoint = new Point(this.CurMouseX, 0);
            l2.EndPoint = new Point(this.CurMouseX, TheCanvas.Height);

            TheCanvas.Children.Add(l2);
        }

        private async Task<Bitmap> LoadImageAsync(string base64)
        {
            byte[] bytes = Convert.FromBase64String(base64);

            using (var memoryStream = new MemoryStream(bytes))
            {
                return await Task.Run(() => Bitmap.DecodeToWidth(memoryStream, 32));
            }
        }

        private  Bitmap LoadImage(string base64)
        {
            byte[] bytes = Convert.FromBase64String(base64);

            using (var memoryStream = new MemoryStream(bytes))
            {
                return Bitmap.DecodeToWidth(memoryStream, 32);
            }
        }

        #endregion

        #region Event Handlers

        private void OnPointerWheelChanged(object sender, PointerWheelEventArgs e)
        {
            // Your logic here

            if (e.KeyModifiers == KeyModifiers.Control)
            {
                // We are scrolling Horizontally

                double d = TheHorizontalScrollBar.Value - (e.Delta.Y * this.scrollMultiplier);
                if (d < 0)
                {
                    d = 0;
                }
                else if (d > TheHorizontalScrollBar.Maximum)
                {
                    d = TheHorizontalScrollBar.Maximum;
                }

                int maxposition = 0;

                for (int i = 0; i < this.colWidths.Length; i++)
                {
                    maxposition += this.colWidths[i];
                }

                double Delta = (maxposition / 100) * d;

                GridXShift = (int)Delta;

                TheHorizontalScrollBar.Value = d;

                this.ReRender();

            }
            else
            {

                if (e.KeyModifiers == KeyModifiers.Alt)
                {
                    // Lets Scroll on the grid Font Size

                    if (e.Delta.Y > 0)
                    {
                        // Increase Font Size
                        this.GridFontSize += 1;
                        this.GridHeaderFontSize += 1;
                        this.GridTitleFontSize += 1;
                    }
                    else
                    {
                        // Decrease Font Size
                        this.GridFontSize += -1;
                        this.GridHeaderFontSize += -1;
                        this.GridTitleFontSize += -1;
                    }

                    ReRender();
                }
                else
                {
                    // We are scrolling Vertically
                    
                    double d = TheVerticleScrollBar.Value - (e.Delta.Y * this.scrollMultiplier);

                    if (d < 0)
                    {
                        d = 0;
                    }
                    else if (d > TheVerticleScrollBar.Maximum)
                    {
                        d = TheVerticleScrollBar.Maximum;
                    }

                    TheVerticleScrollBar.Value = d;

                    if (this.items.Count > 0)
                    {
                        this.GridYShift = (int)d;
                        this.ReRender();
                    }
                }
            
            }

            //this.ReRender();

            e.Handled = true; // Mark the event as handled to prevent it from bubbling up
        }

        private void OnPointerMoved(object sender, PointerEventArgs e)
        {
            // Get the current pointer position relative to the UserControl
            Point position = e.GetPosition(this);

            this.LastPosition = position;

            if (this.InDesignMode)
            {
                this.CurMouseX = (int)position.X;
                this.CurMouseY = (int)position.Y;
            }
            else
            {
                this.CurMouseX = (int)position.X - (int)TheCanvas.Bounds.Left;
                this.CurMouseY = (int)position.Y - (int)TheCanvas.Bounds.Top;
            }
            //this.CurMouseX = (int)position.X;
            //this.CurMouseX = (int)position.X - (int)TheCanvas.Bounds.Left;

            //this.CurMouseY = (int)position.Y - (int)TheCanvas.Bounds.Top;


            if (this.showCrossHairs)
            {
                this.ReRender();
            }

            e.Handled = true; // Mark the event as handled to prevent it from bubbling up

            //// Get the current pointer position relative to the UserControl
            //Point position = e.GetPosition(this);

            ////this.CurMouseX = (int)position.X;
            //this.CurMouseX = (int)position.X - (int)TheCanvas.Bounds.Left;

            //this.CurMouseY = (int)position.Y - (int)TheCanvas.Bounds.Top;

            //for (int i = 0; i < this.TheLines.Count; i++)
            //{

            //    TCCLineMetrics tccm = this.TheLines[i];

            //    if (this.CurMouseX >= tccm.LineX &&
            //        this.CurMouseX <= tccm.LineX + tccm.LineW &&
            //        this.CurMouseY >= tccm.LineY &&
            //        this.CurMouseY <= tccm.LineH)
            //    {
            //        // we are within a Line
            //        int DateOffset = (int)((this.CurMouseX - this.DaySize) / this.DaySize);

            //        TCCDateHovered tccd =
            //            new TCCDateHovered("Date Hovered", this.BaseDate.AddDays(DateOffset), i);

            //        DateHovered?.Invoke(this, tccd);

            //    }

            //}

            //if (this.DisplayCrossHairs)
            //{
            //    this.ReRender();
            //}

        }

        private void OnPointerPressed(object sender, PointerPressedEventArgs e)
        {
            // Get the current pointer position relative to the UserControl
            //Point position = e.GetPosition(this);
            // Do something with this 411

            // We have a rowclicked event so fire it
            if (this.TheItemUnderTheMouse.ItemUnderMouse != null)
            {
                GridItemClick?.Invoke(this, this.TheItemUnderTheMouse);
            }

            if (e.KeyModifiers.HasFlag(KeyModifiers.Control))
            {
                // We are CTRL clicking on items so multi select
                if (this.selecteditems.Contains(this.TheItemUnderTheMouse.ItemUnderMouse))
                {
                    // we are unselecting an item
                    this.selecteditems.Remove(this.TheItemUnderTheMouse.ItemUnderMouse);    
                }
                else
                {
                    // we are adding the item to the selection list
                    this.selecteditems.Add(this.TheItemUnderTheMouse.ItemUnderMouse);

                }
            }
            else
            {
                // we are not CTRL clicking so single select
                this.selecteditems.Clear(); 
                this.selecteditems.Add(this.TheItemUnderTheMouse.ItemUnderMouse);   

            }
            // Look to handle double click here

            if (e.ClickCount == 1)
            {
                _clickCounter++;
                if (_clickCounter == 2)
                {
                    OnDoubleClick(sender, e);
                    _clickCounter = 0;
                }
                else
                {
                    _doubleClickTimer.Start();
                }
            }
            else
            {
                OnDoubleClick(sender, e);
                _clickCounter = 0;
            }

        }

        private void OnDoubleClick(object? sender, PointerPressedEventArgs e)
        {
            // Handle double-click event here

            if (TheItemUnderTheMouse.ItemUnderMouse != null)
            {
                GridItemDoubleClick?.Invoke(this, TheItemUnderTheMouse);
            }

            //Console.WriteLine("Double-click detected");
        }

        private void OnPointerReleased(object sender, PointerReleasedEventArgs e)
        {
            // Get the current pointer position relative to the UserControl
            //Point position = e.GetPosition(this);
            // Do something with this 411

        }

        private void OnPointerEntered(object sender, PointerEventArgs e)
        {
            //this.ShowCrossHairs = true;

            this.MouseInControl = true;
            this.ReRender();

            e.Handled = true;
        }

        private void OnPointerExited(object sender, PointerEventArgs e)
        {
            //this.ShowCrossHairs = false;

            //this.ReRender();

            this.MouseInControl = false;
            this.CurMouseX = -1;
            this.CurMouseY = -1;
            this.ReRender();

            e.Handled = true;
        }

        private void TheVerticleScrollBar_Scroll(object? sender, ScrollEventArgs e)
        {
            this.GridYShift = 0;

            if (this.items.Count > 0)
            {
                this.GridYShift = (int)e.NewValue;
                this.ReRender();
            }

            //throw new NotImplementedException();
        }

        private void TheHorizontalScrollBar_scroll(object? sender, ScrollEventArgs e)
        {
            if (this.items.Count > 0)
            {
                int maxposition = 0;

                for (int i = 0; i < this.colWidths.Length; i++)
                {
                    maxposition += this.colWidths[i];
                }

                double Delta = (maxposition / 100) * e.NewValue;

                GridXShift = (int)Delta;

                this.ReRender();
            }

            //throw new NotImplementedException();
        }

        private void DoubleClickTimer_Tick(object? sender, EventArgs e)
        {
            _clickCounter = 0;
            _doubleClickTimer.Stop();
        }

        #region Context Menu Handlers

        private void Option1_Click(object sender, RoutedEventArgs e)
        {
            // Implement the action for Option 1
        }

        private void Option2_Click(object sender, RoutedEventArgs e)
        {
            // Implement the action for Option 2
        }

        #endregion

        #endregion

        #region Events Exposed

        public event EventHandler<GridHoverItem> GridHover;

        public event EventHandler<GridHoverItem> GridItemDoubleClick;

        public event EventHandler<GridHoverItem> GridItemClick;

        #endregion
        
    }

    // A class used in the Grids Reflection to gather property names and types
    // for population of the column headers and data in the grid itself.
    public class PropertyInfoModel
    {
        public string Name { get; set; }
        public Type Type { get; set; }
    }

    // A class to hold the data for the GridHover event
    // This is used to pass the row and column ID's of the cell
    // that the mouse is hovering over and the content of the cell
    // as well as the object that is under the mouse from the Items list
    public class GridHoverItem
    {
        public int rowID { get; set; }
        public int colID { get; set; }
        public string cellContent { get; set; }
        public object ItemUnderMouse { get; set; }
    }

    // A dummy class used in the test rendering of content both 
    // in design mode and at runtime via test data properties
    public class TestStuff
    {
        public int Id { get; set; }
        public string Name { get; set; }
        public string TheType { get; set; }
        public string Description { get; set; }
        public string Notes { get; set; }
        public string Status { get; set; }
        public string Priority { get; set; }
        public string AssignedTo { get; set; }
        public string AssignedBy { get; set; }
        public string AssignedDate { get; set; }
        public string DueDate { get; set; }
        public string CompletedDate { get; set; }
        public string CompletedBy { get; set; }
        public string CompletedNotes { get; set; }
        public string CompletedStatus { get; set; }
        public string CompletedPriority { get; set; }
        public string CompletedAssignedTo { get; set; }
        public string CompletedAssignedBy { get; set; }

    }

    static public class ImageStrings
    {
        static public string CheckMark
        {
            get
            {
                return ""
                    + @"iVBORw0KGgoAAAANSUhEUgAAAEgAAABGCAIAAADghUVgAAAAAXNSR0IArs4c6QAAAARnQU1BAACxjwv8"
                    + @"YQUAAAAJcEhZcwAADsMAAA7DAcdvqGQAABPCSURBVGhD7ZoJVFNnvsA/3KpABEKAsLjgVh2rda0oewhL"
                    + @"CCEbuFWnVfve6bwz50z73jtneqbtmb5Op8v01borIvuWQAJJWEXQalvbThefTqutrS22oOwhy81yl3zv"
                    + @"/yU+Zg6VvtKRwZkzfz/DTUIu9/f99/8Nwv+g8k+wv5XQHsx5MPZwmGUwS2PWRR45Gtvc2A3vYY7FDL6z"
                    + @"nG6MbRh7X7FZRzBFY4sTe7DlftQYB1S+n8DkcnJuB6YBDq6d47gBmhrB2IoBwYEpF6Y4K7ZfZ7u78JAd"
                    + @"uwi/m4PfpO5HMBqI4B+2Y7LxsODAgfHXbqsL3gU1Om2Ys1F46H3uT69/Xvn4+ede+q7i6XOv13zaaPOM"
                    + @"YJYz95rh4/cfGGXHHhZMDHhAMwQGFEjDD7BCsEy7091d+UHphgMytH8dakhDx5bzCuLWF6lTX1BbsN3t"
                    + @"dpJdYe5DH8MuBjMA5mSxG7DgCB7tDO7vv9X/xcsfFkcXqdHxTTOMGYIWSWDJxpkdSX7HVq85lnMd917u"
                    + @"vwbO5oZPsvcfGLgK+ApECM7hIVQshz0OzA6dxVd+fuHF6YdSUFkiOqMIbMsRVqcsK0mapdm8Vqtqdl0Y"
                    + @"xhYXZgatVuKh3P0HZsMuGiwJqMCxWAgDjvdsl47+qRS9FovKE/n1stDKdH55Wow+d16zkm+UBr20vuhW"
                    + @"PYXNjsEBEnDAlhmInsx9B+YCNUGEgLjmAufiLrE3nnz7Zd6rmyJrUkOrUkJ04vBWRfiZfH9DDqpKQyUp"
                    + @"//H+GzdwF2Zc4JGefgeAgcIZ7JoyMHAGEtlJDGRYTIMFUpi2srZhjPvBmBgGWwa+cF1a3bIXFT4i1MsX"
                    + @"FCX4t+bw3toVdkq2pGIr3wCeti6lcavvbN+XKQMjnkDCHccybppkKuJW8NBlB+vDeHikG/cIjmSi2ky+"
                    + @"SfGgJndemyqgRrSoZXuERsbTyUNatocfTD2H3/ad7fsydaYItQVEMI51esMeWN+dAAi66r416OmefUCM"
                    + @"WqSoZPPiBnlknWTO2fyY6sxl5eJAUwY6p0D7txR8ZWBI3XF3mTowGmoEFpIVLGKWgOT1K0yZu/BNv9cS"
                    + @"kSkL1abFt+yKrcxEJvEM0FutbF5l0szzWagmTqV/yg3l1wjZnbvK1IE5KchXUFhAFr6TglkSLT6zf7ig"
                    + @"RIXO5SFN6opq6Zrq3DCtJLA5N7o+f742J7glC7UkRReKKTyIRxgP2ZK7y9SBMU6o/kjRxHAYzJHhwLeu"
                    + @"4K5l9TtntahRWfw6vXq9VolOrOe3bV1SqVxYLRM2KZE2Bb25wTjUjmkbvg3F4rgyhWBuN9R8YIpQtjs9"
                    + @"kIXPD33wy/f+gIriQozy6JpMYWFSVH1u9PnH0OFNyxoeC6xJDWyV+x1JeLz21xAxe7+57o0/48rUgbko"
                    + @"ACOqgnLJAa4ysv+jAshXoZrs+SXpC4zy2WcVsxqyY8tyH257EpnyZp0Wo6r4JceUNjyEaUgPYMZQy0PA"
                    + @"ubtMGRgLgYPj+qF+Aj+x2sttZ1DBSqFuy3xNVrRRwTPmBuhy5xl3PNi4O0qjCq7IRh2isNc3vt99FkPD"
                    + @"YgMzhgYNduY+DB4MHnE4IHW5hgZuYfODh3LmG2SzNHExOpmgLjtAmxV9eqdAK/MvEi1o2RZqVKA3Vz31"
                    + @"0WvD+Baoy+Mi5T8UGRhy+zgydWDQOsFjz2037k996+mgk8mxhxMCWqVhDfJQvYyvzYpt2za3WjSzLCG6"
                    + @"c6ufJnn9wfxruIeF5E3KYjzsAr/kANJ3su/LlIFZIG6Ah9GWVz85gIpWBxvEscVp/EZ1oEEpbMxf3JQf"
                    + @"rknn69Mj2xX+dWno5HrjlU4nphmKglwHdthvh89Ds3z/gQ3BfzPdQ38ReGwTqtnAM6ZGd+RHVcv9Tepw"
                    + @"g2rlmZ3+JZvDmqXR7Wq//WuyLvyKxEDI6BTrm3l4gwapM8nPu8kUgnGMe1Be82+zm7PnlG4Kb8yaeTp3"
                    + @"frUyuHkbrzJzSZM6qFYUaMwKKBU9dDj3Ir5MQqCZDGoAcISoGwTatvtPYyzu/f3/nJh2IjXAKF/cmOdf"
                    + @"nBTepg6uzQ5t2hZclRlWkx7eKkealOACyf6Py0mbRnscLpLToVZ2gR1yZJZFjZ/Lpi54MFcjqnORRhSt"
                    + @"VYdWy2L0qvk1UtAe9CPRDUr/4oSwDhUqTVhZt6cb4ozFTXgwZ8XQcdHehstNOVxQZ44nkw/G4V7OAdeE"
                    + @"Ybvt1DDm4CkEgHmmPOiyBFppWG0OHEQ1KCPrFeF1shXlaoFeFViWFtGajw5t+bDvXWy13fKM3Dnbj5ZJ"
                    + @"B3OQ8pbgkQUR2u2AmrDmUhO/RgILwCJ0uYDkA4PjKL1KWCWJaM5DB9Y9/eHr39E3yDm8Pc6EZNLBRsAR"
                    + @"wHZsHqjioV3GbtcN5otVFdt45eKQ6ixQEfDAAqXBMawHTFkPmvID6qThB9IG8DcUtvVBFBy37RpX/gY+"
                    + @"RnMulvaQbovUu9jyVPuzqGgN1ISgn2iDChbwwFNYoMDpDckLz++ceTD+0MclmLU5MTMA5xj2nWoCMvlg"
                    + @"LsaMWZK1iDVxNd1tYSeTp9WuBxUBUoxRDeoCNwMqn7MJ9KmoakuG9kkyBXaB3Xo/6PjBSv5uMvlgDjKm"
                    + @"Bt932cx9uH9Z8dbp2rRQnQgYfAv0Bhoj3tWgBM5YfTZ6bVXrwDuYooet0IwCIMTDCcukg0HZRLacdXRR"
                    + @"V5///Ch6Y12gSSGsI3ECeEBLcAB4o24mqBbvO/0bJ3iV92YLqTIYD7nhMEGZdLAu+N9jx1bzFfzJ7KOb"
                    + @"AvTZAWVZQsNuXwwEEuABXcEjxBKoM1BBnAV3Y87eCxkYXHIEQ+tl5gZ9Z/vxQsCc5iFSTXI0zZJpOaz+"
                    + @"AdgxqwvbLn/9Tvt72n7nDQpbe60WeAtinI22uj1OF6b7OKsFthTsH7YUanXQDAQ+J8mh8IIFQ/pykxre"
                    + @"4rZgc+gJCaqOE7TIIRELdbkRpjwIFZG10hiDIsIkC6yXTKsTo7L4c+fO9fb2+i6OYe6UgqMHP14QNTQA"
                    + @"2cXMQtkM+RzSO+udQOA+/M3eU7+IeTkh6Lgo5M2kZ959k7SuQ2aby84yULOR33V6W2Di1zaWZkg3QTA8"
                    + @"UJkykJHdHtgslqFgCxy/Mv4uskw2Q5/BN0oFVRkxxvzpleLVF54QVmSEVKXFnH8UFcUJG1Sx5XKb7c+h"
                    + @"nf6/ruSngIGuhmjIFbibZd12qDQZ2tHfQ13PKs0LO7YZtaSiDzJQ5c/mHt186IMDGN8k9xvhr4CR0AQE"
                    + @"dOiENpZ4EcORewnkHVgko5L34E+Y38FfoBfWR2ll8yE76ST8epmwPm9GvXShMW95rQJeQYb0kM4dvJc3"
                    + @"H7xa4rssELfb/RN4RgWBBdox2+VyEVuCrDHc8ym+vLkwFxU9PPetXNSUgBrjoi+q5xQ+En044YVbJ92A"
                    + @"BSQWGpvBuwmjhYZCjoAx5FRkTuiAdyjv7MkFT7/eYtiLtCmC2szlpRkxOqnw9I7AquyQzkcfOLblYUNe"
                    + @"RIMUNaQHmfIzCve68aDH44FrcTphu2BvfrogX09jN1PkOoYGv/N8GfnGFqTdMOesGmlS5xTFz6tMX6qR"
                    + @"CqsyZ1aIUYVIf73TDA4IpmhnfR4EinGDylja6XH65oQOoPLeKwEjL7x6BL2ybNoH+bzatJVlkoV1uWGt"
                    + @"eTxNztzGPKE2Z2GNZK42LahNjf5r4x/Za9g9tqwFTpYl+zZRQXBZNOUGC+RGbvfgbxb+Nj7QKEWtCfMq"
                    + @"cgUn0x9u3bvx/JOhlRK+Jje6ZSc6lbzqsLK6/wyJVeBAFAvWCPtiI/cRXQ4WAgbhxDbobcmc8O2Ry4uP"
                    + @"Js2qSkYGUaA+Y2m9KkYrnavJEjapgytzlrbv4mlEYQZIXGuf6HjRAuf03o4dlZ9MBULAIK8zzoHr+NqC"
                    + @"wymEqjhuUedjK2vVi3UqXnkGKkmeo88Ja1RFN8iXGBTo8Mqk5n/R3monegPxdn7kz7O0jXWSDYfnFCjL"
                    + @"8bH7sz0dv0VHVi1ozvMvTOI3KYNN8ghQvkYiaJQvrc4LbcidYUwPN0qXvZ7chbttsC1g22DR0Gt5PHAA"
                    + @"wQMOyF+ZuCArXB6De103RJXbkCEeNW2Oad0Rd0A9UycKapKGtSoi2pRRLUq+PpOvTZnXJEEXM9ErSyRF"
                    + @"ez+13yA2DMqiIPqB/ggYCRawxTaIZpaKm/W8VzehC6rQ4vT1tTvD2x9F2vQwrfShxm1zNKKNtbumlaeg"
                    + @"RpHfqQ3Hu0tv2z+HgOOAMPQ91wI2u32sif6/gmDfndj1Ee6e+ft1YO4hBklMY15EjUJQnxvWIA83KHwL"
                    + @"juEVWNNPxge8ux2Vr11dq+rzfIatQ2B8UKP2ARxsLu2hyCjT3o1v8p/fiDrlo0UgZOHRpgsy2HytAnpn"
                    + @"VCZKLN3N4NvYZscWO6l375EgegSCM9OHexf8ZmNgjWhGu3ROfcYSvdoHNso2CrbirX3o+Hphu5rcL31x"
                    + @"87fkNp0DD9mJQUKM93BOuxk6Xln5L1a37/OrSQWS0QV4UG34Co65daLwZqX/oZSC70wMZ8bg54z3BsU9"
                    + @"EgTGw1kgyplfOf0q71gCqtsS2AGNelaoXjbK5ls+MF61dFPnE6En4lHRI6hFOv142regLZsZQzUC6d1p"
                    + @"hWqo8nMj2r9Z8PYuYXEylPC+ismH5FMdvIgMGwTlKbuanuknAdRbv5NBIezyvREEOz3S3YtZ+23ctbvl"
                    + @"P+cWJ0/TbAloFPF1OWPYfGD+Gun8StlqYx44IXpbhpqzo5/b6ILEDQmAlBr2QdyzrnCHX6sKVWx6SCcF"
                    + @"5fiWjwr0dgdMt3LB0cSrni+9wYeEVjv0KJwX8l4IIurnINDSFmz7xvPVs+deQr9bhoyJIXXSMWw+ML4x"
                    + @"b2Hr9lmn4hd2bAtqyfErjw8tTl1ZkXOZjGnB1wb3mJ5BRSL/1twHKhMXt6l8Uw2g8pmiz8H4NZIZpx7+"
                    + @"5fnnSW/cT3scJImO2G3QBNy5rr9aIHiQBP0ddo9AjcQ6bo1ce9z0736F8cG12T42H88o2ML2nX5VIkF7"
                    + @"XnCleJVOGXEiIbZzOyr4Wbbp16WfVhdcKuQdTJzelMPXZcfWKx7QZYxqCRbgARKvXOxfkppS8uhV/BVJ"
                    + @"e0Bl9oAJkoTFjTsnnKggcPc+m/VOo2qB/OP6Ct98vOmZIK0E2O6itMr0yGZVIFR6zdsiC9PWNe1CxzYI"
                    + @"P9qDXly7tnmPoETM12eFN+ZA0iO3FGpyfGFjVF0QJIOrMoMqMxoutZEOAIIqAEHVzJA54V9ZRv2loGEo"
                    + @"PCCawY6BFdhJDUxh2036ug8MlDYGLLJUtLxtR7RJPaMkLaZ5Z2i1LKpOwS9LX970KGpOR83JkaasFdrs"
                    + @"gPLU6dWSFW3/CloCHrBG3/LZJGgPYBwOF9SdbpYizumkIMvfs9BBNDaeUCOLKvYifZrgtGJBbfr8ElFE"
                    + @"rRy1Kkd9z4d6R431JOLddc1tVQhPKebV74uoV/LrNqGz8bOPJ2PrhBvHicq4YMOk6LOsOKJA5euimuWL"
                    + @"qjJnVYuXNe/2KdC3/pJtDM/oCq5OC23cIdBuj6xXzWlLQ0eXHfiy1Ome+NhpgvKDowHGNczcXPBmJqpN"
                    + @"5LXL55ekLymWjMnXo3hjeEZXVHlKwNmtPJ1CYFIjffLm4zKInIP3Ll+NJ+OD9bog14LbuXFP2NEsVLIx"
                    + @"yiCfVyoSmlQRRuUYMEAdwzO6FhmyeYbMwGaFX5Ni1om0jmtN4FuTrq8fAIMtpc2UC8IVtvf0XRG+nIx0"
                    + @"KVEd+T51jQGDNYZndAnPqKKLEucYMpBe8oTpRTI0sJM7yJMt44L1QxgGhXHgaizjHO5z3og9mIn08eMl"
                    + @"7jE8o2tWs2JxYSqvMnGpYffn5i/hdKRRcpL8MqkyLpi97zYxRcgwAw5Sm9IjXeZP0JGEgKp06BSBDZB8"
                    + @"/gZU8HQMz+gKqM+fWyV+qCrr6CcFHtAXBHY4GzN1YJhyYIqCjph0esNQCLo+YT/r7r84u1wEbKA3Yn5G"
                    + @"JYCB9uDpGJ7RFdPwGNKl5hp2s5bPIBUPkfmDxeNrUidTxgcbR2pu1q8tUqHjawI6siIaM8JLUyK1yqCG"
                    + @"bZGapEUNmbE6SVRl+rzanNjWrRGNudOrklBTwpIjKTeoy5h2uIbIZNJMvgF6zyqM8WTCYCO4p+SG7sFy"
                    + @"JTqxRtAhgxQXXJS6oEY1/UwOqk9DtWKeURFcL59TkR5YnQEhFB1a+9yVg07v12kY6s7Xl8kN10mWCYNh"
                    + @"txtK8uNXygVvJKKSRwQX1Ita1csrJEGm/KjOx2M6H4PyP6RBEdm0NaxB6V8mFhft+xr3kWaUJpOfIfId"
                    + @"6x+623+vZMJgjINMoKDnPfjRqZBDyag8LqJTKdSKo/+QtKQge3FFTlhZKr8yNVyXxa8WzzjxyFtdH7ow"
                    + @"bRsaAk0BzQBFxls/8P2MeyUTBuuDQmuYwk64MvMrHx8PPpGGCjciTdKac/tCa9KDdamLz6siGkR+/710"
                    + @"UXHK3ivPkT4ScgY4l3eCTMbGHjKLunO6SZMJg3X7JocUhG0bhc3HuvSra/cEVcgR+JghHhnjkClxVuH6"
                    + @"JYcSXrj4Shf+Gmgo2AjvVM5OkS9qkPH3fehjdrsVnITGzGAfMDrc2Ham971n3znu92oC71hG8Emx4FDS"
                    + @"luLtb1ws+NbWBb8ILmlzO33fZXbarL5vQtGAN8ky8eBhcbktw1DFkhtILid2kK9gAx72WHqHrr138+z7"
                    + @"gxe78S0n9ONgrWQ2x9kxN+Sxe+ehDLnPRKqZSZeJg/2dyD/B/t7kHxQM4/8Fj39V/tDIjzoAAAAASUVO"
                    + @"RK5CYII=";

            }
        }   
        
        static public string RedX
        {
            get
            {
                return ""
                + @"iVBORw0KGgoAAAANSUhEUgAAAEMAAAA + CAIAAAD / FDE7AAAAAXNSR0IArs4c6QAAAARnQU1BAACxjwv8"
                + @"YQUAAAAJcEhZcwAADsMAAA7DAcdvqGQAABiUSURBVGhD3Xp7lBxXeed3b9Wtd/Vr3qPRjN6WhCVkMOAY"
                + @"NsDagcQ4JyRZAtjBIdkYDMlZ7MQkTgCvIeuNjYM5CdnN8l4g4YSHszGvhdjhYQdjCMIgOZIlo9dont09"
                + @"3V1d77q3bu1XMwNxyyNWCvkHfupT3V1Tde/93e/1+6pFiqKAnwrQ9feffPy0MskgTXkEMgIhgBeQZsBD"
                + @"4AmcgURCAN0MViCCoo+HzF+/6cKBY4EEKPAfCInjS3wJkSUAOZ7CP4nymBcyhLQLMSwCTgo5HvAMF5DK"
                + @"1AeRrg83iHPjBIcSIAnPdBybasiUS84YaRG9FWZ7DQ2kPOOdHh+e1kMFbLJ+24Uhw8F5THJh6iaszYtc"
                + @"CtlhXC0n0xgwBfmsskU0GeAECnBXCCYZ3jzLZMjsPeUfz8UAk16RWMTQcHQ/Bc0oTyl4SWm5J/N+XWPD"
                + @"OYFQFRV1rr+wpWIB1Fbvu1AEEODadKB5kCRRatkOMa3SLfopUgBGQAWRi5wLNS8UnNhuhUgUHAMM2soU"
                + @"awQvDpPcruOyzsUgE4iqYBBOy91jgOMiKaKU3/T2bPD1z5MdO+29/xEE9fyQNrh70Uw8XJOK2yNV/FpQ"
                + @"QEdpht60qIIJoSZK5z17SHvo69aDj5LDT2ZBNBdy2LNv2+tfB9e+LNGdPlBcV31tuEEMMMHYUHHXS1ND"
                + @"piExJJK5oFnpGX7XPcc+8fEt11/n/P5/B1YSyHKhaeWCLgKrkUEJej7gPAluvEIkyD7ElbOnyKe+yB74"
                + @"qn72+5ne6Q0nXk1CK9w5dWX6wMmHvfyye+8c+u1XJ1waigUbzTvABEOchDibATa0KbqrrEBqSA4fvfur"
                + @"977zClYx9LGTl18xc+ed4E4ovYt1LgAMbfR2BfoxEBHVLQZ5CL0WvO2NScdL2j0ly5lp5I4e6IChvQPa"
                + @"i99dGp6+gt1yG7z0BYuKidYcLQeyV4cbwAATzFTgxZpZwfkWi6hOiZFE8oGvnbnr9SNTM2o7MDiZi1dW"
                + @"rn7+M2//CwiHYBhD5SIgA1FYakyBFpklE/B78Lm/f/QjH95szxMBLGcWWBZ1CehSYHgXS+r8Mbe69abf"
                + @"nfi561qgjOSWnRawuAzbJ9ZHfAoGmGCmY6nQqFkwOOs1p10DDh373h/fOZoeHDGmpKJzaNpG2D21+N09"
                + @"Vzzvo5+2YHWDLgKil0VMMw2IyZOHv/1n99iPfuMZ1UbfdFSVUpVImguZ8iItMM7RE0+ykY/+T7jswFzA"
                + @"pjQXvb9LxYpFd5RZ4lwMMPGAV5dWYHQ0xGE5ztuUN94gv/+lbGg/pVRBpyZESqwAIsuyPM8nP3sc0r5o"
                + @"9OeANkSlIhzQoUe6Nb/e1GPQ5BD6U5almmuCQ7rwBEuGHWUY5tP3v5d84JOaF8itQycmTPeka26VWjpP"
                + @"jq0Ym57VlLKfzTeSJrn/tKZppmni7OtLPD8GmHARscIoGA3CvmsL+IU3fDP+xt7Nbt5lONbacHg9ckAy"
                + @"JaXUafzd+4OhZw5jDtJxFzE55Um1jkXJwTM8Xio6rtuwC5aGomezOhHaoUPdj/zv7Oi3CuILTRq54nC2"
                + @"rMiZoYkTx58Y2j6mBZHWJ0cVuu+Pbwmf+0rGGJJZWx7OiEfcTcTamadigCsTbJZR3DzXTvkb3xD0Hr6U"
                + @"ak2/3BK8GTms0cbPqqriHAo9OH/Nq4aPnOzrAOj+PvhVx8BYQwmAs0bmuLJJgpkQmqmpRTLtOw8nH/xQ"
                + @"8bkvucttpap1XSWhikHsmWF64oknRndc1o5zP+/P5yv7fu8P0xffgNb4IY212dewduYcDNhE8ihjFu5m"
                + @"9aY/zA59KN5bkFat0tsV1Jtrl60dVzel3JU4b49olVNH5re+8z3ta1+pEaXCQbCAAPES3qA1rBepBp6O"
                + @"CacH3z7e+r0bCSuYSVEqFGUBhJwZquk4fntBVySza158qrO4+823sFfc5EPNLRe1jh+uc23qp2PAJpRh"
                + @"2SnhLS4uLbWDICg9ylgt9k8ZC4HD4Z9GtG1H26e37jZnb7tx+PMfQVnRwbzqY64E3aj4GnADw1aMotc9"
                + @"9I35W95cD5vUSdsjxYKa0Th3ueFKpcn9bl+dHGrA7OM9r7vvN/+AvfxNHaTxg9lwXsTq7pVYP/s0DNgE"
                + @"En7GUGfOJrDZX3rldeapw3q1Nm+7Yz+gsXZcH5KQWImGefWIN79t91j324drr77O/P17MtkoqNClGhGB"
                + @"1hmFPtz/QPS+96+cOTLxzKFexgORG4pdLQyWo1DFZJvHtQnj8cfsmtp84VUTb/tLSRyKtVPjgBbELCYl"
                + @"zrUWpT8CA3/2Mm+GF8VmMxPV8U98srrrV48phQnrroVYI7A2Osb9sKRNQ+yd2L3wvbNs5xR86otw060a"
                + @"bbG8rMEKxKNyGf7poeh9/6v1xDemnrtpIRcko5O8OlrUsXis4BU0dyjoYtm3nPTZPz/xR396BkSMd6NZ"
                + @"k/UEszZvOf0qfriYczBgkzILd9A/a21BtQwqTgBv/K34u/fx4WeeMxYywWOFm22ynNpi2twOx0RTg3AH"
                + @"r2u92ruPeS5uUt998MHeu94TLhwxt9uxnllJw2EO41roZxmRiq1IJU3SvnH6yeQlrxi/+R6YnPHUbBl6"
                + @"EzDsLlMYW59rLeWsTY1mxGSztpKnYtC7MNjR0SkKUqnjrnAp55ZOPn50xw0v7b/i2cpiAJxkBqZpVcsZ"
                + @"i/LEycvMKFEKos4kJJdpmoqML116YP9/vRmWlmZvvzdfXrK2j2hJUvfyXrSibNvX79N6nEhtccXqzbib"
                + @"4aGlv3nJldff9haksZIBMypokiLo24YJ6gYrPh+exkQrtR2WPVNZFXpCgufPnv2w+5I74cB4XWHhkDIv"
                + @"Ez2EGXuiJ3pFXhpHIbQU4bh/HLdMLGXa8PP2+Csr/LFjYzU3HGKp720vLFE35073pqd3CzXOkjlIo+U2"
                + @"N0e2T977LpjeDsxq9uNKrVFufhrrKBPJv5kJljSlZMJlWfgsVYUctxy6rEUPfdt98S3Lz7WMImJVCxSt"
                + @"v+JbllPeLsvEohLUuKX1iSzsSG0rWT/yJzXLGB8NGPej/igYvg6VFdGPQzmh5d3OSGY/lqmX3fUOuPLq"
                + @"VWfAroRhw4XIeKIx9CesUxeKQSZlDS3bhtIY2JdmGeYOvaxNSkbicPGf8+e/NtuhTubYLarHlWQcqng7"
                + @"gmIbswq8HY92IsqQxeZJkjjHRZGc0DxOsVU0sLdlfG55dqY6deJMeMnd74aff6EnDJnmVc0tN6PshGWO"
                + @"YokgqYuwyWBqQ1VVYHtd4IDYoxiaoehmjiHQRI4mTOyvHfzY5EJlUcqVZm+XNYweha/SGqsJTeAKoMDX"
                + @"IsVYSIROPJUvh14RJJVcIQU4PvR12dGS7Zt3npr1pn79BnjRC/upSlSLWe6aK+VRhvHGyi+rtC4Y5zKh"
                + @"OCPmSsDmsxxpfbAhMEKoJ7pa3yMf/YArxv2t095hTJi47JI2MpFlmKCCLV98rBbIwovj2FLN0XpFNzVQ"
                + @"bNcpuMRj1wtm53v2FS+yf+dNy3le0csQx8639FOc12RUVwmaOUUNexEYYLL60AMw5UmeQSYViR132bX4"
                + @"qig9NjOTxOk4O9nDH9rSsha2NDBTYYgjAbyrNMsab7QQZzbXiC+TNMf8Q3OSBX6HB7xhRyeWdldmHucw"
                + @"+c57YqtGTexkhZFjTkxEnhZlpKx6e4FFZoPG8EdggImPsYiuTQrUh0Qtm0ZcG+YwDi2h8sKFwsCEYkuo"
                + @"5p/6oz1jl2DCxUyFIb52exknpaORvJVUtaGGPawIKvOCGia2JJzRqKGpSz4/1b3mvk/7Dup+pUoxi4UY"
                + @"ZhpTGOYXtCcg/whiDhsVjR+BwYg/HzgXJI7UQoUqhj92ewUThRKxO976/Qc+s5mEZt3wK/WUODAXjNhD"
                + @"Huuv3ziI6sLhI1dePXLD7SOXXhExTGKtcW0MUurZkQW4cxhjVADJBXcz1JiYV9ZvvBAod9xxx/rH8yNO"
                + @"UtXATITxmNME1TxglGOxVbdvr1uVBJszLxG4mapq1e0obZHyAcgGSEZr5lePGUzoVz2LiaqjunMkc3Uw"
                + @"pKrklGYSW15VUZmi05JXmXYuHIMRfx6EFMWewqDQC6HIrDwlQc30aMte5TkvWFGqpKg6whQ+6ttiTvdW"
                + @"b9oAcZsOP38r+/zHF37jN4D20Rumcv27sAwS87UCVFdyXRGqUpQ1LWRYpy8CF8TEMhkmE8wumHGJo8ZY"
                + @"glGf2KqVdE4efKTnLeuuoVQqRSb8Fc81nPXbnoaRqeGl2dN0whpenH3sRS8jUbujpZekJnprjt6Ejmuq"
                + @"ZU1LsaLwUjZcDC6ICU7BcILSGIoPNNHQkfICW8Q/vyv4zMcatJUZfq4ktlUlbT6Zb/hgrUQvOpFWK2Ft"
                + @"iwb6ZdncQ9e9sBEuWKIh1aINWbM0fulRRZJRzvF9/bYLwwUxkelq8admkGBElo8mzWQpfvQf4POfnWye"
                + @"3jKs9/OV5bDtum5Fc1l43hV0vE5t+yWkp/WzrDstf1aVTzzvF6B5zBJBDXKBjT+mX6y1uklBRYdbv+3C"
                + @"cGFM1ASLJhQYhroJ4GJ0f+Ur8YffX2Sy2hiTig6KIXOaZZleNdrFxokLQUb2Vo+GRdE/PpzXyUjU7I9c"
                + @"Wj3+2pfDI4/pzdZw+Uxv9aE9vqlqgpnsYnBBTEBBFSPQuywVDDTK0dn0i1/OP/Ol/sh0XJ+c62WONdKw"
                + @"693eSmbIjoE7uzFmzo62MTNo/csDmCP60qX7ZGtxFz81e9f/gEcO6oLrIMMoKReFcXgxQhgxWE8w9HiR"
                + @"Mk6NslwzHE2qEGKd5oFuUA8sN4X8WPu1N9qPzy7sHI9oMqG4TihJJJhhEo3mIlNlcZK0to1tgTMdcJRe"
                + @"nS+vdGfIJiV2eC1utVooWAzLDIKg4rg60+bOzE4WXb8ypl//Gvv6/5zAsFE+0YeoLCbLOtgKWGlKMSVo"
                + @"BFBsgB9AfYOkMlhPUNJqBPM5lukgxCacYrFHGYtvWg6JA7o3O/uGW+uZf4wEl1hThUQRSzVFVSwtILzD"
                + @"w4RwnLKmqF1RqO0inO+2q2xidMzuiTbENdfGDlDXdRREYRRlacY0za64qSa4ZO3vnam0Um3fpZmjLIho"
                + @"iOmohWlSZhpcEqMQpVxkCXOs8iH50zDABMUsKkh8J2grkRuGg+mKK8CyUmkbarj8gfcqf/85aWTq9Ca3"
                + @"R2xF5UkmNVLYak+GYR4ppqKYarrUHc0a6q9es7xjZusRX+tHh7Xu9mo18EKBtRWbGcZUFdOfxK5UNXQ1"
                + @"p/2M682e8/gs7bSVZ81kDvYLREtReTJQFCgy9BZNV6imCbpxeh6IE6lKXsSlnEURr5cWRM+L8IvEkE6T"
                + @"D3+MfPy+ocvGfUg3Z2ZAsEpqeZ6HeRbwQMn5CNOHVUNLREU4S5Ua3Py6iTveCjsORIvZiDPU6jRBoQY2"
                + @"z1ykYYR+Zds2CoNe4JuhZht2fcdwViwv/O1H+F99dKS/rJEUVaBg5ctLemncw6qDHLIfyLxzMGCTHFNh"
                + @"qToKpZQbKEcy7D/KRoGl9ODBxVvfMqaEp4eEbbrmbKDVKpCKRIVY5STPGrniFIbaF96yB5XNw3/7rp4+"
                + @"oRRW8UtXsjxh/+db6bCj43pt3KAi8gOZ56VmpNiE4SqsGm42dpZDYFaN5UcOJUdO1/Y/gw2NxgQ6kOi6"
                + @"ZqOvobLOCZpzA986hwlqHYF+rqMj0pJGzkuJJbNw4Tv+m+4Y7y8mOxws7zp1l0XUoEqUSlLVuJIbIneJ"
                + @"DUHe66e+bjhvfzPddZmV1TCZHgal8awpa3SX/M6pU90ncylNphuqVmBfjTOi4CqKzpBlza+YONxkpeUo"
                + @"vOs7C571xGny7AOaiQIJixjTSompYQ4qpcxG/jXARMZcYTo6ImaI0hqlLVNYmOvd86fLD//D2OWbujIb"
                + @"06djX8ohTUu9QmjMNSIRMyFNavc7wXKjtuXl1/Z+7dWVqI6zeipMYyPMdLp/m25NpbPf7HY62AXU3Sqm"
                + @"J4x+SrAKpQW6UBg7qHdE0WmujE2ON+ruqYe/Znqe6lrm5omCWEiIYQNEqZ/5Olt/LPpUDMSJougqLZsr"
                + @"vyh/+S1V9umTX/mbvx772APbfu3yb4pZN5BRJ2TUbISkXUehhE0Z8DTlvGweF3LOt21WfvNVI1DtJEmq"
                + @"Y5sJ6nKh9vUO6Mf/00unb/j10c2Tvb4X+j5PMwwYFZtnoBPzPts5ebKuxEvBJWK4mpFF3mxcvqn3iU8E"
                + @"n74P/uVJdBINwz5Fxy80E4vzBjjniUQKVElRs/cLWiHq0qngLTfSg/+YTO5TFAWzDV6CIS5XH2/iGUut"
                + @"e1nbocGZ7uKOeMIb26P/3ftUNn4+MX4UYM93/hnufnPz2LfY1Iw+tKnTa2rVvN62E4kNKjEw32c8yZPY"
                + @"pkUN5Z0YeqSdjI4Zf3mbf/nPeFDLuZzhFdjop7RBJjn4PIggH2MueJ0T7/xv/Ov37x5RPO6u/QyEFyMT"
                + @"POJnrAkChJBxworN6swjhx+/4sv30cpesNCv1sc7FwnEhm/2zvq335l+4TOiAuPPfU77dMuo2rHEEoAt"
                + @"NjVzIrHVkjylhZ3zzpA90lGWe2LzvW+FZzwblE2tQoxY/z/vwjZXMxzNwBTehfs/3fryl7A7zaiB1jjn"
                + @"AXPZ4xLi622RBpvJxPLB03ve+65TEztAcbLz0UAwT4Lbqe11/+Tu4D9cPo5hfPDJijEaFDGnPCFJrwj6"
                + @"SiZNBSPBzJXAUZOk31N6U163c8Pt8v6vgslXn1ptgEEmWEswWoAI2V6575MHTDrRqC8mpdHQDog1v0JW"
                + @"iJKKlU+4Q/BP88vX/1L0s7+4PR/CaryC+3EedBS7aPNGBt3q6JYPfnDlRS/p5UydX1KTrJKTOqFGIWMZ"
                + @"B2XriG2XERtkzM+thJNNtaTT5n4PixuG8vpwgxg860DGcZiMYHY8ftxwqOetVBrjSACdCoGXrDHBI342"
                + @"E7VYTtOfObD/HW8hqQJ9NcKuIkeBsTEagaoNs2bYqudYqrfV7/6rpWuvpliQAuLEiiutMcUyKI3z1M+z"
                + @"hIDeiZyxMSc2jiwsDn3y7fqvXGVFtLM+2LkYYILZkKayQaop6Eo3Ahb3UTqWSrgE2gSvWeOAXznnwSnp"
                + @"uZP6X78dwJ4M7KxS6vFJZePcUkKCFotqXV8oeiiYCa/tvvUd3utf0VKNbpBDK0K1apPSk3NUS6g1pIBZ"
                + @"r8tscuNr4MUvE6PTQIvz/XA+wMSDyDEoxmUAmlsfWVQze6KWNJulIz0FSANbkSRJoHZJ7dbfmXMmwgC5"
                + @"Q6hAZ35htcM4D1wMRcFys1Brrb5P0HjKMP3tt7m/fI2/bbqZZGk/UiQ1mYZdNka/kucBKN61V+/+L7ex"
                + @"oC6hMmsY1fM476BN8B9eF6H6NGHP/rNZ36hZJE5W46IE0sDL1gyCZLb8ynX5VS+YAi11zBMO1HOYmBiV"
                + @"mMrPg0ye7leMWGETK7ANhabbXzQKPXfGX/+a0atekDYqYflAvSxTKL1pxkcXIu9Vz99y8+tIPEQ1G2Xk"
                + @"tKAot9eHG8RgFhZpDyVK+X92BDz4lVPvuducPTi+qxH44zihTwJp4Aykt+DVlcamS58D7/7Q+o0/HjD+"
                + @"UhDWtw4u3nTzuNo6szked4eMfzxz+N4P7DqwX986XbaQuWSKrhQgk4KaG+THAZuUJTfLGyiI8NNlB4Z/"
                + @"8aXepi1HD552WJz7S04QTPWMqUO8elzyvQfgbW9Yv+3Hht/rWdjFb5+Y+OxfHK47W46q3//67Nm3vmbf"
                + @"lVfoMxgb5SJ/uN9rfvF0DNikC3EFO5SQgY5qTUB/gX/hC53/+2B44mugqXrBrA6t2tP0mmvht15+cnJy"
                + @"Gya7fx8I2U/CCouhN3rsKH/jPcrz9s3+wS9vcfaXNCjF7qQoFBV1BS4WnWujijLAJIVYRxUv1R5KSZs5"
                + @"5c8fEZyZg/mHm4eO4Xjuzp3pru2waw/RxgDUp/5e/uOBd7nApTZQxmGQN1tAHHCnwMoywQUlqoKJqBTA"
                + @"BLUtrnejDn8wTsK+sK0+6i6sFQJKSUAhQ/PkHLw+WGZiKC3A2ZQJDMpmCqMX8ZvTjwIP+8wWK2FDswPo"
                + @"zLr2COgjmAUnUY2hAC41PC4JZS12soyqF8Ak4NxRu6s/72EiwnScGLCCNX8FqkNlXBYJmGG8qkYlxCEM"
                + @"/TtZRfiguDhk4nNWU5Yzb1irawnkq/IK10cx0LGeSYnyr+zhN4qUQSZlDgSeY2+DfU2paMXqf/Arb0Sz"
                + @"90LXsktCWSKKSG1ULu4R9PmRQGjk5ciJhmVJkFJoFH3LwuYYgUkfcklUoqKyXyW2EZHB3NVBZwTQuapy"
                + @"KCgPqSAg7QyaMGtB30VLZV7TFsfqarNh/ms2+bHho3zHRRbAojwXCWi8MHMXyg3FF8NevHyAgQ1KOacf"
                + @"bfw8bdAmP8kYsMlPNH5amAD8PzSx5BKGkX4wAAAAAElFTkSuQmCC";
            }
        }
    
        static public string Folder
        {
            get
            {
                return  @""
                + @"iVBORw0KGgoAAAANSUhEUgAAAD8AAABACAYAAACtK6/LAAAAAXNSR0IArs4c6QAAAARnQU1BAACxjwv8"
                + @"YQUAAAAJcEhZcwAAFiUAABYlAUlSJPAAAAnLSURBVHhe7VtbbBTXGf7mtvaCvbZxgktQghyiNmpSqS0V"
                + @"UZW+AIEgqkpJH6JGVGpRKzVqqUqiVk0JSiGxTZCaRinXFAdo+pSX8FykPqRqaVVVAlqogqo0iOAbAeFL"
                + @"jL07l9PvP3NmGC+7NhhszLqf/e2eOXPmzHz//5//nNmdtYIwVDBwbBu+78N2HFNzg1AKjjlGOouiSJfn"
                + @"OiaID4IAFt/l4h3XjStvAC6FyzH6eMu6qWPvJFLxFmyEYYBiaZwCWKaQqSBCBeL1XC5H0WKE1JZzHpYf"
                + @"BkoxSi9/chnHjv0BR4++h4ufDKA4XpTdcatysFrpAFe6RWvrPXjyyfV49tmNWLx4MWvisE+MM2fNUQoC"
                + @"de7cefXCT36qWgqtKufmVH2do1wbyrFc5QphyLLU2bajLMdStsM2QrbN19WrH/1wi7pwoV9JNDF3KA4F"
                + @"FZKyPRdplUqBOvreUWx98RcYOP8RHrqvDZ97YAnCUommsbWBEv8nHjTjhP5ViGwLH37cg55Lg8g1tuLF"
                + @"l7bie9/fhPr6ergc+4rJcK563hoeHFWvv/4r7H7zDXijQ9ixZTO+uLQNLse/SlRfB9lhIWRY+5whzlzo"
                + @"xa+7D6P3aoAvrFiBzp1d+Nrjj+uwt7h/rsIe59geGRlBFPhoytfj4Qfux4OLFqGdvL+5uSKXtjRhyaIC"
                + @"ljU14qHGAtZT8DPr1yFnBzhz+iTe7u7GR+fOaa/bNIBNA8w2Zdoup80oNWlIwxrou6xe29mJIwcPYIlr"
                + @"4a1XXsYjhQbYYQlBFJpmEyEREZJeRGHcGOHwuMzBv23/AfzxxBnUNzVh5WOPob29PQ57clbBa5MZK4GO"
                + @"U76sWr0KTz/1NI3ARMU/q7/vktpF8b87+Fu0Mcvte3UHHm5uhOePcVBLxq8AxY6ZAh1OExY74eQILGjA"
                + @"X/99Fh2HDuHUlSEwoZjGdx7JrLNwYR7f3bQJW1/ahpbmFli9/RfVri6K7z6Iz3gO9r9C8QznXDAORcaH"
                + @"Ca6VdCJUDr0uU5pCiXO77dVhlEuGv3xwFu/+6X30DwzA51DKeqAcMxkPIli8qyddhvzw2Bj6Rz5FrqER"
                + @"Ozo68NwPnoPVM9CvdnZ1UXw3lngM+x3b8WiBng9KXLD4uiMNnf2yBshCBMYifTYZHBvF4OAgt+QCRGRy"
                + @"nAwBUySqJ9Tbg5DnCyw6h+//6RvArt+/i4tFH9/45lPYt3efiO9TOzsp/u1utDEhHKD4RwoFeCFDXmVX"
                + @"edXFW5bDPfG+kNEgucLh5C/3CFnv6rJRn9TPpAEiOiRyPJT8AFcdFxuefwG9Y+NYs/YJ7N+3n3vlKiR8"
                + @"+W7bHiynDsrLkXWw6heQecNsOcO6PFDHY3Ie6cLmMtfL1cHmHG9rA9jsM2alctJmZuhxtslB/hq8hXra"
                + @"dXiNMiSikPlqoKdHdXbtxJFDh9Fan8PWH2/GssICBMVxuWOJTahR3fN6YIkRGWJxK77q8vULnKm2bzcU"
                + @"Z6QSh3C+pQUbf/ZzDDM/rXliDXbv3gPrIsV3UPyhw0dg+0W0MiMWh0bg8PrHZZoynVSDiNW69VYMGf1i"
                + @"gPjY6j3Inqn6vxUkg9HnWQoNHi4VI4xR/Oq1a7Fnzx7YJd59+g4TA3mVY7VvcJhTl8IohZeoYgLZUzmL"
                + @"Qp5gnCqEUtbb9HyR/TB+qpPHzhT1ddlKC5c1ycCIDwaBhsx8Yhbr44Fe1dXJRQ7DXo1exVc+uxxfal/G"
                + @"qA0R8uBrMEcaUJsBPZxppjuVdz3V0LPyYubZ2YbFXOYyiUvW/29vD45/8CFGOB2vWUfP797LbN/fp7o6"
                + @"OvEOFyf3WBFee34LVi5dAlUcQ+heU3VNrAgkzLaUZQWXlZcsKiphsn23G7yt1NaPmOl7P72KjTs60B9E"
                + @"WL1unRYfZ/vYR8gza7fdey/qmKkbmLGbc/Upm+omstkw2S6UsYkzQXl9pXYzynweDfkFWEhd97W1mZVI"
                + @"7DVxAre5YVwpC3/5BCe9MaDh3AqUehkRTBMpbamTfek2+8vs13WzTAay1hLR+/r+QoepaCVYptx4SpJa"
                + @"3y/B4fpePpNzuEcsJe2rMYG2orQnpwxruYjZolwKx73DJbY4U1+ZVGvG+tIa+TxLN8gO8CqQFnOdGixk"
                + @"3ZHYRWDEE6yQe+8E+lZU3g1lmshSlqUJdR17mms0GlMk+uRVR6veTFvF01S6rVvFAqdEYqW5xClgJxEu"
                + @"+mgsUtSyMokNwtjgruRkEL0pkgWK4AYMl6L8hHOFU0GLTxsmBVGe8XxNISNrgudrEWkkULSmVBoDxAnP"
                + @"IFueD5jo+ax1agTiZE2K0pRKI7Dmw/46ZDw7/8RnUPPis+FezjLxmZioAYjALNLtdMzrtWs13t3QKqj4"
                + @"OlXGCvQ8N1MDCLLlGoERW65wnie8ckfTSnIzm46PGoZ89qLvbfUnMPoenn/6rs60qGH8f56fV8hEdM2L"
                + @"T1Jaxbs6PcsZxHVmT6a+VqE9L3LjZSCTnbwndbLzLkeqReszmoxjKd6U0mYRq+R5Gr5PirTbMs4UKp1L"
                + @"eJNI5BKZMZ90ZsRrA1RD9uSVeLtR6RxZTg/yrc61kEgY76sJZDWVkys87kkRL3Li0t2Pcg3ptpE8/+Z5"
                + @"gbGC+Yo68X42CmoZ8fcTHPPyJt/TShDE5dpHrFOe1+LKx4YTWnDksVJJ9BEtI1kiYgaoRImWNJOUU/Zl"
                + @"yZdpM9tPpXMJs20mUj6XF5fKk3byrKArX1XLPm7LzZttuR4cx4OrHMifFdEe/HMsV/9upiJDeVeTsKx9"
                + @"FE6D2eMrnSPLbNsMwxBh6JMsB/KARPydvDLXbysxj2MjcCwMlkoY4iovbFmE8YUNCAvNt4FNFepmh0FD"
                + @"Af6CRiiWh2kMJQ9GUq/ricPp6MHxUB3qfguv/nIbwqEr+Oqjn8eXly9Hno1UUHmhQ3NJHOny1Eji8GYg"
                + @"fd9o/5NAluuMopARffbceRw7+U/4jPRvffs7eOM3b8LyA6VOnDyBLVs24/ifj7MZ4JE5nluGXYKbvfw7"
                + @"jeR6ExPKtjwf2P5gO/buPYBVa1bDKpVk5If4+z/+hu3bX8bpf53m+NCf7+jfycnhsZPjbqZ85mYCeEq5"
                + @"j5wOJJmllz4N8Lx6DmMXSS/yK7COjk5s2PB1bQyrKI8qMTBsruWHBodx6tRpDF8Z4yQgh/q6Ax0BfNEf"
                + @"b900pin+VoTLsTyt67jwvBy3ucH/lStXoLm5WU8D8uMoqxiEyrIielTpDC+ZUX6GIRbTdom7ugXcCfGE"
                + @"1pvpg1rEofpyRBsToCW/f5NIFiYLnuyDSQmSB5TuNmSvuVyVeThNgvt6wbWOzI1N7PX5hAm/otaf35vy"
                + @"ZJABMK3cdwdQPeyB/wHnJW3xe5R+twAAAABJRU5ErkJggg==";

            }
        }
    
        static public string Afile
        {
            get
            {
                return  @""
                + @"iVBORw0KGgoAAAANSUhEUgAAADcAAABGCAYAAABopQwiAAAAAXNSR0IArs4c6QAAAARnQU1BAACxjwv8"
                + @"YQUAAAAJcEhZcwAAFiUAABYlAUlSJPAAAA+TSURBVGhD1VsJlFTVmf7qvarq6gYaGmi6m15kCQ0IQWil"
                + @"jR5mXPDISVzGSWLCIgyiA44LRGhkkJgocyacSXKCkaMkIiTiCtGYEyLrjCfOOCICgVZiN4uoLKJg6Iam"
                + @"t6p6VfP9/32vq6q7ixAndle+6r/urfvu8n93+e9/33vtizpOHETcp0EKfPLF5JSwi6FauQ131FD0diOE"
                + @"oZBIcMnF9ZNOeykkl5L5azTOUhTzW0Jfuio+N5KVbw+55l33+SxRQjXwtPA5TsRVOY5YLAa/P8BojH8x"
                + @"+GxbCwgBLQwRF16jbuk2nEeZ/z/YhTGvQdGJv30+xERfTSMtZZtCLg7L8qG5sQmbN23Ets1b4LdsUpFM"
                + @"cThOzGRWgkmFWbE3ch4k7a8Dqderm0qzWrZmdJImRBV2fo8+vXHjzTdhzCVjEQgGWUQuGh1ILuySs9HY"
                + @"cBbz75uLl19cj9LCIliOA4u9ovrq6GkZwjSmzX1h5ARSt9TntcOO5neMI+Yw3hiOoKGlFVULF2Lu/fcj"
                + @"GMpiqpufkpiW/G5pbsZdd87C2ROf4O5p0xGIO7ARgyXTk0jWW+c1//48ufa/BallOkdSueSFZ1laWpIO"
                + @"njiBVS+uw8233IIFVVXwh0J6XRRVcm3WMuZDuLUF98yZhZbjx/Gv02egV7YfcCJCTwt0BiHnqSo5Usgl"
                + @"TZFUsESydWqPTsuZNEsmphRnHe+e/BQ/emYtLqusxPQZM9RGDCgaiMLSEiWnFkKL+RxKlPVywCkxiuOL"
                + @"IWa5isgIpRGfJ1JZW7r8EEikvXhRfnUQc9lEUoWmgyJ6Uhi3ZYRoD17bshX/9tD38cCCKqx68knU19Wz"
                + @"KlmfCiksszlKIrQ8LimHV0XkavdDtKBhc/WTfVmspBiYQQNLMGpYOU6f+gx7/rAHzc0tzKsjRxIxkmJo"
                + @"cb7aYiV9fvaIzXr8HJEA07g9MLNkT7WYYlgS8sXCkNOZxZCrSLcrGb1Rw4bhaxMnoqy4GNFIGH7bTzvB"
                + @"CSxKxTi0th1AMJCF5pYwx5DEsnrgHDmLNNMqCSldXzqMCXJGugKyp8n6ltBQpVLSvQgyzPH7OQjGqko/"
                + @"W9RXp2UrCVXv2ovXX/sf1Dc04eS5Jmx6awcWL38U837wA3xA62lzVGOGWbdANmyl5PWlG0qyRb1s2bJk"
                + @"2moek5dcgZraGsyfPx/z5s7D3l17cKD2ANat+xV276nGO/sP4fDhw1I7/0zPdITU0l4uBBdeLq4WVFaR"
                + @"MfOqhWb3fsms4rTl9qUhP2pXBw26CLdzf5s5858wtPQiDCoswaRrJ+LKivGoHDEKZSWlUgsCAXHNpCGp"
                + @"yEhH5ZLlfOgsvyedQYjJfisiE04nnTtVPcjIUUhOFOXIWejfPx9Tp03F7XfcjhHDhqK0oD9uuu5a3HXb"
                + @"ZMy7YybKBw9Sv9Ohx5K27S5DegW0w03vyy9OV7e3LBoUGRknGkaQqT39PgwpzEd5SSEC7AIhFhMhSVO4"
                + @"O0GdVQWPqJmqbfDWnLDVtRRzaC0DCNGFsQPcAvw2zrU2o76xQd0vMbsSahVijrodooMn3jdDw0shxlMj"
                + @"J0+exNGjR3H6zFkSasLe/bX4xfr1WLl2LT44dpRHIbpizGpLmClQ1TsS9KAjt3PnDtw5axZumz4d299+"
                + @"G3tqavGTp9bgha3/iZdffwv7DhyETxxW5nWisuH/bYCrK45+/frj76++ChMmTEDBgEIM6NsXl40eiRF0"
                + @"QEeWFSGfv2XqOpy6Enp91b6nvni48y0Z7HDvY64n8tCeWhg8eAjuue9ePLhkCUaPHIXhZaWY9fUbsXDG"
                + @"NCy+fSbGlJfriPm55vT4I2vOky5DsvKGgMaEnJh+1/x71wRms6COOdk9EMoOiT/GfHH0yO6FUSO/jJHD"
                + @"L0YomGWydSmZdEgo3ymSVDTkCHFGdWhJwLJoLeksh3nUC3Nri9NPkzWXmUjucMbVkzFQ9ysSiaCVB9Vw"
                + @"OAzOPTQz8fDpz7B246tY89vf4PS5s5pROkCsq8RFdGaaeroY0mp7IWRsvA9J6nDsr92PqZOn4KYbbsSW"
                + @"bdtQ/d57WLZ8Oda8+CKefull7Ni928xrwqespJaUbbNrwAbPNynV1EkGHT2SkwUpXkdubi6KeESXY0/A"
                + @"DqIgvxB5vfuhT6++uh7PV2lXQ22j8pDQk47E9Tw35pIxeOKJlVjx2Apccfl4jBw6GPfOnIV7Z8xgOB1j"
                + @"R4/SUWqzTN0JMyiEzB43TAO5Q6J5grSIPXvSYgYsZPN3cXZPXH9ZBb52xaXIzZZbZsyXMdayPSEvLTWd"
                + @"K8jsD57rKZfFbER5vPAFsnnKCHGd0bfkqGUCtwQN96Pr36S1Bw2Kl92FbAX+IBp4Ot/0X6/jlVe3oDWS"
                + @"gS6XdrTRO6F9ovc5FnJPyI/amgOomr8Q9/zLXLzx5tvYd/B9LH9yFX7+wvN4nI7zmzt3uVOS48wNXjZ5"
                + @"FVmDpq4uRMLUt4VUwmjnXTWw5L5IfX093nprB3bv3oW6utOoP3MaR499iLPn6hBFhPtgK6LifvFEICSF"
                + @"kCddjcTYCFJ/KbUkpSy58zW+shK/fuVl/Po3L2HS9degcuzFeHjRPPzzlFswm1J56VglJgT/lmCsJUej"
                + @"oKgIA4uLuc9xdOj9l+T3x6033YB//Ook5Obk6BSU07h4M4kx646x6wzepKQ+SYNJJ9885FCSGpednV4Y"
                + @"YwESkk3AZgEhJ5AbtxI1e56kmfRuR5saCX0sOQXonWY5zohz7A8gFsrGWdjYXnsI2/e/j1YSDgaDej0z"
                + @"9roLgyWul9xe+MVTT2Hl40+gZv9+fPjxJ1j70it4bM1a/GTVGux5d59OSSGmN4i6E21+VlKYBpY4wh9/"
                + @"fBzLli3DI0uXorq6GgdIcPOWjfjo+HEcP1mHU6dOMatZc970FHjbQ5dBiBFq7mUL8EKNd9REn+SNG1eB"
                + @"Da9uxG83bMDEa67BlZXj8b0HFuEbk67DrZOuQgV9T11n/HhrtLvQeVeaDpfvRNcLOSKYlYVh5cMxdmwF"
                + @"+vXNQ15OFsaXD8Xdk7+Ju6fcirKiQkScKNecucWgHcgRl4ftXQtPdQmTbyskxIsJlJzsddIlUe5jYmDk"
                + @"aYk8r8sOBdEjJ6QHWVlrNg+ypnhXk0qGp7qL5F1b4Kom/e7elNWB0D1O3mKQWwoOC31w7CPUvn8QDo85"
                + @"tu3X9ZbZFtMbN0NYR66eLtfvt27Fto2v4ggt56d1Z7Hh92/i0bXr8eM1L6D28EfaI3KXxRFryXi7/uoa"
                + @"tPVpgoQk6eMtii4XSXX14zAANTU1WLRgAZbQiOygj7m3eh+eX/8Kdr1bi3cPHsGHR4/rcwQxKnpzVmrg"
                + @"6GnY5ZBWRZFEmOxMmG/DUl4zwcUXj8a/L/sRljy0FJdeMh5fHjYSc6ZMxcRx43DliC+hYvRorksWY17z"
                + @"NoFnft2u6jZ4hBimLBWJuzeIevfJw8RJ12PyjNswZMggFBfk46tXX4Wqu+bgu/PmomRAgfaOkEne5zIG"
                + @"KTp5JIWcxmOI+hw0NJ1Ba7SJo9SMCOP9e2SjsHcuM7Gw/mUaMXf2pDFwavTFAvqZIWhb5hWjYAAR+pJH"
                + @"eM47WncGEfaMvhYhD9HFWiZ9MgGqRyf9rtOytaUFB/b9EdW7/4DPaDk/a2zEG/v2YeUL6/DTp5/BoSNH"
                + @"lZS+HSdvzQknTzIGHdkpuerqvZg/7ztYNL+K1nIn3qs5gLXPrcNr/7sd//32Lhw6/KGOmiCT9jhRRSil"
                + @"04kaxzF48CBMmTwZ3/7Wt1FWWoqBhUW44vLLUVpSjAE8tJbwECvvoajFJDJrUqYHvS0L+fkFmDZ7Nubc"
                + @"eze3hXKUDeyHqbdMwg8fnIcVSxdh1LAhcKKOvoui92R1l3Qlgyh62nhboDfXEHciaHHCCEfCiIZbYdNR"
                + @"LujTG0UUv5dbsqrItmC8Ay89E2HIUcG4zYOonyNDi+mjH8kvhJujCLfoMyzN05kYgpkJdZybm5tx5PBh"
                + @"fHToAzQ2NKOZfGpOfIxnN2/F0xs24cSfTrtbgA5y9+BztKvk5BHWQ4sW48GqB7Brxx788eD7ePz5Z/Hc"
                + @"lq345e824Z39B7jWxG2W14q/+MOqzgmSSREvXTJcIHRa9urVCxcNHYLBlL55ecgNhTAofwD68vjTjwan"
                + @"T04OHWe5IUtfxliUJMlcKLkhJLX4u0vwvUceRsWlFRgxdChmT7sN3//O/Vi6oAqjhw9XQyOPryIM1Ryp"
                + @"kckwpKjEs6daSkZ75vZGMDtEnXkwjTnIoxs2bng5KkYMR4+sYJvDrJ4KvRQVpmUWRXf+6peQcyF2L+YS"
                + @"NRe4r8Wj8FPaUl2CCZhKUqW7QGLtkETOFZ1usq6S5TxK++T/Dox0L7nk1k3MJWdcKT9/WjRNvjiTHe51"
                + @"MblsvBLvsZXcKJLp2Ca8JveXRMTYxDjS6SR+XpF7ogmJcWl0lBjbies9HSceYZ3yjjO1Fs+JcXk5r83P"
                + @"pG5KTn9SuQAzSui3s2jyc3ghAH8ghEAoO0X8XI/+LObhWrSDWbBEAiIBxtlB6SSQTuwOYgdt+IN+CnXg"
                + @"8Uva82eF2HY22w3BDgUQEB1s+RcXqk1iwsSWF7dJVqD/NCG3DCx6KK0N5zBn9p3Yt2snrq4cj6B4LLGo"
                + @"bIaucWQ3SE+oeD1kQtNjkikxOdojvYH1KjVwq1SwaRWvWn2RnBXJW+qnzjRh+8538M1rJ+DWf7gBD694"
                + @"DOFAEKuffR79CgoT5MRoxKIRLF36MH65+inOswgaGxvQJ7cXgpwKQrBNCbdxTXKRHO8Mbsn0cBlpNYwm"
                + @"qhPd3NVD2HQL5SonJ6emhV65ebjjGzfjK5eNxSNCjrNo9TPPIZ8nG0OO2WUowy2tOFtXh5OffsJz3XY8"
                + @"uXIlrv27CbhyzCiYt79Mo8ZXMUh5dYMM03FUcukuJtFuc8c1kF9CjAQ5M+RjxzltWY88YovwCBan3rk9"
                + @"bAR7BvEfP1uFJuZf/exziZGTagSmf3Trw5Ytm7F44QP4UlkZRhQXIqj/MSLNieFw1eGXbg8MTdMS0SsS"
                + @"64DO0gx4JWkuJkV1RuisEBHQ2Ag5i6Om2xeTHIThcJpue+MNDBwyFE+sXoMB3shpIamQWvv4M2b5sJOn"
                + @"8xXLH8WJI0dgtTbpxi7QdtzWPcvkrbe4WFle8/RIhuRIP3ICU5dAs+lPLaUit/mFStxqMTqwu40eTGez"
                + @"YkMcpxVXXzcR91UtRO+8vu3IMSa9Ig84GlubcezIMbQ2NQJci+K5GFB5LWGKeTBp7uj9hTB9laac1Etp"
                + @"M1iW91xeJqZbRgMxejEUDizEgOJiTUyZlp8H0qMXAtXxPFmTlUjOJqN93hakYEoGbYlt+fB/YlgFxrk6"
                + @"bGoAAAAASUVORK5CYII=";

            }
        }

        static public string BackButton
        {
              get
            {
                return @""
                + @"iVBORw0KGgoAAAANSUhEUgAAAGcAAABmCAYAAADWHY9cAAAAAXNSR0IArs4c6QAAAARnQU1BAACxjwv8"
                + @"YQUAAAAJcEhZcwAAFiUAABYlAUlSJPAAAFCeSURBVHherb1Zs2XHdee3zp7OeOdbcwGFKswkAQIkQUqU"
                + @"ZEsOPdhhO+xwv/W36I6W+1Xfxy/uCEe4w3Z3a2qaoiRQxEigCNQ83ao7nXkPZ5/+/Vfuc3EBAnQZxazK"
                + @"u+fMNQ+ZufdpPXr0aGn/H6W2FjVhL7J0saTWftaWtdWcLuPacrZ5srQl97a4mtYta5ctyxZmccmJZcuW"
                + @"SWyLmLZqPUu3kVkSR9aKOEdbi3rB6YW12Odui3WLoPMawGylKX94sLUUBLbQttmvdWOrZXEcc5l9HdZL"
                + @"i7io44h9wdyK1RAngWPp5wSx2uS5lvBsWVWWVlW1RWlmWadroGHTPOc2cOq0rWwVVlphyaJliTqnjfBP"
                + @"++DMOVUvnIQcwBq6ioBF+NXAuqR+U3l65gijZQQgsEkIg6QIVkPBShUqahsKBAIaAS7ial9XJgsQBhaB"
                + @"E0HgDCJmcWqx2obhZVXaEuZbEtmSaxWMnC4rG5e5jeZTm5WVHR+OrShrq8RMaglDi8WC44UzeAlM4kvC"
                + @"8512x3pZ2/rdnvXY77LfjiK70OvaQIA1zNIzgUQQF2JpX2KmEtGO9sqqot+CY+DOMpGWf1VDG+FLn+DZ"
                + @"gvraFxPEkKYxpwMy5GXBga6dMPMbylMyRx0l9CEpqekc5ggsnqwRS0nusqkqYaNukUa2ek7AiHlzMYA2"
                + @"sjizbtJGmgGwXNKuJLdli2ppQ5jxeDq1h7OR7ZUTO6wLG7dKJDWy8UFuVUF7Ik/DINVaTBEVaCNCuiOY"
                + @"kML4NMmsjba10YAkTqzDLRch8Hmurw36tr62Zv1+xzIUUtDCa//TEjVhOE3xHP0Bc7UouIjlSBJrSYjo"
                + @"s0haXlOkLquCIIpJJY0VKKHwdsZxe8p10SanrwJ5lMadyPPXlKdijnCORTzEYUFrMiVLgJf0xAAYQxyZ"
                + @"Du2vJEbSxFnu4YAqxDP+6qiGuaAPdBlmMbJRsbC90dAej8d2hHQeFbntw5yj6QRtQdv0fBpDKCS2SK1V"
                + @"pfTD8wGkZhuOI/1D2kU4CYFvMU+SsCXa2RLB65m1k4UN0KjtwcB2But2BiZto1Eb7dS6tNNWc9wby/zJ"
                + @"qIEjHAl9wNiatnRKwqCywnNVVrp4ukirTpcvP/Hb5amYEwsmpFVNlYjeLMWEIQoYEusCZAfJl1RgWxx4"
                + @"1QrprBA7JxpApbQRT/lTQ7hUdh0zhxbdmxzb56Njuz0d2l005aCY2hIT1GqjVTFVLOWZluwh4ljn7JeN"
                + @"mDtDqEi2zJEwFTI1Zs4LB5LUVdXxEikq2zNMC/6jQBdhXJ8Ht7KOnYVZ56hnum27cnbbtvuZddKWzScj"
                + @"zNrcOl20rx1biXleIIFLzHwKg2JwX0iDgKNy3JE7TLo0CZAkF+5rS6cHjMcH6zmB9Lu481TMkVYkIj53"
                + @"IrSukgt0dcGFECDIB0lzAITOFhBKTKnluIEmorrULCIr8spG2O+DcmY3Dh7bJw/u28Px0BYQJOr1bY6k"
                + @"WwTzogy40ZKlKtpAVVsxx9oX0NJdNSsTp+LHPO8BB0V4q9/TWzVTISlLbEqL++ScI8xYVBUELqGuIVgv"
                + @"XTpn59cGdnl7y85g/rK6AhruXUAImVHhB7HFdOEOaqEGNP2cm2z1qer00JEYF677xd9Rnoo5AkB204Hg"
                + @"oGYn+BgRQSRR518AEPrFhAGcAIy5KJ/1eF7aYyKez/Ye2Kf3b9uTfGLx+sCSPkxBivP5Al+EHC+x6dIw"
                + @"ZwgO2ZkRMElT2lXf+idGNP5GDHLHTv8xviXcDSQOXEMHP8Q0Y5aWEWZSGkc3rUiShzijEbbI0cw5odnE"
                + @"2tXCXtjath+99Ipd7K7ZLr4rQbg8kECjWlgPWXtpixPJOwt/XUj9fMNA0YKt2LW6UzAFOL++PJ3PUQEg"
                + @"8GgaCwxyADhZwiQ3yTBBDhG0w504ZKnyYT63vdnM3r1xxx6MJzbFpywVQoOsCCVBb0HQqIVsgpH+iUHO"
                + @"HNqrS6IxqkJtid1SdlZQ0IVHZ80/FfmbNibRS0MMwexwO1Xk9zK29CUhU0ADc5YRgQqGerkUg3D8wJiJ"
                + @"2eOpxbPcXjpz3n748mt2CWYlRWV9mJNB9ZLnRQOgbPoJYlTRlkyZiiyHrE/YSjjoPlzye7+pPDVzJKGx"
                + @"IiAhDDUXEL0GwBxbO1ZuAsAJfqGLwe0gSjXEXnQTe1CX9p9vfGw/+/WHluxcwJ+seYoh39pSqO10Dmgp"
                + @"fwlAN8dCyO9T1fWaCKigX53QfbpN57/Y91BYxyusmq0LtraIeky4FOO3lAaoLkCqxjQsZR5UxSTMVwvf"
                + @"lfFchBleHE+tDXAvXr5ib738il1bI9qDj85SKK/oVUG3+CE9z2GwBFBF5n4luNqKORLsE7i/oTwlc+iU"
                + @"aKkiz1CSGGELlkBRIMnAZ60ssTbZaELS2cZcLBE8DIO9hz/5P//557aH8+1fvGDlcg0m4+yFv6RSTGIb"
                + @"CpCKgivKOkLNPfAiMBFJxfnVjeYEZuhs0I7wrDZhqzaaP9502KKXJbqNELnVlAXQFgq6qXZKhqYWBCyC"
                + @"IZF2w6BqNof4EVqT2U8uX7Z3Lly0fi/0LcuxJOSPaWOB/+p1iETF4NBc0z976rQpTQD8jeWpmbOgI7Wr"
                + @"qKQm01cSiUxgZUBotiAoiNGq1Apam4HQf/rgfft/b143212zakAIHHeI1vpEfR321U6QWtVgjKlihjOk"
                + @"2VID1mwdDPoSYwMr+MvWdwOTTsqKSf73t4t8o4jiBGu6DsQLx/RCThMS2wXSEZMEJQQsLcL+hc7hNwfD"
                + @"kX1/c8fefvU12+2F6LELoyoSZO5COmc20IiD22xoxnWPXoFKkZwUVObQ/dI3lKdijoCd5TM0BHvdaYNY"
                + @"HPwKxMpybC+S6AShpU/2h/b//OpdeyxTt7tph/OJRZ0uuUZs2RwGFRmMEXNUsffKztz2ixmKoBrGyOnq"
                + @"mLoMHAkFjFriblO+zBT9/yaWhCIBk0B4f6Ib51wzZXLUjahBoy0STZklpQtzBQoIZdIF/g6MwGLE+we2"
                + @"uHPPtsDtv3rnHXvl2ra7qnUlSDCkHA5tEw2LnDkIIW0qxFaPq+DAo9pnZY4iIWX3RPcOdEqoGxFZlce5"
                + @"taFy1u5bTie/uHHT/ubmpzbe6pqR3IECat62Yn9GcNN2YATkinw+otBU/QuU+aK65oBcYBhnVOl76Vor"
                + @"NENLJ2bsd5XmFjcjcEHMUTd+Wltd8C2bcMlS8i0yTssJDjR8E2Ex1JfMVRuT15UWkRJU5GavP/+8/dkP"
                + @"v2vrNJMo6qQPsZa7YYRq6EPtalhKtPBzK8C+pjw1czQyEBHGcoCEQHZyuAwzJqEajef293dv2buP7tp8"
                + @"d8P2DRFK2tbubmDjCBJKmIW6F2lBOwAchInaaEhTQnjOjtOJ86vrDXN0oVb2D8FWxZFr8Av3hNLc/uXS"
                + @"HHvbX7r5yyTSCEalAIdkuSV/o5EFBCJJlOksrYBZGj3ooUWK7uazY2vPc3v13AX7s7fetEuYN8I1ktW5"
                + @"MybmXo2eSGMEhIIoGZvVUM83lafzORIlJEXjU0Kkhjk1gC84vjUa2j9+dt0+ffLEpmhVQT7TQpOKAgCI"
                + @"CtKkB4GFJEFFhoNtUWnSabOikXNEBcniWIeyMLpR+7rFC/vBavuul9NkXe2d3L8qzYUvzsPcU/ZkBcsJ"
                + @"w7h/pY0YXh+7i5QX4XMUtVZoUQvp10Ct2NWDFilmPyNduLjWtz94/VV78dyW1fO5JW6SpUFiqywHR4p0"
                + @"aT/G9yQnuP92ceacwNZULw2cOqEYXa5hOUMjlLlnMKkX2+cw5uf3btj7j+/bpKot6a1bqyLHKDE91Mhw"
                + @"omnXcpAuy6l10Jy4hcqp0F4wJZBXhGr2l6pc1ujCEiQ8J1jR0qVQY3mK1k4x6Ys/oaxgp/juqUt+oBxK"
                + @"cx2rQpuhVwqw+oAqAYEsRYwpEy9lPULCS+WeFgyRT4oR1LQCpnxq9QgNQpNeIjJ9+9oL9iI5UUYqIdOm"
                + @"hF0+R6P3AkdyEINc/DucTuvRw8fQEQeG2VLk1QGSRBQpJDORVUQqSzLlNC8Bln2AnWctu1OW9neffGQ3"
                + @"Dg8sb6dEN8pRNMoM0oTUyieiiPsxCxpVqIivE5K9qKVsXARis2IMz2o0QWKlKQP5FFUNj6g5he2KdkTA"
                + @"GB+k6sEDRFUSKelx8nK/2hQnV4z3IkqoqlNtvqI5zcmmSMJhhIak6FdzTahROKv+dIuaFm/RpBZC2cJk"
                + @"xzm4zRFeoou0H9vlwYb96SvfsecxcSny2EaeJbejYgTjMYu01I7aRLiafghtqpyGxFFWfjcHwYpaE4ks"
                + @"ycaVq5By4tIhDBSqoI6GUebdyO5gg//601/bZ+ORTeLUcjqwZABx6UiDlDwlhGqkaFnPoQvqDVMiJ2Sg"
                + @"izt1MUEnHFueEQc0bCPhcIaJyByL2GrTJ/wULaVcw8wAuDOBEngC8Zo2ETVOcK9GAqCkYA/MUoUEIbNt"
                + @"KnCdVNjMLTF+wS2b7EoTlMiBryL/COa1MG/YvNAfFsVS0gb87Cgd2M1xaT/78LY9mSBQAtubFxYxfhpz"
                + @"CDgJsLrccFkgrYp2VSM5YY3Mdqhtl9wKYhdWtukUDVGjcMaidhtHFtkUYD/45Lrduf8QmwtiIF1zXfWk"
                + @"Ve+o2TnpnT9uTiCY4mie8zEzdQG2dYIfU4JJXWrqNC7gnZLAAiBLtIWtTAQmLTCB58Q3NRs6WB34kUd6"
                + @"DQyS+1Ca+56xePsiNuhL3pQTS7vLDj2x1VCdAoqPH9yw//TxZzYG5THnihJtiTPrJx1rtzLER4JJWYHX"
                + @"lBWUEiduwqkhhR3yF2XDS/KZoktUQVq+oJMWTKAt/EzLPrh+z67fvgOBJI3SKiSDDgHJO1k17Fv+rEjk"
                + @"ByvNaLRhdc2JTV8aM3PGaAgcDBU8RATwMUyJERpt0XOaCqGwKwNm56R94HFZEGOoMn106Fd13ftaAfgs"
                + @"RX3wz0d7lFAGGbUFOY62CSgooCiwMh/v3ba/ev+mjYAnh5aKWntJF5oikFgogbOqod0vilNUzh5FcWeb"
                + @"alKLEFGzezNMkexsgk8p0aJPH8/sHz75FO0BCEVuqHJCRzGmTcDq3wp7EcLLqlf9FeRyImLSyX0eqONu"
                + @"NCxDf9IcMUf7MKwFI9BND0llTqQ5EtkwYBmakV8TKjIbzhxJtfcpBrF1RumCeuRK6PqZioaTYvlWmpW8"
                + @"edsOC+27BY7xPT1bDjL7u+vv2UeHezaVpVE8pOewHiEQ4lhltW2KmhNWPpi5JOpYEr/XdWUV2M0gmGLx"
                + @"DEZlaMy9UW1/8957NiVCKcg1ZnC9pvE4IyIDmgUquyLOSUcC9lR1qN2kraYBdI80QJoA4LIHzhQxSMdi"
                + @"jqRLjMH8yvazL+1x5miEwRkkVqjvUNXwiQb5lVMAfWn7LQvNOXMq+tMhpsyDETkpwcK+4PIZ4y6Ce27d"
                + @"/vrXv7InozHPhLHHuIU/x/ro+dPVYdeWEq0ka0nouChzy8kqFb/XmJBED2PejnF6//n9D2yPuL1M2wQO"
                + @"iWlaY0kHGsUvpa5uPWj2pHHa1IGKU6o5o4s6fbr6s+Fu1wINnmpwlW1NxFerH61h0FgVsLaW9KeHJFqq"
                + @"IsqKMdIab4g/CFlgjKqKjvTA76HQlaMiZlD936pvaQ/nNFAsXFr9jk8uvnfrlh1q9rXbJkFVnrSC6+tL"
                + @"pPl/H7uCysghEisE8CM8KJc15flf3bpnH967Y/VgDY1CKtKOddc3eZhIbY454l7Nq5/QQDuCXPXUfnDk"
                + @"ziI/vyKiE9C5RhsNU9ynrRiD+ayjDrfg27g1gjkyd8HviBDOIXBQ9KN2QvuhqO2wOQHE6++hNE1Jc7Qj"
                + @"bRJ86koheLfTtxkCPS0L653Zsfdu37D7k7EN0RwciDPuBLSvFLUYaSWNH4gp5DSaAEshSk/hMeXueGL/"
                + @"cOO6Wb9vx7M5iRRBQ69v7QEJJwRUyzGdaBrBia1yGvdmPzBkZb6kKlQxRqbQiYrJ82CBNpV3u7aEuiQf"
                + @"EIMQGWw1Jo5QtKWQX+1J6wMqtEFddShgGlEW878A6TRw37LQhNxFJZrBBMdbmqouXYMlQJh8/HJKZCYf"
                + @"MxnPrXv+jP2HTz6wm8dDX0EiWkuWhAGYeLsyAtrV4hT5WudyC6evcatJUZDnLG0z7tuYBn9BPvOwmFjd"
                + @"6zhTWnRYoK6T4dhnJxUMiDE4IIdb5WTvK3QICaNUXb5EBoZIR0jAkJhAQXmVAoYILVFdsL9QjqIcAq2p"
                + @"MAVLJcMIiUxbVRU+lAIeJHZqLyFA0b10Rvs+ja7alMCmRsuesSgV02CwRES5jugYqCqrQ/uCiQAgRtAT"
                + @"0ocMATsiHbgfFfb+/Xu2T/7jskg7vrSLZxMNKsMw5Zp5rkRVhbY0o5fLxKENMcSYHM08Lv8QOxlvbeDH"
                + @"RSTlJXLkqjwkKXGYxI5T1QkTdMXL6j5nDpoDKGKO+w/XHFVAAdol2wJTqXmRTrtrHQKOWsujZjNL4UKP"
                + @"pzeAoY1AaOGgrxdwDdbzEJ9OXRoV2inQcOboZHMBSE7r0bctakogr3D0VjlQ9WNtQbWaEVdrkBhhj7E+"
                + @"Exh6Z3RsNw9HNuY6IPvIS4LVmqMYOVXq0+lorIaiQbgCxiw4TOOOzfD2svfvfkjYzPXSGQPhXOcCYuGv"
                + @"EBaqDVRNbWgQioBUFfCCVsM3yt5c1GBKoCRVITYBAFInH+bTBGhFrMHFKrdMS5OKmW1yfgdm9KkJMMmk"
                + @"agntUowRstJgNelwfKE9AZzTgH374iCrinpNc+pGRkFbaYSwq3Ot6kGfwa2lEYsEQYMRe9Op/TP+5yEu"
                + @"w4eogFOaU4Crqop8OJYFkkmqkMSUSEymoWJ/f17YL69ft7WzF2xGNLZCb8WH1dYZ46CsiMFuuNCUFQO0"
                + @"L0KJMX7gJbBWjhHGcFlrlzPNtBItlqNDW44ObKPO7SwJ6XY5tV2EaBtmZBo28VEJdYQms3Xl4I/zWsGC"
                + @"skSHZdVfuDdsn72smvUNf9woUJzgbMtZYR18TgJTCsxxVWC60h4Rb2I3jw7szsGTk+XEWvSStrOwOIXn"
                + @"feDVF5bTcCqEZdvpoEJrfn7zupXra3TUhmAao5LpgZTSFAj0pergURtCnPDiVPnCfDVIiZh6EkGQuayp"
                + @"GoZPUiraFVdTS2aH1ps+sZ35gT2/OLKr6PF58rANfI9N55YjeRrBCCLcMEhN659Hck31sytifg1w/z9L"
                + @"g+YXuOhcc17WpSZQqBXBaexMuHGuRJBaRcuyZdeids8mBAQ39h7YwfAIIRL+UiyiURin1spFBXMUDMg8"
                + @"EC3FqGDK9nAysw95sHvhPDRYYD66dCwNWDEGr+P7gkagsd9ALO35avFL2goZGdlwxH8I6sBrS/sgFcOc"
                + @"JYyJq4ltxIWdjWZ2Zr5vF2dP7GUxhzysi9nND45t9GTf5pOpmzJNhrVgtK+ZXmkONQwrCArBxY4kJ5x4"
                + @"puJmDEF2nJpzoV92dAJ8fGQFDcgJoGR62622JUpCtQxsfWD30JyHTx6DM4rRweQBYwVT9LyYGmnBtaRO"
                + @"w94aJSgnlV2/c8cmncimGqNESpMlEZlLiRiirSAJzAkKrGb9pBprbmiKn6Y3aY3+cewMcikXY6Q5tCLE"
                + @"wFQj2S08aDcubae9sAtJZReWY7uQH9r5fGibJMopJndycGjDx/s2G2G3m5lKjWetuhOC6iwIyyl4KLr0"
                + @"rEU4aFRcS3ETzJX6LDE8wkOWW8K8aBPcaCEL+wlCnyxgCj5VVGuhJVqb8ODRQzsaH8NLRabCn6vQQ4GO"
                + @"a46Q07Sq1iQfHo3t7v6B2XrfhiRQEabO1yrTgRP2dD2NtOitP6ttU8KeznDvCXN1U/AzbtYUaPC/XhS2"
                + @"mA+tS+y4DVN2bW7nqc+npV1czqwz3rd+PrcE+z0nlJ8cH1uBli+VBCsHkt9Sd+AEp7xndRuAUvUzzrCV"
                + @"f/xSdWi/rv52ccGUwFLd13BbDXOkOXoPSL4PbKzVIRJDg3Sf5sj0QoAESSF43O3Z3vHI7jx4ZCWMkVn2"
                + @"NxiAXb1GeoVDwYBBkhEtf4wkPpwUOKZNq6aSyDi81MQd+uN+Q2NKCvTJQzSmfTLEArelCWKAzJ5GkqMl"
                + @"cWQ0JxjD/LSmdEWeIntMO9JK5TFaMBgTQm5OZ/YahH9zOLIfHR3ZG6OhXZwcWzYeWk7EVskEklelmFll"
                + @"2Oo7giLZnHMV7Wh0HGGqaXtJ4hfNgHNOVwgehsX7UyS4bJHLpRNgmoFCQZurMTrMq+akFlBZaqAF85gh"
                + @"T5Ld32qysKCCU6wpDtqGdBoudNZKSEJUI5vgA8pamBjJxHpzCxJXzeZCEiLmRdK323XHbg0rG+vtM81T"
                + @"5dCtYYkzR/ZQqqQxtM/zwmZEF8ZDLd3MP6VILhoyf2KOA6vGxByYYhpeYauAQhKMBERoY2AOUECEZYoW"
                + @"tiFRSn8wJ9IMFP3Wagtn2SFhu0Q+8zZM+imRy/f3H9tzB4/tTBrZ5lrP4k5mZZdsO24TPss+I32SWOxy"
                + @"Z050V4iwwIqALYUPVEtzailJhMgaOZcVUE6kwdUswLSEOZqqCOEvWqwJOpjT8iqJF3OA1wkuk1vQXo6G"
                + @"6BlMlsyWuAAlYo1ewBxRTPeLGkoHlKoqv16Ae0VfEly9z1MsOzbsbNsegnBIUupWZSbtctIgBNJnJIae"
                + @"bTKvbYi0piRAJRlqjIqJJz5uxo58Q5XhtDoL7ClpK4gJUGlHhP2MNazi2bKmphPyI7J68iartPpmYFW9"
                + @"gzfZthyCl90ZwD+0rdl1e3Fy034627M3WxNbW07wdbXtbaxbfvkFS7/7Q0vf+qnFL3/PDnp9G7XGaNtD"
                + @"y1uPLEoeAMeeFf0DKzvUZJ9sb4i5mFnUm1s9QCv6EL8HnFmBHS/I1ivMIjnShCBnjgBWkNCFTShiIcSs"
                + @"lOecaVSNkCPAiyQCF63t1hKvjmvt76MkWUbQNce8KWqDrdIBmTUFBM4t2Wi2R8MhzBm7Myo0lUoUIX/k"
                + @"0iaV5yGtK646AIsfWCZz2gHpGqRJnmLC2pjAQmMbGkWuxBwhUmOGYE5Rblm+3IKIsVXtMeH7I7uyuGVv"
                + @"V/fszyb37NV8Hw069vdLW6++bO0f/tiya69bduVVi1563Y7XBjash8jlY2t1j6y9NrJkfWjL9SNbwJw6"
                + @"PbSoO0ZKOZcRZnemwDpHEWa2iNGS5ZQICJ81Iyod92BSYE5LSy8l/GKOGBKjGSlVs2ZoiKI+z11ktmU2"
                + @"ibrk5Z61yPfESWrj+cz2yHty5TREzhBcV2UOFcaiNdi6vaNDD/08HEU9kzSzhbJ0CYmMJFXmyl+kwqQF"
                + @"IDEbaJ8mwWJMmBBbQJBazFMfMhNVD6J0YCLaWBxYG3O1u3doL4/n9iqSvE2ENizHdgyQaXfLLl553a6+"
                + @"+Lat7zxPA0i2t19bl4RuGW8RAV0kYbuC5lzi2q7V5QaBRN/qGUIwJck7IoQdIhwjTO0U4PFJLUxHAiPi"
                + @"WitWO2gB9+IIIq0SIoXwsBi8A4NksjT5py2OWtKMdsm5yOxpvF7BzLMWLVKUlsyJQPcnIzucyfyJOQps"
                + @"YL+HtZTD2dyeoFoR2lJpCEFqRVWmit3CTuscxOU4hTl6RUMq7m9ZE3mJeFq+7k4Ws1J3xBxFLTCn7Dlx"
                + @"ooLEcvLQLhDf/+RwZj8mdH9lVtpAwxY98oCzZ+3ii2/Y1pU38E19TCvhPE5cGr137y5hPvlP+xyadYV+"
                + @"L8OUC7aYwZx8h/Z3AW8LU7VO/LFBXbc4H1hSrlF72PguzKFi52tMbZl2aQP/hdOP8VuOnvsL8FV8nGLO"
                + @"9JKVAiaK/Gwkz6+QzL3J74E5YgICoYh5WM7s/uETTsIPaSmwRFqFSJBhh/PwDqYG4TSco3s0OqrXxjkh"
                + @"gXLH6KZNDAdmRwjiccT5kEzKaCriaOWEkfihls24cYatP7Lz0z17bfLYvtsq7Qp+pwfC0yK2Ituwjavf"
                + @"tXNv/QFm7CUYg2NW+6h58XjPHn/8sU0+u2ntwyMb5GPrTPZta3QIo8e2jsSdB25YZRdJYs8hjbv4zO12"
                + @"19bbHWsrNEXIClRjjC85ynKbZ2hEs07bXwGB5vh+x01qouixhTb5Vv6Ic76W2rULdFV/H4X+oBSBEqat"
                + @"Lu3R4YG/be7iQB+R3jqbIyh6wWlGdCWnFMMgPah38TOSpYUyXIWUYFHD7YXe6lLEkSsiE6RyZPrGQIhw"
                + @"silREsFFTN7SIny29Miy1mP7YTyy/56w+i0c/lpL0+Fo6dYl6176jvUvvWHx5lk3hSVmjuTFFsM9O/j4"
                + @"fRt++KGtHwztKvZ5Z/7Ezpb37Aftuf20F9mPkZDv5Uf2cnFsV6Opnce3bNYz62FiO2iBMooFjCkxs3kf"
                + @"xvTxQR3ML/5EaxUqGCU/usjkUyRc4Amu/poINUSmEFAGBGFx7XINe3YOrfx50ukSKNV2gLAV0HOhfqBz"
                + @"69Gj/eU+OcEvP79jf//5TZuvD8y4eU54u9BKkQwbrrkFohVl8J5xU5MSq+uZcVBDTTxpgbbMm9YRRzAm"
                + @"XsytDcM7VWE75Ct/ipl4DtM2wSSMNJRx5qKdefl165077ya9KAgwMCMZ9+/d+I3d+/Bjojez7ayDKUWL"
                + @"tVaZXEjloNe2e8CRVVwr2zZGWKo2Wxj/SAv3RGfum6JJo3ZiU2qRyRSjSYs2BFZeJoZI67lRpkIMaLTD"
                + @"LcKK/o2Eh+SQ837JTUlzw7cr8v9aLB+h0dPxHsLXsX/59ju22UYwiIpbx0+Gy89GM/vZp5/Zp/sHNidr"
                + @"XUCEHA4qYdNSnsHaJki0rVSoXE0I2TVZhpYtSMaQtFgTY70ekRHRj8yDvmwxOrIt1PRF0r/Li5ntzse2"
                + @"DfFLkst684ydef0NS69es8XGpscMQjOFbfmTe/bgo49sdOu2dTEtG+11/IS0F42EOGmFJqIVUwg9xT+2"
                + @"cegdJFyr9rldeZzh84EB2PGPWqhyjPkaYorHCNCUHO7+KEI41myykVm+BsN5sNY4Fn7UF14oyMGa6NV4"
                + @"BUQL7NmijdDByDZ2J4Nx0jKZ92cpSUqAg2Tki2MyqGN7AR/+P7/4Xbu02ScX9HwIUsKIKdKuWB4NdrXS"
                + @"P32tQmuFtcZAUl1LI/BBSiAj2XLMX8UzMxgwLaeWI7HVdGh2fGyD46G9SEL7PYB4BQKdreaYwaX1B2ds"
                + @"6+JzFu/iwJF+Lf1pkSctZo/t4NandveDX9p87671cJBrSGZX/g8t1vLYOYIClIi11rPlPEp0B0OT1sg6"
                + @"yyGBxcjWNf42GdrWONSL47FdGU3speHYXjsa2uuHx/Z9iP4yQcguvqqDKWlXuS+XVVqn19hnaKc+rYLL"
                + @"RUmUb0AHNMszfUzhIithDhefqUBoJDL4cQRBzEbA/FUTmTRpr27QDNwIYPXBBg1vhxEBHDKMUK6jCKZV"
                + @"Y4zkP5BCvW9fELVUyzVsds+KASEm0VmaTW17fmQ/2MeEHc/snSH2/+Fjm5PYFlq1E/dAas3mSO+iCwA4"
                + @"5Qytih8/tPL9j+z4n35l889vkabMrE8+0UaKI8+dYAZaulwiIOQhVZJjouTYcxt3cjsiyRx1KpLbBX4M"
                + @"gwPdasRarziWRqS46FuXqG2bKO7CtI/QmL2B6XwHYfnB8cJ2ESIDt4WmFmG8UnS9g6NlYgrKtNCyi3a2"
                + @"pU3IU75Opq+XRZ+1iMzeDNxBq8XufJazC20QRhyJ+VcyJki21hB4NIa0ys6KOZoK1jsoCYgnbaIc9D2H"
                + @"uzOisUJBAkGAplizIrc+oeCVw3174/CxvXi8b2fxOQlRyJyO9gkeCtR4hEQeHR7a8NFDq/YeWH77ho0+"
                + @"vW6zm7eJGya2s+zabrph6zAxlbnJCUYgUkqmnopwEh0JkAQPRhQQcgZsRaJa+9vNBQ47B4cZ9xSePMtQ"
                + @"JWhhYn1M0jbcO49plPa8OS3t6qRAywgi5rnpE0QdvcDraQHkQhhjIssUBmH0YBaSDiE1WvLMBRgVAYo/"
                + @"/kojezNSGq2j9rG8owfHy7+6e9/+PSal7vStAIESYvr8A9Ku90fKGkD7RF4AuiSxK7FcFUgryujTepfr"
                + @"Vw727U1MyHminx6GTl/52P3OK0RgOySCZvv3Htnxkwe2nE1snTY3CHX1GqOEYqzFjPiQNfKHrnJdiKCF"
                + @"HMt8gvcqLc60VgChQZrlEzQqOIcR+kqVXurSaxXwx5p1iL7MFduA8wdLhf0STAiA29ChFQQOc4X9ii3I"
                + @"pSYEJzMS0xv9tt0cpPa4HdkRDbU0i8l5fTgpohG5AJlXmXd/8VcS/20LNEiwPooMJ52hLTdmtjVc2J8m"
                + @"m/bn77yJBk19DA4NWKANSKWwoPrcCIgpEdK0sYDyl1eRJ4WRkNW60KhDoraNb7h6NLIfHUzs5YdDW987"
                + @"ILIiaNhqW/bqJYsun7P0uSu28+LLNkbyF5iiASaqfrRvi7uPrPP42NozggsS2VoRVIGDnmMmc7Y6xhH4"
                + @"gnqeXdYKQJSU9D2p7Bb0QUCQ4aSVErgZV0SpsTCF6oTZXjGDVTq3vD21mWpE4AFnInzWGoJ1cZLbtcdz"
                + @"zHBuryAU5xC4tsbhMgkk/aKdFd1KICPysjYCmhQw91mLDBSmUoC3sFB6STjXaySc1xsIbtYWEFyfyJKq"
                + @"yqwpEAgf30HRFNMDUDLcsNaTNTJx5JocY3t5YM9NbttLk7v2nXzPNsgdWtuxld02tn8bxp0Hmy3ZFZvh"
                + @"D+xyj3YL62PmZjC16CEM5Ckt7l/Dv2wecw9R44Trx1Fhh93KDvqxTTuZzZLMJlHKFrOIFo2VkwCnfECX"
                + @"NEBV395RougzuoR/KVLZJcLrwOyMUDtaZOhgZnPaqTDJGeYSxUUrsRKoa2tAAk5w8QL53utjTN0h4f+w"
                + @"tDb+qIXgLjHLS3CvyeN84FPDxs9aoL0iQmcOzBC9S62+oUgrA3M4CJ/lQvR0I4xRTC+maS6nhTPszyD4"
                + @"cBNicy/J3qDct+fGd+za/KG9tjxCk2b2sF3a0aBHmNq34REm54h2QEbWY7+FhkCIPr0VSPNhnwixI+NK"
                + @"nzj99nxpAxCuIfy017KjtZaNBjBHITOAz/BteQeDSb4y00CyMIAZWvNOLOLhrX8ho2FOBnM6VZtrBBaa"
                + @"NkD0S0w27tZ9R0cOFzO5wJ/mJKizAaxDwy5Mp3btaGzfO8rtOQKaHsRqETwsC5iDttS0oemRsMr02YrP"
                + @"CIj4TnPNmykQCcGY6KLZUf88ipJAhcc+lETIqmlrbRfcLMblhLt5C0RocI65eZJ27cZgw27gp26SAx1h"
                + @"+jQcOCAJbMdjQkAc/q33rLz9qcU3Prbinz6w9akSzAwpb5Oc4noh4hCgxikahxYVOIXEtQvNJHTfgjDr"
                + @"+A+PkpRdxZg5T/yI3ORJJVkyY+zLhfvApc+wknfhR0ohDOx4TforbQ0B2JigaSArHxIvYBOwZODT4TmF"
                + @"sProknTsOczoi63c1o4e2iCfWZdcTkncQpOGHXI1jVpDW3oM9WTn1Dld/0pVAaRAZ92LtQIDnofO0GGC"
                + @"4Cnlcubwz5M3RSD+mp0eQEo0qxfmZqgAXGmCqs09+qZARgLXWbcH3YFdRw4/mM7tESGgXENcjdkOcb5H"
                + @"hMX/bOUH/2T2q/cs+fgz6480rUCgIYKIOdYmkeygKW07RlumkVZyzpD2mW1CkE1MypqYo9AMKSqlWS5q"
                + @"RJaaVxdSHCuClAjKH4a3FzRwQ6gfcT8BgybJskVhXULn3jwkkqITiQJmGw+KdvvcPqhqpKNDZHgBOJ4n"
                + @"Z3qB7fqcNAKLEWtuSu8Qwhy9Ts7tXsOfsF0xyTdiiGir2jDGCxclY/LlqIxbATHIMNl6pXMOH4SReKTo"
                + @"0EM3t39q1Z+WJkmaxDTkFr+RYZuzDvuYllbcRbEG9jBds1/2N+wXrZ79sujYXr5myfHAIkzaZFjYkV5N"
                + @"nI+QYBwz0iBp1vunC2ELM1Ibw169Eq6lQPg2Hz3uQaUBYHR8alpes7sYo0UjklMIjZnVSiEFEYECiqDI"
                + @"S1oFERpVY2aEbyVarK8FzohNZR4LT5o7+J0urrBvueZnuG+hV/AhepWS6yQTnpsSDc5JD2b2I8ziGyNM"
                + @"8d3PMXvAKIYu1uhSdoKi7hvC+3FTdEqC7idX9VQRw9ykKyQXk6B/7KZMWq1TGhf06zh5etCLuY0199YV"
                + @"GMj/xBBzaYR7dohdnHhIK8eIkbBi47yNnrtqNzfO2bsw586QLG2fEHS+bp0EJGg7J8paEvaWtFcgzfpg"
                + @"a0kU1Go1GT4mk1iF0JLwHOZERGNLGF/XPRiq3ItUkn43qpH13cwlOHyojv0HygCvsnfNWqLhFVJd4teU"
                + @"+8yIKmeYCw31jGHOmPRgHvXQpT4SmtiE3G2mQVGiuSqZudnK2Y6o6/B+a39uP0bbeqMDq4pD4AS/xUZg"
                + @"jjRCfYuYVOfIqjTMEIO8hkMvLk+r+1WlGFStOU81+qLrfgy3UojGKTpd2fHwkIZqfJYU1SqR9BxJr8q5"
                + @"LUja9CmRJZKd2JZ14gvW2blqo7OX7Gcw99+3l3Zze9PG7Q20C7PRGtBOG2RQ3giXTJDRlRMnxo+JorQ4"
                + @"IyOy0sSfcpa5iEpwkSPNMkltzJWPfqPZmbRI80owuOXCk0GwrqUkr9mSEJv+kgVwLdq0S9SGXwkf41Pu"
                + @"hrGA0SJMWCuHIGrBBoKRAlNG7eCbUtGBm0o0DoNg82pof7jes97Hn1g2wfRq9lFSLzqdYtBJ9RPsipTN"
                + @"/pcK9zhzlHSBrzRHK4ikIPoeqTfBOV0ipIypdKjkk2N/WM/JpOhZ7qoxt9DAtSzVkDrR0FInZ4Sph3KW"
                + @"RHJo0Ph7L9nfXGjb/z4/sOuTyvKyR+yO+C7FHE1eaaSaRDXHtsOQpISoaIuiKiV2JZI/bec27CknmcH8"
                + @"Em2CWBollu9RuKhhJHcCglVt94FnAFHX0KoBjO5Tu0RpHY6J3HhWei7tDN9BkJ8SarQLwxJMiL622KWf"
                + @"XoX5hmewldA9skdaBLJu9hYh/h8cj2x5/wFk4jkEY0XklQY5wZtyojGqkoFm3+9vnpGKaJTfAxkED3kj"
                + @"6oU5uleXdWMb39JDpfwDDEiEhCJMDYSsWKtaFqh2jQGHt5ZgGjLCyYyHYxCqiazyAjudYxKgVXxx157s"
                + @"DOz/mhza3+LU9/vnyF/WcbrkHh55ERURGZZ6C1iDVfgODysh3jKeA9WMriEK/qPQQCNOWqtn9A6qENIn"
                + @"LDX7PORwLpOrYQGP8cmqqZ3lFDM4sx5a0AFp/6Sw5mUQc70H1MUsrxe5a4m+sFEhnMqhYAuCoHG0hDAc"
                + @"gWOLfFlnAzNbHthrF/vWOrpri8NH5MT6aNmpIvApjobvfFFFSt9vih/qPjEVzrlWS3MwZb2MfuVZuB6Y"
                + @"A3A9NCcRhxt19fCOrWJvrQFrzToILEBiayvZRRrKCHVjiNjKpjBwaMV034rZWPJonWvP2+IPv2d/30vt"
                + @"P94b2Wi5izSvEeSkMCW1Qw2hkLvoGwE1UGqJlr4WaEh2QpsdiKs1YlqJM2wrz0kJc9s2RHP3QeoxPuxh"
                + @"urQDfMuITKpcDgH2CYA/pvcDED4GxgkagZkCkYiwfal1yghDl7wqA05tNeSTI2j7egvNVwppOp38KMdM"
                + @"jCLbXTtvB2hMvIvvGxC9bSKwD29adXxAP02BTiqn6O/F5ftUPSnc78xB831pGhTQ8jB9r7urF6GVgyGE"
                + @"/q2cFIZ00bMVc1xzeNbb0x/MQj/btG6M9KM5FdKYl1MrcdCLesRDRFxEPP1+ZIM17H+GSchLW4Dw2Tff"
                + @"tvnz1+x/+/Xn9iG5x2Fn0yYJPqjWi77qi2gJEzVXtKS5e44VOfqnR4jetFZB/qHAjMzQ8CGG7ggJvx/3"
                + @"7DMixU/wax+3t+wD2v2ot2GfUm+sbdqd/pY96m6TGG9jmrbJy7btKNnC0Q/oS9MdMk1CFDgIQKKK4AV/"
                + @"FVazwCMswlp73ZbDyjYTUmet2kkKe2G7Y9H+XSuH+9AFcE8TXgRf1VVpCOn3fOU+N4euOWiJmANT2uDp"
                + @"X4nneuve3v7y0ayyX16/Ze/evWfzAdrR7toUc5QmSBDSVM8I6/Axam2hhQ9KyzEjcqS+CrImetMgpCIm"
                + @"j64QCRyxLzyEuDJjcZnb+Po/2ytZZH/c2bJXlWu0JrSDVqEpE8xZG+3oKPv2N7MRFo61AmiJ7Z9i80dk"
                + @"6jkwHND2v/vssf3ySU4b3INJasVEfhkCQbs5yWa/1bHdeGBn+j3b2WjbNkKznQ1sAzgvoAHrrUP8URcz"
                + @"2yM6JOfKFdA/Ab993IlMdh9/2sL/0DbmdVTftuhsz+5mW/bv3r9hHyNcW2/+1DZ3d8CPf5gmDb8oudUH"
                + @"M/T2Q4pQrRgnTREzlOw7U9CMukBTMkwlGj4m2X1lbc3+xVtv2zaBSBc443/9F3/xlwvuPtwf2r2jQ6t7"
                + @"5AByUBBG69c8SOC6UjnnuEZrYQoc8bBV+z4rKsYo+uGMf/pEM6RKMkgcqzaZdSe2zu62HWNmDh7tY7bM"
                + @"kp5yFa28ZF/rEjiXYuo0x7yEsWKQxrSKOaGxjF+7Q461QTS3br9CYR8h6dFg12LC+VafqHAdQq5tWHv3"
                + @"nNX9bZuiXUe08WA+t9tHQ7vx6NA+fXjPHhSP8FeaEiCIWazjLxEIrV9D8Fqc948PCVXMSuI+kdC/JJUA"
                + @"PyWiC9q8MybY6WxYt4u2UXwckn8aI/MXoCX8jQkS2bw0DPITfg0TiZbIsMdo5g7u5a0rL1gfYdTiqPhf"
                + @"/a//5i+1vHV8OLU7BwdWkK3PFbH0kChuXmCetHx2obkcgFeyGCMZspPqWQtCFnA6jDVRRVR38GgP/Tsc"
                + @"AKAXbTUvEvU7NllP7FfzPf+e9CaSs05Cu42U+noF7hWacvwA4AMUmgIghSHq0vLYzKY46veOkPM6tfXd"
                + @"M3bm8kXbudCzrYsdWz87sLUzA1vHia9jBTowLRogJN3MSq3s7yT2eDmyj/bv2a9vHdoBkUW9uW22s4Wx"
                + @"nNtgdIiz1/o7NAF49bqlpjY4wuxE1idE7wDDr/dn9hgz3ev1YZhGlGGsxiShx2okX1rk+DfV/Qw1bLkG"
                + @"jnouJfrrwtwzuIO3X3iO/FC0XKA5//Yv/hLDYBWScPv4yI7RAGXlmqvxV/l8boTO9HMeeE+tPtHCQn8f"
                + @"h540l64xIfduYkoj8R596T9Aaq2br1rpEAygJVP80xIi5tw3PhxD9MQu4As8YgYuDcCKGJqykPTFaF9G"
                + @"2774DxJqLdu7hxPb43hta8t2d85ab1Bbt4MpRtA7A6JCtKzX7lt3MLDO9rr10No+Jqh/bse650mQz+yA"
                + @"54bdfzy3Xz+6b/emQ+sTBV7sZNaDkUtNn0MsTd+TGfnLZR3lTPTZp348quw+Qj1YI3QnuPEBYnyiT7VI"
                + @"85SbSWBPaw7lhEFcUqpSaXkwgdUminAFXF6/dEbkph3o9q/+7b+BOco3Irs7mdi9+dgitAZxkDYTXqNg"
                + @"mteGGeiIN+4rVmjYfQvkkhnjJHCIQeL6ytYqkZLDoyHMoT7rtaCmKU4VcxF11myyieNGAO7PIQG2fg3E"
                + @"FTmmAKev0xYQRx9AzRK9ikLgos/Vo73vHk/sPlFkf20XDTlLH6hWXRCkSCIUBtM+1Rfpa0www0SxTYn8"
                + @"YkxRTN+d/q71ds5YMujahFTgNtHmZ+RhEwRug7C9pwhRaycgnsayRacwYhLZ5zDqYYv8DDeQ9ehPwYUk"
                + @"nv/+ng0C6e+rBjKErf5QVwyS5sgURrOZncPPf/f55+wSvpFMX94AGmHo0WDbRGJ2NjZ4EhI3airu65cz"
                + @"NACsDD0iFhftF6i5VinKD0lTItRBS418XJpWXfKpASoYowaodZ37+54ZjEnrNSva6zba3rbHly7bR2z/"
                + @"YTS3u4dEbiX3YOLaeqMY06IwUvZeX5GyxZTwWHMvtCnfCMHiFtEWDjyxdcRjQGSFJSgQKg3xwyyf+sA3"
                + @"LkyvkRQ2I+Ao6d8GW9Y6u23ZxbM2eOGS5S9ctg83BvYfCZ0/3h/bwUifxFhYB4b6K/QIj+QPYtju5rpt"
                + @"dQYkWsChD+KJTg2xZZKlPWLCihEnRSQR2FTdE5HDqckNLNWFnU0fKdDwmDQDzEVi2uOmDaKFVIsInTHe"
                + @"DS0F2yit0LC91hbHOdKjUQLMi9O+8SkydyKYM1CxBIQtNR2QJT7c347Ic+YgOeOZEqEgWtH73iXIzZDA"
                + @"exDqP2CS/hY7/0mrZxOF7/ikPiYjrY7A8QhNmtkYmKYZUkn7yISvHcgjheIaw8Nk+isabKXpMMbf1yEs"
                + @"rwFqWcmIS3MRJOBGESwd9CyDMNn2ZUs3XrPx7lX763ZiPyOHekywkxHMZPI7EjKboBljW0dbBliAfDwl"
                + @"v1NOJiqKXqIl+zo8zRSVU4zRNoTMRISYxY3Bug1QQP1EDSLvg9CehK7a6HFTT69/EDnI5uuC233MjBij"
                + @"dcLSkmQOgzCDYo4CNH93RYjqXu5ULLAk+tIX22tpGQyq1YZ+wWNEnfMczNHQfR8nEyGVJSHv9MIZu3Pl"
                + @"gv0C5vz8eG63pmhbRV+YiJZyqUwvOxG9ycZ3idz6RFiYOb3rpNHlpQKWNpASBWacT7T2gLb0XYWWPs2C"
                + @"ZuM8QZ6oTvg48jAPeJVHLdINi/tXLb70ug1fvmYfDVK7NTu26XziL2/lJVKFcGiyUdoEiy0fjW0+loaJ"
                + @"WKIimtD4nhPNUREfmt3VVrRVZCf/uNFfg44wR2sDed41RxNpJVGKfMIOTv8a0hgfEuq2MCFLpJF8Rovq"
                + @"1LO/rCtz0pgpVUmTZ/c40/A1c7RIG12G+BF+QZ++EhNLmRaiPh0IEJlJvcCqXCqBgAukL9rs2ezMmv1m"
                + @"I7W/XRT2V7PSPrFNG8ZX8VnXgPkCKX0Ps1hC/KGlXb328cQTxM6iY51Ki9cxczKxEHAJs/QbOP4bP8JB"
                + @"zHAcBECYN9FaMS0oh820S7KJ0HWic1Ylz9v19hX7uW3bR0RmB3qpNsYko4Ub5Gdby/vw9pAcbG5zQv5F"
                + @"KdHXQOuC1IA2I31Tm/7UDxKg+asauKAUmjG1dkEw9OSxnVHudXbNXYW+GDkj3YBFgTmVBgOjkTPnDdR1"
                + @"G81Jm+ETAn231mKOfsrEQ2invLYyYdKUsBXFV1/m0HCX1t3pUyOae5Ezj3DItX/YTYzjPiWbOc9i3hR6"
                + @"x/qZlkHbkgtbVl45Y/fObNi7RET/OEntN6NNO5yeh8DPWasYWJ1r5eceEvoI5jzG5muqG6etOewxhEAg"
                + @"CiAvtTAEZPV7BOFNCPqSyZEDV2VfU8SS/Jj7snJK9AS2c6LH7Hl7vP0d+6h3yW60B3aItpbAA7VtA9O2"
                + @"3XpgWYpWIchTEvVaQz5aCYJbiPVuTzpFs2ES3WjWV281iDlgTG+aAp/Z2enILndj215HkIBl0dEiTWmz"
                + @"LApPKqqQFMXYyrNndu3i2TOWH43gC4ZAKtrooWe1HISFIBzrfHPNC3RXkVB+uegEIImZyh9UwQEcvQ3x"
                + @"VYxMcSAZxqIcFeQ2+OvNHUsJgW8jR+/ODu19TMpDzNUkJSTnmQVRnuitZFnfjZEJD5QXcGHjOycASQhU"
                + @"OeXyFvbDCQmYBE75CoII43RLSXBUYuo1F/SI5x8o/PUcMLMtNChVLsj9CqU1qhxpFhktQs3dpFYIhZBU"
                + @"tCe59je/oV+KaebAzqxv2nmtfg3gwezM+oqW2Y98rZdgc+aktrG1aS8897wtJlJvLtC4lqJ6BCKmaAsB"
                + @"fD5EjDrFHUf0dFXRZdFKvYsZdBa+6cm+TKvOi8Aa8yTyWY4wCXLaSKDWiC3IjSrM3N52au/jd94b7tkd"
                + @"wv2S/uVH9LMuWtQBnqKHw3dSBEPTt2t2A60CGOcDNcCqyIoGOOGfsuTiiUUgUKog8ghc97jvIcw66PS5"
                + @"N7NtEtJM6wp4dkEor7FJ+dIWDJK9jKCn1mD4F6SEPF2cQAGcYtwO9L5w5qxYh4ZjpQjb67LyNzyiFPuq"
                + @"z5ToAf+OMpHV5fMXbLvdsyWOriIS8e9+6mEAXDEoaA8dnjAoVB/MVOVIBfQCoivOacNFaY0csaI6ZxCl"
                + @"nmOGCATaOO1O1AH4pc1EuO01W1zasaMzffsNSn8jlynBgWMutQhdrw1Kg/QDGP6NnhN4QgEcr35WJleM"
                + @"UWrgmqO7AUqaIwY5YzBFaKuiLhG4BMdj7jqIM3tAPnInQ5PQcS36SAiv/RX9UrkQj8tcU/1XG0FyKb8r"
                + @"Oy7U6UM01jLfHLr2s7ad2yYxxhpIQJWFizEartJrN645sL3JBQgOkIA1VO57z121ejjG+eZoDi2LwM6Q"
                + @"VZWNFFqiLDWIZ6CC6qo40uIIJdBAG79lxRwxnCsuYSkI+ctXmiUF+RoENOlVbHatdeWsFRd3bLhBEjkg"
                + @"usFJuwPGHEqD9JOFyBpNnep/VZpT0hYxR2bU3abbQgEWLgTGaMlUDiERMiJNvWU5Q2NGvYE9Jqq6lerz"
                + @"XAla06VfzFohYpa+jNYVFFehTwGQEzuD9M+XPCmnIYJUUr+Y5LiP83YBk+bMVK4EHfxlL9rwp/wrFFBK"
                + @"zFn9LIk+qv3m1RdtK1FkQhDgSZGILOzYXfFAMueECPVEa4SrgPxKEVEUKKyuyVGKQTJxYlJMNNQl3tcX"
                + @"L6aY1QKAE5LRDEZoXGaOehRr2ONzO3bm+Rfs7KUXbdDfJrDQGFVYGInsBTgpzSbAenLAeTHFYRQGq5Or"
                + @"CwIQMdWIA7TQNQnigpymIOk8Tnt2f6nRAS1jIvLC3+kDRNIc/xY1+Gj1poi0hDkayuKvmz6NUijjj9CK"
                + @"zaRjV8+es+01Jc/m42kdNJ8nIUjtH4sg6oND3iLRDUwQ4TWncJ4c4vtXr2E/584cISIEThBdbdlx7Vld"
                + @"OFVXTNAhf2FMeOlKTPKix4gGgwaBgDOJLef1zWoxR44pTbUSJ7HpOLcpxF9s9m393Hm7dOmq9bsbVuGr"
                + @"FpjmQlMX3hdFfQtmEbcBTyXAxB+/9pUSVAlSaOgfQQX3qijRYIS0g9AkfRLgjj3B3zwiqd7vtQkYxBxN"
                + @"VRD8Ko1C2wwi62v0EhpCYReYOgVvRasaJYEOr+5etMsbW8q20GKuCRw0aEF/+t5qDf7y7AAEdeC2xoQ0"
                + @"mtpD9aL50n7wyov+w3Ja+ODf1YSjNO81SJqj7tvmIJxWR6qr4teb81J5Pe5nGmbLZCJRBbZ7OD4m2cNE"
                + @"ECH10SIJ8HDvyOZHhXUhUDYgjNbIOfcoohNhaqIjzftARVCBE5Smyy/Kl058HZBNceYoN0JQcM6LHF8C"
                + @"oZOsD+gdtFcrUMl50Jq9fttyjX4UCDVapsFSLvvItARe6YE7eWmjwNO1MicXW9hLO2ftTI+wuipsNhnZ"
                + @"6AgcobWUQB8ALOg7YOIw4rYUUtOJTvZQwc1Oy/6X//a/s8P7d0MOICKSKdfs6/2Vkn2oDSCosEuiKCBq"
                + @"wwD2Thdnwumzuj0I6kkRYZNORmLZ9sFOtRVhFhQcdAmx4xJCgKfmdhY0GCO9/f6g+QE9mZQwJrgivHTa"
                + @"i/e92qFqI1E9BY6X8JgTCIysQ/jcgaJLiF9OZbqAJ+pZgrYeA9898pPe+bO2jt9Y4GCmJJYawy5gaDEn"
                + @"6gLWDmFxChMiraEbHlq1f2DfvXzZrqyvG3kq5CaZhplaAeUMpm/963TxZyEvoLDVD+ZxFQbwFDYcYbYL"
                + @"Gz176dJFIrcJ+ceQCAOiiSmIdH+A/YcQRTX3NW6r4jg3iJ4UJ4iqiBNMnu4LloQDZy4E9ZNN5ZRPeGEa"
                + @"9HtwvpCQYw9Gmhs1uRWKGgxtnNTVrhfuP8WY8H2BcNGnN+QbXIJUOadwG5j8p56Vm0CScE/KLZnNieKO"
                + @"NLUw6FpPhNbniEks5bOFi+bC/Buo7QxrcGApwt5CM87C8O89d8m2MtrSIhGslUYtVIO0OkSwQ+6GP0os"
                + @"fd6EBhVe+i+aIwkKTzfQnh+8+oqvNR7gtMaP96yLs+qizjUqqcRVIaf/bnODbCjqxXsKhfY11VBpvI2t"
                + @"04nb5YdkczWBp2/H6Ku3PkwEgm4+xTQRq7nXB1kbZjpxqf4iMfeeIHjCFdXfLv47p57PqB0xAxnF8Xlt"
                + @"HJRDLxiAxeewVAWfw6PV15HNNQJPWD3Y2bL+1hpaI/NGXsi1DNOUwbDR6Ajtblut922Gx/bnb75pF9e5"
                + @"li3xpSKL4JZQ0q6TS1vhJMGEUGKOiCcZDOuNNS6EEyPc6HDupXO79vbLL1l1eIgzQ4SKGfdyXQwBIGXT"
                + @"NOcNhsKOI93sU7TrfXl/4bLg8QFTZ1BA3okg5jSMCaZJRGmOuUc/fKGIRuedMSKy9j26Cs+G55rntbs6"
                + @"Ak/3cSdRieCAGYpKPCumgp3uDYU9ZxDm3pmkY85G+iGn8P5Qb2PDepsD7qR/2lZCL5LKmqQEB4oR8sN9"
                + @"e+ell+zljYH1oK9eoXR1lEA5M2jUK22raz2vhdueWAqBhki6Sa9iKPxbzmtbh2tvXbtm17Ct+u6F3k7T"
                + @"i7maU1kCtNYpi0EnDYe2aQYgdaIhTtg2N6yKzrmEflFdZenHQ1rF3hIU2Vi2zkANkcg+c62mrrL7MHam"
                + @"6o1SQ1nhreLgqKo97nHaEB4qkdXclP+ci5gDzqs+HQYN67hto4pZ8vwVJg5zq88Nt9EUjaToJzs1jSHz"
                + @"pmCgraDgcGxX1jbt7WtXbB1LlOdDy+sxcGiCEVo3AiYGhW2AMSpoUF36vDeEJgVy36M3CqT+i3xmMZn7"
                + @"Lsz647fetN1eB47mOElCbAWJS9xzw5yTZPN0ccKsmNTUrzDIj8QYEeukimoNwBDJR72BR8xJtKZAGt5c"
                + @"dzOl+1fMVaeUk1781KpfquCU5rDVbvgIhLgh2wExibTCSAitasDMTaAYpLFiVQVNivvxOUpFdAuqIrcb"
                + @"40vEnJJQsiIw6MDl9qSwP3r5Nbu8xv3zMbmyhEqLVnAd9CFtC4wJYPoWSCL9dqXMjMJoqOwDeXr/EsUL"
                + @"GS1brerUGpOLGx374fe+Y5uaS8HP4HSIngpXYamyinfie6ui81Sa95FpV4TALN0XchyEAIT0fqcTugHW"
                + @"QVR7TXXtQBojXzoVjsUUmTbvQDhQf4tB9OXN6cjBaa7zX4zxRFGa05g03RM+CgttBJMLhgBXbbRH5riM"
                + @"fXo/ymlIn5OhXx/YRbC1TEyjL+m4tP/61e/b5XafiIzkejkzDWysRiIExEpuAmOgSyBoEBjawIRLWmlM"
                + @"wTiNEDjCHBLSVPP5ODjaycgrXnnuvL3+8oseEMxnY54BCPadQE7A0NFJER0oYobegfG5HZp2egoY6CHa"
                + @"BCIAqDMmENv9iarOyR1wVjY/bXyOGONiK9ND365tfpf2w1b/AzjqTGfZ+m64N/ApBASSlPCDSzJr3Ks+"
                + @"6TvMV0l7RExN/LElYdSv9GqhfExOqHBblkf/VlanRRL9xnPX7MfPb9kZnpxPZ/6xiVLT5bUmNGWSha+q"
                + @"oBFcDhVb6JRq9YdsqGAWUIIcIvribw1UwSj/nQMis4wQu03C94NrLxIknLU+RE0URlcECGiQfiFKzk/h"
                + @"Z62RWaQxijsg1UWSiO60FsC/0wYiLo0CRh1LYlEd0yfCCEGWXbYaNkdflzwv8yH7jr1YoNmFBg/JabTi"
                + @"azX248TVMRGUvs+jTyG781f7wkkIN/3pp8vcJHF/hcP2UQmiJ8EkBnuAUlA1miFlcf6LkVxD2v1dIg2j"
                + @"g0vdJkbrQmbUhEyG++eWzIbWnh3Zdy6t209e34Q+YNWn5fnUMvk04eHflm/Tlt6SoGpxJLAr0hMm6qv1"
                + @"sPkpSoEfig6DDK7Q0iChfmFpSWJVKiwctO12kds/3vrMPr5z18ZIcpL0YHAXxsiuRv6pfY2HJYO+h895"
                + @"OUEDCSc168pfUGcrB6xtQ2hJsM4jPi7RThBtVxohKsFYUUylATpA/HQlSGVgXNAaF0f2Q/tOFPZPVg05"
                + @"JVQpknAxmCdaJKOLJbTQegVfRowPxo+kRLN6i+5i1rX/6Sd/bIO8sp7GCMHBfTjWQWst9WqhGJXKjJ4U"
                + @"YGv2VJ7yd0KlDeQhksY4Q0pim4DRg/HUPr57zz68fctG2NyEiGTOVr96qI9H6Jc8YoBEzGyWizlInEc8"
                + @"TTnpOZAWReZvqJAHuum8tuGqiKPPGsspf/vSMMf7UZMNEM4k7QdGBIiaY+fiqmifq1qNE6dWFvrw+Mza"
                + @"qFeGVqXsX9natD/+/vdtlzBbbzn44DHt60kxV59RkSkPWhLg+LryVMyRdZe90sxeC3X3b/TrQ6iYvPuj"
                + @"0t6/dds+eHTfHmNzrd/zDzBoiVSnu+Yhakm0giXA4njWBWoCltDUUzkxS1WOiI3MkKIlwPaqXELJno9V"
                + @"oW+aPvex+G9b1ImYEzRnRXffrDRDps9h0b4u6BJ3iE/h0NK1jhV1bjUCmhI9JviT7nxmV3d37L955wd2"
                + @"luCpmGHy9ZDXoKEqq+jVgyjh+w3lqZjjPEcVtafRXy3A1gxgO9U7LWbDydL+8c4t+8WNz6zc6FuO6ZNv"
                + @"KLlYzrR8NyaA6OFDZS4F2Mp0aLQbprjUrpjDoQikSIlDX1Gqc8IDMOSTV1btWYqT5CuYqw9njPoXAyXU"
                + @"zbmTGyiCLM6ESWkJaUYymVt8NLTXtnbtz//wJ7ZDLqOVNVpAqajN8xgeZderfvZYfuzk58S+oTydWQMY"
                + @"z3fkOInm9NkS/d6NVkDqA1ii6wgf86vb9+3n9z63PZzjste1MblQEnVskPVtcjSxpIOzR6vcfKHXvmxX"
                + @"NVDF+wkYhMhI7+vo1w9rrQQSR/BPrbJPPqhxi29bhK4irlMcdi4IhJXGyMBJ+wPxvghvw3UJUj0/Ai80"
                + @"eTi39nhOHvO6/cmrL1itD0vgV7RkqwJ+/caOXqVUryvGKNCSH/cpErncbygBqqcoCRKcwRjN1ml+p9Yn"
                + @"UhrbL41vY/Z+9Ool+x//8I/sUtS2yee3bRu/M+D8/pM7lhKttADUJVH/gLSJXh1AAaofaAjSRKMikjbK"
                + @"a1Q9dAVpYekXv311YZDRdwYonxEcSDtwBEboNnVEf2iAhox8e1IXhohYfvuebRG0/A9/8if2zndfMII1"
                + @"625gukVwIld1p140mYlYsSV3Uf+c+13mbFWeWnP0gqzWf2lhe6Xf15EPyAh9cYz6cIR+lEjOTV0Cr713"
                + @"84H9Hz//O5sNYtt69Yr/eIVV8kHSnoB4SCzDvhPCazgVBjcJXbWFIF5AbuV/vnVR26rshCSTU3TqQSPn"
                + @"tB/611bCIDj8ST3kNcLBp/uP7I+uPmc/eOO7mqohzyltvU04rNQDUz0eE07rI+X+iBrQebWJ3VAnFH3A"
                + @"QhHcN5WnZE6I4aUtGUzQ75Dp58H00Tz94u5cA3yEhQkqoAV5euGVtMRujub2f3/4D/b+/m1LtrYB/BxY"
                + @"aJpBmCvPcQqEYxAIzAFwZe0a7/Itx1QfLab4L/FqSOVbFtca4PQ35mRuYErYNnAInoYpJwOx2ir3wRQp"
                + @"xE7J9/6URPyNnS1Luph2TJi0ryDH8fXPtNPTj6uX7KsdNd0whKPGh7p4+JlvKk8ZEACVBuk8AZPHABKk"
                + @"V6EuSi698ukGvWUm/14gRSV+KeqnNiSA+OX1j+2fPv2NTbJ1orkt0/uOmqjTp7E0aKgfYpCAauWJFMV9"
                + @"i+c9yoMwPTDK1zvTfx2NoNCUvmCY/JdvqfSn7VdLmAQ8VWg7qvU2mQakEBBg0LImmeiKKv+mYXwRXCs3"
                + @"LS+sOp5Ye1rZ+c66vX7xeXvtwkW7SK7XwQ8qB5Yg6sMXhYQNKAWpXmZOMMOSI1UgdKZoVac+lqGRmfAL"
                + @"9c/MHEk5zHEnKik+Vf0Y4rH1OVRyoJIOSyROP1KaaWkRyesjIpq/vXXLPj04sNl0al1C7nZv4OsW5hCg"
                + @"RZge6wuGIKLEVBLmW+yOM0iYkSOlHcLTZMp1QSLYVPjbYCEG6fsJKj6riCQ7g/Rfx7RXzdtQlIQZQgEu"
                + @"7VEl3gpkIHhLISFMGu8/sXI4tB0Cmu9ffMHevPC8vdDfsJ76cm0n2kyX/ga5vplT0o6KtEXjanqlyZkj"
                + @"7acQtDpz9KXHClLp9frMpfHry1Mxh+AZS6DkEoJztw9gwhjh4ytAIdNSwykaChEBeMZh19A+qt0B+6TT"
                + @"tlvHIzvAJNyFQdfv3rEHo7HV3Z5FgzX/7EpeCZsM4iC1+Bb1ozEsw8/5PL2mygfk1EnoY1W+YFIovnr/"
                + @"S2dOFRhd6tdNMG1acBFlaDjt0RkwozWao5rNrB4N7dzaur107py9fPGinVkb2GYX2CBopZ+JwfxBY9cA"
                + @"Tyo5hiQIqpgjrfAmnU4qQWtARVZDwQf7Gbi1ae+bylMyB4AQATlwSYJWNgYggnqqf40aVGzzQl/daBGl"
                + @"6QsaXCMB9Q+Ao0EtkNPE+hPM3qM8t1uTqX12dGR3hyM71tS4Zg/7mD6RWwKAGmmWVK+Aa4RCfepVEn8H"
                + @"SP9cMzjpW0HaoHLavDWnnIPacKPG/pRrhZU/IqpWJeiHMgryltI26fs7Z8/bC+ubdvXMjq2RcOq6DF+h"
                + @"ZxL5QzE4+EjBtWKKtu6+nCmhUymOgg9pi6q0R8xq/z6YoyKkdKNLCnVVxCghq2ta96bF43r72T9BKcCJ"
                + @"7FINgmqpkF4v0X0AJiD1U4xPxhO7f3hgj0ZH9mQ4tv0R2oSm+ScgYUKwOWiRtvg5fVIZVnvfKuo3HK3O"
                + @"NQyDQfqn4r7I/+ufvuEpwqIdsxztrqwDIbeA7TyaIm3Z7nft7Oa6PuLEvVqaJVgUFKHBmOECDdZqJU3g"
                + @"eEJJd9q6b2FfQiu4xB8xQbGMYJa26Jxfo0ropLTfVJ6KOVJPf7udO+eYgqkUAIj0YA8roI/RKapR+Ftl"
                + @"qC25jf9oNkRKCAj0jYNWq21DvKe+vijTLIQy7hHeWrkyIfR8MoY545EdYsYejia2h586KBamQZCFlj7h"
                + @"k7CPIAthKOD5lQLxxRQkUyW8OCu95x84BC0rbNAtrJuiIcvMzsV9u5yt2RWClV1MV0+TiYoVKDkknXsy"
                + @"7B4QuNlierVis8oSAoHIo1P5l1VyKeaoiAkSYmkMl/14VVzDnHpfh8MX5ek0hzu0RElNaePSoH9Aoo4E"
                + @"uMIszWdozZbmH4LU6B7uhShaxVnh4P2X4DnWB7zRByRHzxJy0rqkS1+2nYD8wWxqTyYwazaxY0zgtNT7"
                + @"oRAMIdGr+Vreqh/BqLhXW9WTFfyEse7sMaUJ0aAiQn2C2X/BFni24fNWO7Kt7sC28Xebnb715Odo32km"
                + @"CvN8RRSnpVb65UHhptlOTUZqZY1wU5VvlRkXovz1x530wt/3Q9E1pxX4rjRMAYKT9RvKUzFHNyhcFhjy"
                + @"IzJl4SxRGXqpMFIM0Ad79GlG/yYOxFDGn6N2U6rkt6d5C/b8Oy9ImRiu0XYfmXGu16ZfQk86sK2LUUQy"
                + @"KyR3WsxslE9sjgk6HuqXf7m1YchXGSSoNHouRmQwSd8J0BqwLmZL69v0KZkdbtLnTISS8lk5aA3hu2/g"
                + @"efkLCU5Y7K6Tp0gkE0kRTRWjhnylEVrOhRHnlV6IGdIqKlu9taGkWhE6BAM/hJFI75vKUzNHis3toSNB"
                + @"0ZwNDo6OBLOqEjY6DtJF6gokJUzy1/8EENGb3tOUrqdQxj9wrUPMl2ZjJeHeVZAFTxLD7CjIU5XzKLRW"
                + @"Xy6r+t8QzIGgDw06npSvYCfYMo3IKjKkGRHVp+rVl4+mcxOPy3Tpk8XoCUoEsWkHKEIjFN228sPq1+Fj"
                + @"N2z5oxt0iRuCxoS+V1qjB33YSnh+bTH7L6r4JtUrCcTJAAAAAElFTkSuQmCC";

            }
        }
    }

    public class AFileEntry
    {
        public bool Typ { get; set; }
        public string Name { get; set; }
        public string FileSize { get; set; }
        public string Dirs { get; set; }
        public string Files { get; set; }
        public string Flags { get; set; }

        public AFileEntry(string name, int filesize, bool isdirectory)
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

            Dirs = "";
            Files = "";
            Flags = "";
        }

        public AFileEntry(string name, int filesize, bool isdirectory, string flags)
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

            Dirs = "";
            Files = "";
            Flags = flags;
        }

        public AFileEntry(string name, int filesize, bool isdirectory, int directories, int files, string flags)
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
            if (directories > 0)
            {
                Dirs = directories.ToString();
            }
            else
            {
                Dirs = "";
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

            Flags = flags;

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
