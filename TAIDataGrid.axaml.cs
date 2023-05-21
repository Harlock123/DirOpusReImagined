using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Controls.Shapes;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Media;
using Avalonia.Metadata;
using Avalonia.Threading;
using System;

using System.Collections.Generic;
using System.Drawing.Text;
using System.Globalization;
using System.Reflection;


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
                items = value;
                selecteditems.Clear();
                this.TheItemUnderTheMouse = new GridHoverItem();
                this.GridXShift = 0;
                this.GridYShift = 0;

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

        public void ReRender()
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

                if (this.showCrossHairs)
                {
                    RenderCrossHairs();
                }

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
}
