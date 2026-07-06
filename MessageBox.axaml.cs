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

    public MessageBox(string message, string title = "Message")
    {
        InitializeComponent();
        Title = title;
        messageTextBlock = this.FindControl<TextBlock>("messageTextBlock");
        okButton = this.FindControl<Button>("okButton");
        if (messageTextBlock != null) messageTextBlock.Text = message;

        if (okButton != null) okButton.Click += OnOKButtonClick;
    }

    /// <summary>
    /// Confirmation variant: shows a Cancel button and returns a result via
    /// <c>ShowDialog&lt;bool&gt;</c> (true = OK/confirm, false = Cancel or close). Optional custom
    /// button labels (e.g. "Overwrite"/"Cancel").
    /// </summary>
    public MessageBox(string message, bool showCancel, string okText = "OK", string cancelText = "Cancel",
        string title = "Warning")
    {
        InitializeComponent();
        Title = title;
        messageTextBlock = this.FindControl<TextBlock>("messageTextBlock");
        okButton = this.FindControl<Button>("okButton");
        var cancelButton = this.FindControl<Button>("cancelButton");

        if (messageTextBlock != null) messageTextBlock.Text = message;
        if (okButton != null)
        {
            okButton.Content = okText;
            okButton.Click += (_, _) => Close(true);
        }
        if (cancelButton != null)
        {
            cancelButton.Content = cancelText;
            cancelButton.IsVisible = showCancel;
            cancelButton.Click += (_, _) => Close(false);
        }
    }

    private void InitializeComponent()
    {
        AvaloniaXamlLoader.Load(this);
    }

    private void OnOKButtonClick(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
    {
        Close(true);
    }
}