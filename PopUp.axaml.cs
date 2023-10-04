using Avalonia.Controls;

namespace DirOpusReImagined
{
    public partial class PopUp : Window
    {
        public PopUp()
        {
            InitializeComponent();
        }

        public void ClosePopUp()
        {
            Close();
        }

        public void SetText(string text)
        {
            TheText.Text = text;
        }
    }
}
