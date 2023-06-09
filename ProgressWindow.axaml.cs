using Avalonia.Controls;

namespace DirOpusReImagined
{
    public partial class ProgressWindow : Window
    {
        public ProgressWindow()
        {
            InitializeComponent();
        }

        public ProgressWindow(string title, string message)
        {
            InitializeComponent();
            Title = title;
            MessageText.Text = message;
        }
    }
}
