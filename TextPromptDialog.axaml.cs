using Avalonia.Controls;
using Avalonia.Markup.Xaml;

namespace DirOpusReImagined
{
    /// <summary>
    /// Asks for one line of text. <c>await new TextPromptDialog(...).ShowDialog&lt;string?&gt;(owner)</c>
    /// gives the trimmed text, or null if cancelled or left empty.
    /// </summary>
    public partial class TextPromptDialog : Window
    {
        public TextPromptDialog()
        {
            AvaloniaXamlLoader.Load(this);
        }

        public TextPromptDialog(string title, string prompt, string initial = "") : this()
        {
            Title = title;
            this.FindControl<TextBlock>("PromptText")!.Text = prompt;
            var box = this.FindControl<TextBox>("InputBox")!;
            box.Text = initial;
            this.FindControl<Button>("OkButton")!.Click += (_, _) =>
            {
                string text = (box.Text ?? "").Trim();
                Close(text.Length == 0 ? null : text);
            };
            this.FindControl<Button>("CancelButton")!.Click += (_, _) => Close(null);
            Opened += (_, _) => { box.Focus(); box.SelectAll(); };
        }
    }
}
