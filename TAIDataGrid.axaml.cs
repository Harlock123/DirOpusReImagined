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

        #endregion

        #region Public Methods

        async public void ReRender()
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
                        }
                        else
                        {
                            rr.Height = this.GridTitleHeight;
                        }

                        this.gridHeaderAndTitleHeight += this.gridTitleHeight;

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
                                                image.Source = this.CheckMark;
                                                image.Width = this.rowHeights[rowidx];
                                                image.Height = this.rowHeights[rowidx];
                                            }
                                            else
                                            {
                                                image.Source = this.RedX;
                                                image.Width = this.rowHeights[rowidx];
                                                image.Height = this.rowHeights[rowidx];
                                            }

                                            
                                            // Set the position of the image on the canvas.
                                            Canvas.SetLeft(image, left + 2 - this.GridXShift);
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
                                            Canvas.SetLeft(ttb, left + 2 - this.GridXShift);
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
    }
}
