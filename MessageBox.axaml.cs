using Avalonia.Controls;
using Avalonia.Markup.Xaml;

namespace DirOpusReImagined;

public partial class MessageBox : Window
{
    
    //private TextBlock messageTextBlock;
    //private Button okButton;
    
    public MessageBox()
    {
        InitializeComponent();
        messageTextBlock = this.FindControl<TextBlock>("messageTextBlock");
        okButton = this.FindControl<Button>("okButton");
        if (messageTextBlock != null) messageTextBlock.Text = "A Message Goes Here";

        if (okButton != null) okButton.Click += OnOKButtonClick;
    }

    public MessageBox(string message)
    {
        InitializeComponent();
        messageTextBlock = this.FindControl<TextBlock>("messageTextBlock");
        okButton = this.FindControl<Button>("okButton");
        if (messageTextBlock != null) messageTextBlock.Text = message;

        if (okButton != null) okButton.Click += OnOKButtonClick;
    }

    private void InitializeComponent()
    {
        AvaloniaXamlLoader.Load(this);
    }

    private void OnOKButtonClick(object sender, Avalonia.Interactivity.RoutedEventArgs e)
    {
        Close();
    }
}